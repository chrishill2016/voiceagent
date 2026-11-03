using System.Text.Json;
using System.Text.Json.Nodes;
using VoiceAgent.Api.Features.Calls;
using VoiceAgent.Core.Agent;
using VoiceAgent.Core.Cases;
using VoiceAgent.Core.Verification;
using VoiceAgent.Shared;

namespace VoiceAgent.Api.Features.Tools;

/// <summary>
/// Runs the function tools the voice agent calls. All sensitive checks happen here, on the server:
/// the model only ever sees pass/fail and case details for callers who have passed security.
/// </summary>
public sealed class AgentToolHandler(
    ICaseRepository cases,
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
                ToolNames.FindCase => await FindCaseAsync(session, args, ct),
                ToolNames.VerifyIdentity => await VerifyIdentityAsync(session, args),
                ToolNames.GetCaseDetails => await GetCaseDetailsAsync(session),
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

        return Output(new { recorded = true, next_step = "Ask for the case number, or the postcode and surname." });
    }

    private async Task<string> FindCaseAsync(CallSession session, JsonObject args, CancellationToken ct)
    {
        var caseNumber = Str(args, "case_number");
        var postcode = Str(args, "postcode");
        var surname = Str(args, "surname");

        ConveyancingCase? match = null;
        var multiple = false;

        if (caseNumber is not null)
            match = await cases.FindByCaseNumberAsync(caseNumber, ct);

        if (match is null && postcode is not null && surname is not null)
        {
            var found = await cases.FindByPostcodeAndSurnameAsync(postcode, surname, ct);
            // Prefer the one open case if the others are completed.
            var candidates = found.Count > 1 ? found.Where(c => !c.IsCompleted).ToList() : found.ToList();
            if (candidates.Count == 1) match = candidates[0];
            else multiple = found.Count > 1;
        }

        if (match is null)
        {
            await tracker.AddEventAsync(session, multiple
                ? $"Several cases match postcode '{postcode}' and surname '{surname}'"
                : $"No case found for {Describe(caseNumber, postcode, surname)}");

            return Output(new
            {
                found = false,
                multiple_matches = multiple,
                next_step = multiple
                    ? "More than one case matches. Ask the caller for their case number."
                    : caseNumber is not null && (postcode is null || surname is null)
                        ? "Ask the caller to check the case number and repeat it once. If they can't find it, ask for the postcode and surname instead."
                        : "Ask the caller to check and repeat the details once. If it still isn't found, offer to transfer them to a colleague.",
            });
        }

        session.SetClaimedCase(match);
        var verified = session.VerifiedCase?.Id == match.Id;

        await tracker.UpdateAsync(session, s => s with
        {
            CaseReference = match.CaseNumber,
            Stage = verified ? CallStage.Verified : CallStage.Verifying,
            Verification = verified ? VerificationStatus.Passed : VerificationStatus.InProgress,
            ClientName = verified ? s.ClientName : null,
            CaseId = verified ? s.CaseId : null,
            VerificationAttempts = session.FailedVerificationAttempts,
        });
        await tracker.AddEventAsync(session, caseNumber is not null && Identifiers.NormaliseCaseNumber(caseNumber) == Identifiers.NormaliseCaseNumber(match.CaseNumber)
            ? $"Case {match.CaseNumber} found by case number; security check required"
            : $"Case {match.CaseNumber} found by postcode and surname; security check required");

        return Output(new
        {
            found = true,
            already_verified = verified,
            next_step = verified
                ? "The caller is already verified. Call get_case_details."
                : "Ask the security questions one at a time: the full name of anyone on the account, their previous mortgage lender, and the date of birth of anyone on the account. Then call verify_identity.",
        });
    }

    private async Task<string> VerifyIdentityAsync(CallSession session, JsonObject args)
    {
        var account = session.ClaimedCase;
        if (account is null)
            return Output(new { verified = false, error = "No case has been found yet. Call find_case first." });

        if (session.Summary.Stage == CallStage.QueuedForAgent)
            return Output(new { verified = false, transferring_to_agent = true, next_step = "The caller is already waiting for a colleague." });

        var answers = new SecurityAnswers(Str(args, "full_name"), Str(args, "previous_lender"), Str(args, "date_of_birth"));
        var result = IdentityVerifier.Evaluate(account, answers, session.FailedVerificationAttempts);

        switch (result.Decision)
        {
            case VerificationDecision.Passed:
                session.MarkVerified();
                await tracker.UpdateAsync(session, s => s with
                {
                    Stage = CallStage.Verified,
                    Verification = VerificationStatus.Passed,
                    ClientName = account.ClientNames,
                    CaseId = account.Id,
                });
                await tracker.AddEventAsync(session, $"Security passed for case {account.CaseNumber} ({account.ClientNames})");
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

    private async Task<string> GetCaseDetailsAsync(CallSession session)
    {
        var c = session.VerifiedCase;
        if (c is null)
            return Output(new { error = "The caller has not passed security. Do not share any details." });

        await tracker.AddEventAsync(session, $"Shared progress update: {c.CaseNumber} ({c.Stage})");

        return Output(new
        {
            client_names = c.ClientNames,
            case_number = c.CaseNumber,
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
            note = c.IsCompleted ? "This case has completed. Ask how else you can help, or offer a transfer." : null,
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

    private static string Describe(string? caseNumber, string? postcode, string? surname) =>
        caseNumber is not null ? $"case number '{caseNumber}'"
        : postcode is not null || surname is not null ? $"postcode '{postcode}' and surname '{surname}'"
        : "no details given";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static string Output(object value) => JsonSerializer.Serialize(value, Json);
}
