using System.Text.Json.Nodes;
using VoiceAgent.Core.Agent;
using VoiceAgent.Shared;

namespace VoiceAgent.Tests.Api;

public class AgentToolHandlerTests
{
    private readonly TestHarness _h = new();

    private async Task<JsonObject> Call(VoiceAgent.Api.Features.Calls.CallSession session, string tool, object args) =>
        JsonNode.Parse(await _h.Tools.HandleAsync(session, tool, System.Text.Json.JsonSerializer.Serialize(args)))!.AsObject();

    [Fact]
    public async Task Records_call_reason_and_moves_to_identifying_case()
    {
        var session = await _h.StartCallAsync();

        await Call(session, ToolNames.RecordCallReason, new { reason = "Slow broadband", category = "Technical fault" });

        Assert.Equal("Slow broadband", session.Summary.CallReason);
        Assert.Equal("Technical fault", session.Summary.CallCategory);
        Assert.Equal(CallStage.IdentifyingCase, session.Summary.Stage);
    }

    [Fact]
    public async Task Finding_a_customer_never_reveals_personal_details()
    {
        var session = await _h.StartCallAsync();

        var result = await Call(session, ToolNames.FindCustomer, new { case_reference = "C S one zero four two" });

        Assert.True(result["found"]!.GetValue<bool>());
        var json = result.ToJsonString();
        Assert.DoesNotContain("Amelia", json);
        Assert.DoesNotContain("lighthouse", json);
        Assert.Equal("CS1042", session.Summary.CaseReference);
        Assert.Equal("****4567", session.Summary.ClaimedAccount);
        Assert.Equal(CallStage.Verifying, session.Summary.Stage);
        Assert.Null(session.Summary.CustomerName);
    }

    [Fact]
    public async Task Unknown_account_is_reported_as_not_found()
    {
        var session = await _h.StartCallAsync();
        var result = await Call(session, ToolNames.FindCustomer, new { account_number = "99999999" });
        Assert.False(result["found"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Case_details_are_blocked_until_security_is_passed()
    {
        var session = await _h.StartCallAsync();
        await Call(session, ToolNames.FindCustomer, new { account_number = "10234567" });

        var blocked = await Call(session, ToolNames.GetCaseDetails, new { });
        Assert.NotNull(blocked["error"]);
        Assert.DoesNotContain("Broadband", blocked.ToJsonString());

        var verified = await Call(session, ToolNames.VerifyIdentity, new { date_of_birth = "14/03/1985", postcode = "LS14AP", memorable_word = "lighthouse" });
        Assert.True(verified["verified"]!.GetValue<bool>());
        Assert.Equal("Amelia Hughes", session.Summary.CustomerName);
        Assert.Equal(CallStage.Verified, session.Summary.Stage);

        var details = await Call(session, ToolNames.GetCaseDetails, new { });
        Assert.Contains("CS1042", details.ToJsonString());
        Assert.Contains("Engineer visit booked", details.ToJsonString());
    }

    [Fact]
    public async Task Three_failed_security_attempts_queue_the_caller_for_an_agent()
    {
        var session = await _h.StartCallAsync();
        await Call(session, ToolNames.FindCustomer, new { account_number = "10234567" });
        var wrong = new { date_of_birth = "1 January 1990", postcode = "AB1 2CD", memorable_word = "nope" };

        var first = await Call(session, ToolNames.VerifyIdentity, wrong);
        Assert.Equal(2, first["attempts_remaining"]!.GetValue<int>());
        var second = await Call(session, ToolNames.VerifyIdentity, wrong);
        Assert.Equal(1, second["attempts_remaining"]!.GetValue<int>());
        Assert.Equal(CallStage.Verifying, session.Summary.Stage);

        var third = await Call(session, ToolNames.VerifyIdentity, wrong);

        Assert.True(third["transferring_to_agent"]!.GetValue<bool>());
        Assert.Equal(CallStage.QueuedForAgent, session.Summary.Stage);
        Assert.Equal(VerificationStatus.Failed, session.Summary.Verification);
        Assert.Equal(3, session.Summary.VerificationAttempts);
        Assert.True(session.HandOffPending);
        Assert.Contains(_h.Queue.Waiting(), c => c.Id == session.Id);
    }

    [Fact]
    public async Task Wrong_answers_never_say_which_answer_was_wrong()
    {
        var session = await _h.StartCallAsync();
        await Call(session, ToolNames.FindCustomer, new { account_number = "10234567" });

        var result = await Call(session, ToolNames.VerifyIdentity, new { date_of_birth = "14/03/1985", postcode = "LS1 4AP", memorable_word = "wrong" });

        var json = result.ToJsonString().ToLowerInvariant();
        Assert.DoesNotContain("memorable", json.Replace("ask all three security questions again", ""));
        Assert.DoesNotContain("lighthouse", json);
    }

    [Fact]
    public async Task Verifying_before_finding_an_account_is_rejected()
    {
        var session = await _h.StartCallAsync();
        var result = await Call(session, ToolNames.VerifyIdentity, new { date_of_birth = "x", postcode = "y", memorable_word = "z" });
        Assert.False(result["verified"]!.GetValue<bool>());
        Assert.Equal(0, session.FailedVerificationAttempts);
    }

    [Fact]
    public async Task Switching_account_after_verifying_requires_security_again()
    {
        var session = await _h.StartCallAsync();
        await Call(session, ToolNames.FindCustomer, new { account_number = "10234567" });
        await Call(session, ToolNames.VerifyIdentity, new { date_of_birth = "14/03/1985", postcode = "LS1 4AP", memorable_word = "lighthouse" });

        await Call(session, ToolNames.FindCustomer, new { account_number = "10987654" });

        Assert.Null(session.VerifiedCustomer);
        Assert.Null(session.Summary.CustomerName);
        Assert.Equal(CallStage.Verifying, session.Summary.Stage);
    }

    [Fact]
    public async Task Transfer_tool_queues_the_caller()
    {
        var session = await _h.StartCallAsync();
        await Call(session, ToolNames.TransferToAgent, new { reason = "Wants to make a complaint" });

        Assert.Equal(CallStage.QueuedForAgent, session.Summary.Stage);
        Assert.Equal("Wants to make a complaint", session.Summary.QueueReason);
        Assert.NotNull(session.Summary.QueuedAt);
    }

    [Fact]
    public async Task Bad_arguments_and_unknown_tools_return_errors_instead_of_throwing()
    {
        var session = await _h.StartCallAsync();
        Assert.Contains("error", await _h.Tools.HandleAsync(session, ToolNames.FindCustomer, "{not json"));
        Assert.Contains("error", await _h.Tools.HandleAsync(session, "delete_everything", "{}"));
    }
}
