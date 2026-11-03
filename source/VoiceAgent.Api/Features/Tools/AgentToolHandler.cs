using System.Text.Json;
using System.Text.Json.Nodes;
using VoiceAgent.Api.Features.Calls;
using VoiceAgent.Core.Agent;
using VoiceAgent.Core.Customers;
using VoiceAgent.Core.Verification;
using VoiceAgent.Shared;

namespace VoiceAgent.Api.Features.Tools;

/// <summary>
/// Runs the function tools the voice agent calls. All sensitive checks happen here, on the server:
/// the model only ever sees pass/fail and case details for callers who have passed security.
/// </summary>
public sealed class AgentToolHandler(
    ICustomerRepository customers,
    CallTracker tracker,
    AgentQueue queue,
    ILogger<AgentToolHandler> logger)
{
    public async Task<string> HandleAsync(CallSession session, string toolName, string argumentsJson, CancellationToken ct = default)
    {
        JsonObject args;
        try
        {
            args = JsonNode.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return Output(new { error = "The tool arguments were not valid JSON." });
        }

        try
        {
            return toolName switch
            {
                ToolNames.RecordCallReason => await RecordCallReasonAsync(session, args),
                ToolNames.FindCustomer => await FindCustomerAsync(session, args, ct),
                ToolNames.VerifyIdentity => await VerifyIdentityAsync(session, args),
                ToolNames.GetCaseDetails => await GetCaseDetailsAsync(session, args),
                ToolNames.TransferToAgent => await TransferAsync(session, args),
                _ => Output(new { error = $"Unknown tool '{toolName}'." }),
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tool {Tool} failed for call {CallId}", toolName, session.Id);
            return Output(new { error = "Something went wrong. Offer to transfer the caller to a colleague." });
        }
    }

    private async Task<string> RecordCallReasonAsync(CallSession session, JsonObject args)
    {
        var reason = Str(args, "reason") ?? "Not stated";
        var category = Str(args, "category") ?? "Other";

        await tracker.UpdateAsync(session, s => s with
        {
            CallReason = reason,
            CallCategory = category,
            Stage = s.Stage == CallStage.Connecting ? CallStage.IdentifyingCase : s.Stage,
        });
        await tracker.AddEventAsync(session, $"Reason for call: {reason} ({category})");

        return Output(new { recorded = true, next_step = "Ask for the matter reference or client number." });
    }

    private async Task<string> FindCustomerAsync(CallSession session, JsonObject args, CancellationToken ct)
    {
        var clientNumber = Str(args, "client_number");
        var caseReference = Str(args, "case_reference");

        Customer? customer = null;
        string? matchedCase = null;

        if (!string.IsNullOrWhiteSpace(caseReference))
        {
            customer = await customers.FindByCaseReferenceAsync(caseReference, ct);
            if (customer is not null)
            {
                var wanted = Identifiers.NormaliseCaseReference(caseReference);
                matchedCase = customer.Cases.First(c => Identifiers.NormaliseCaseReference(c.Reference) == wanted).Reference;
            }
        }

        if (customer is null && !string.IsNullOrWhiteSpace(clientNumber))
            customer = await customers.FindByClientNumberAsync(clientNumber, ct);

        if (customer is null)
        {
            await tracker.AddEventAsync(session, $"No client found for {Describe(clientNumber, caseReference)}");
            return Output(new
            {
                found = false,
                next_step = "Ask the caller to check and repeat the number once. If it still isn't found, offer to transfer them to a colleague.",
            });
        }

        session.SetClaimedCustomer(customer);
        var verified = session.VerifiedCustomer?.Id == customer.Id;

        await tracker.UpdateAsync(session, s => s with
        {
            ClaimedClient = customer.MaskedClientNumber,
            CaseReference = matchedCase ?? s.CaseReference,
            Stage = verified ? CallStage.Verified : CallStage.Verifying,
            Verification = verified ? VerificationStatus.Passed : VerificationStatus.InProgress,
            CustomerName = verified ? s.CustomerName : null,
            CustomerId = verified ? s.CustomerId : null,
            VerificationAttempts = session.FailedVerificationAttempts,
        });
        await tracker.AddEventAsync(session, $"Client {customer.MaskedClientNumber} found{(matchedCase is null ? "" : $" from matter {matchedCase}")}; security check required");

        return Output(new
        {
            found = true,
            already_verified = verified,
            next_step = verified
                ? "The caller is already verified. Call get_case_details."
                : "Ask the security questions one at a time: date of birth, postcode, memorable word. Then call verify_identity.",
        });
    }

    private async Task<string> VerifyIdentityAsync(CallSession session, JsonObject args)
    {
        var customer = session.ClaimedCustomer;
        if (customer is null)
            return Output(new { verified = false, error = "No client has been found yet. Call find_customer first." });

        if (session.Summary.Stage == CallStage.QueuedForAgent)
            return Output(new { verified = false, transferring_to_agent = true, next_step = "The caller is already waiting for a colleague." });

        var answers = new SecurityAnswers(Str(args, "date_of_birth"), Str(args, "postcode"), Str(args, "memorable_word"));
        var result = IdentityVerifier.Evaluate(customer, answers, session.FailedVerificationAttempts);

        switch (result.Decision)
        {
            case VerificationDecision.Passed:
                session.MarkVerified();
                await tracker.UpdateAsync(session, s => s with
                {
                    Stage = CallStage.Verified,
                    Verification = VerificationStatus.Passed,
                    CustomerName = customer.FullName,
                    CustomerId = customer.Id,
                });
                await tracker.AddEventAsync(session, $"Security passed for {customer.FullName}");
                return Output(new { verified = true, next_step = "Thank the caller and call get_case_details." });

            case VerificationDecision.RetryAllowed:
                session.RecordFailedVerification();
                await tracker.UpdateAsync(session, s => s with { VerificationAttempts = result.AttemptsUsed });
                await tracker.AddEventAsync(session, $"Security failed (attempt {result.AttemptsUsed} of {IdentityVerifier.MaxAttempts})");
                return Output(new
                {
                    verified = false,
                    attempts_remaining = result.AttemptsRemaining,
                    next_step = "Say you couldn't verify those details, without saying which was wrong, and ask all three security questions again.",
                });

            default:
                session.RecordFailedVerification();
                await tracker.UpdateAsync(session, s => s with
                {
                    VerificationAttempts = result.AttemptsUsed,
                    Verification = VerificationStatus.Failed,
                });
                await tracker.AddEventAsync(session, $"Security failed {IdentityVerifier.MaxAttempts} times");
                await queue.EnqueueAsync(session, "Could not pass security questions");
                return Output(new
                {
                    verified = false,
                    transferring_to_agent = true,
                    next_step = "Apologise, say you're transferring them to a colleague who can help confirm their identity, and ask them to stay on the line. Do not ask any more security questions.",
                });
        }
    }

    private async Task<string> GetCaseDetailsAsync(CallSession session, JsonObject args)
    {
        var customer = session.VerifiedCustomer;
        if (customer is null)
            return Output(new { error = "The caller has not passed security. Do not share any details." });

        var reference = Str(args, "case_reference");
        var cases = customer.Cases
            .Where(c => reference is null
                || Identifiers.NormaliseCaseReference(c.Reference) == Identifiers.NormaliseCaseReference(reference))
            .Where(c => reference is not null || !c.IsCompleted)
            .ToList();

        if (cases.Count == 1)
            await tracker.UpdateAsync(session, s => s with { CaseReference = cases[0].Reference });

        await tracker.AddEventAsync(session, cases.Count == 0
            ? "No open matters found"
            : $"Shared progress update: {string.Join(", ", cases.Select(c => $"{c.Reference} ({c.Stage})"))}");

        return Output(new
        {
            client_first_name = customer.FirstName,
            matters = cases.Select(c => new
            {
                reference = c.Reference,
                matter_type = c.MatterType,
                property = c.PropertyAddress,
                stage = c.Stage,
                case_handler = c.CaseHandler,
                latest_update = c.LatestUpdate,
                waiting_on = c.WaitingOn,
                next_step = c.NextStep,
                target_exchange_date = SpokenDate(c.TargetExchangeDate),
                completion_date = SpokenDate(c.CompletionDate),
                last_updated = SpokenDate(c.LastUpdatedOn),
                milestones = c.Milestones.Select(m => new
                {
                    name = m.Name,
                    status = m.Status switch
                    {
                        MilestoneStatus.Done => "done",
                        MilestoneStatus.InProgress => "in progress",
                        _ => "not started",
                    },
                    completed_on = SpokenDate(m.CompletedOn),
                }),
            }),
            note = cases.Count == 0
                ? "There are no open matters. Ask how else you can help, or offer a transfer."
                : null,
        });
    }

    private static string? SpokenDate(DateOnly? date) => date?.ToString("dddd d MMMM yyyy");

    private async Task<string> TransferAsync(CallSession session, JsonObject args)
    {
        var reason = Str(args, "reason") ?? "Caller asked for a person";
        await queue.EnqueueAsync(session, reason);
        return Output(new
        {
            queued = true,
            next_step = "Tell the caller you're transferring them to a colleague and to stay on the line. Then stop talking.",
        });
    }

    private static string? Str(JsonObject args, string name) =>
        args[name] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    private static string Describe(string? clientNumber, string? caseRef) =>
        caseRef is not null ? $"matter reference '{caseRef}'" : clientNumber is not null ? $"client number '{clientNumber}'" : "no number given";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static string Output(object value) => JsonSerializer.Serialize(value, Json);
}
