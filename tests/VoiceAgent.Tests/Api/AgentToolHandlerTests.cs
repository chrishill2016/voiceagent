using System.Text.Json.Nodes;
using VoiceAgent.Api.Features.Calls;
using VoiceAgent.Core.Agent;
using VoiceAgent.Shared;

namespace VoiceAgent.Tests.Api;

public class AgentToolHandlerTests
{
    private readonly TestHarness _h = new();

    // Case 1042/2617: Amelia Hughes (14/03/1985) and Tom Hughes, previous lender Nationwide Building Society.
    private static readonly object HughesAnswers = new { full_name = "Amelia Hughes", previous_lender = "Nationwide", date_of_birth = "14/03/1985" };
    private static readonly object WrongAnswers = new { full_name = "Amelia Hughes", previous_lender = "Halifax", date_of_birth = "1 January 1990" };

    private async Task<JsonObject> Call(CallSession session, string tool, object args) =>
        JsonNode.Parse(await _h.Tools.HandleAsync(session, tool, System.Text.Json.JsonSerializer.Serialize(args)))!.AsObject();

    [Fact]
    public async Task Records_call_reason_and_moves_to_finding_the_case()
    {
        var session = await _h.StartCallAsync();

        await Call(session, ToolNames.RecordCallReason, new { reason = "Wants a progress update on their purchase", category = "Progress update" });

        Assert.Equal("Wants a progress update on their purchase", session.Summary.CallReason);
        Assert.Equal("Progress update", session.Summary.CallCategory);
        Assert.Equal(CallStage.IdentifyingCase, session.Summary.Stage);
    }

    [Theory]
    [InlineData("1042/2617")]
    [InlineData("1042 2617")]
    [InlineData("one oh four two stroke two six one seven")]
    public async Task Finds_the_case_by_case_number_without_revealing_personal_details(string caseNumber)
    {
        var session = await _h.StartCallAsync();

        var result = await Call(session, ToolNames.FindCase, new { case_number = caseNumber });

        Assert.True(result["found"]!.GetValue<bool>());
        var json = result.ToJsonString();
        Assert.DoesNotContain("Hughes", json);
        Assert.DoesNotContain("Nationwide", json);
        Assert.Equal("1042/2617", session.Summary.CaseReference);
        Assert.Equal(CallStage.Verifying, session.Summary.Stage);
        Assert.Null(session.Summary.ClientName);
    }

    [Fact]
    public async Task Finds_the_case_by_postcode_and_surname()
    {
        var session = await _h.StartCallAsync();

        var result = await Call(session, ToolNames.FindCase, new { postcode = "LS6 2QT", surname = "hughes" });

        Assert.True(result["found"]!.GetValue<bool>());
        Assert.Equal("1042/2617", session.Summary.CaseReference);
    }

    [Fact]
    public async Task Postcode_and_surname_prefers_the_open_case_when_others_have_completed()
    {
        // Oliver Patel has an open sale (1077/3150) and a completed remortgage (0861/1904) at the same addresses.
        var session = await _h.StartCallAsync();

        var result = await Call(session, ToolNames.FindCase, new { postcode = "M4 2BS", surname = "Patel" });

        Assert.True(result["found"]!.GetValue<bool>());
        Assert.Equal("1077/3150", session.Summary.CaseReference);
    }

    [Fact]
    public async Task Falls_back_to_postcode_and_surname_when_the_case_number_is_wrong()
    {
        var session = await _h.StartCallAsync();
        var result = await Call(session, ToolNames.FindCase, new { case_number = "9999/9999", postcode = "BT7 1JL", surname = "O'Connor" });
        Assert.True(result["found"]!.GetValue<bool>());
        Assert.Equal("1118/5096", session.Summary.CaseReference);
    }

    [Fact]
    public async Task Unknown_case_is_reported_as_not_found()
    {
        var session = await _h.StartCallAsync();
        var result = await Call(session, ToolNames.FindCase, new { case_number = "9999/9999" });
        Assert.False(result["found"]!.GetValue<bool>());
        Assert.Contains("postcode and surname", result["next_step"]!.GetValue<string>());
    }

    [Fact]
    public async Task Case_details_are_blocked_until_security_is_passed()
    {
        var session = await _h.StartCallAsync();
        await Call(session, ToolNames.FindCase, new { case_number = "1042/2617" });

        var blocked = await Call(session, ToolNames.GetCaseDetails, new { });
        Assert.NotNull(blocked["error"]);
        Assert.DoesNotContain("Elm Road", blocked.ToJsonString());

        var verified = await Call(session, ToolNames.VerifyIdentity, HughesAnswers);
        Assert.True(verified["verified"]!.GetValue<bool>());
        Assert.Equal("Amelia Hughes and Tom Hughes", session.Summary.ClientName);
        Assert.Equal(CallStage.Verified, session.Summary.Stage);

        var details = await Call(session, ToolNames.GetCaseDetails, new { });
        Assert.Equal("1042/2617", details["case_number"]!.GetValue<string>());
        Assert.Equal("Searches", details["stage"]!.GetValue<string>());
        Assert.Equal("Local authority", details["waiting_on"]!.GetValue<string>());
        Assert.Equal("Friday 4 December 2026", details["target_exchange_date"]!.GetValue<string>());
        Assert.Contains(details["milestones"]!.AsArray(), m => m!["name"]!.GetValue<string>() == "Searches" && m["status"]!.GetValue<string>() == "in progress");
    }

    [Fact]
    public async Task Joint_clients_can_answer_with_either_persons_details()
    {
        var session = await _h.StartCallAsync();
        await Call(session, ToolNames.FindCase, new { case_number = "1042/2617" });

        var result = await Call(session, ToolNames.VerifyIdentity, new { full_name = "Tom Hughes", previous_lender = "Nationwide Building Society", date_of_birth = "2nd August 1983" });

        Assert.True(result["verified"]!.GetValue<bool>());
    }

    [Fact]
    public async Task First_time_buyers_answer_none_for_previous_lender()
    {
        var session = await _h.StartCallAsync();
        await Call(session, ToolNames.FindCase, new { case_number = "1140/6273" });

        var result = await Call(session, ToolNames.VerifyIdentity, new { full_name = "Priya Shah", previous_lender = "none", date_of_birth = "17 May 1995" });

        Assert.True(result["verified"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Three_failed_security_attempts_queue_the_caller_for_an_agent()
    {
        var session = await _h.StartCallAsync();
        await Call(session, ToolNames.FindCase, new { case_number = "1042/2617" });

        var first = await Call(session, ToolNames.VerifyIdentity, WrongAnswers);
        Assert.Equal(2, first["attempts_remaining"]!.GetValue<int>());
        var second = await Call(session, ToolNames.VerifyIdentity, WrongAnswers);
        Assert.Equal(1, second["attempts_remaining"]!.GetValue<int>());
        Assert.Equal(CallStage.Verifying, session.Summary.Stage);

        var third = await Call(session, ToolNames.VerifyIdentity, WrongAnswers);

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
        await Call(session, ToolNames.FindCase, new { case_number = "1042/2617" });

        var result = await Call(session, ToolNames.VerifyIdentity, WrongAnswers);

        var json = result.ToJsonString().ToLowerInvariant();
        Assert.DoesNotContain("lender was", json);
        Assert.DoesNotContain("nationwide", json);
        Assert.DoesNotContain("hughes", json);
    }

    [Fact]
    public async Task Verifying_before_finding_a_case_is_rejected()
    {
        var session = await _h.StartCallAsync();
        var result = await Call(session, ToolNames.VerifyIdentity, HughesAnswers);
        Assert.False(result["verified"]!.GetValue<bool>());
        Assert.Equal(0, session.FailedVerificationAttempts);
    }

    [Fact]
    public async Task Switching_case_after_verifying_requires_security_again()
    {
        var session = await _h.StartCallAsync();
        await Call(session, ToolNames.FindCase, new { case_number = "1042/2617" });
        await Call(session, ToolNames.VerifyIdentity, HughesAnswers);

        await Call(session, ToolNames.FindCase, new { case_number = "1077/3150" });

        Assert.Null(session.VerifiedCase);
        Assert.Null(session.Summary.ClientName);
        Assert.Equal(CallStage.Verifying, session.Summary.Stage);
    }

    [Fact]
    public async Task Transfer_tool_queues_the_caller()
    {
        var session = await _h.StartCallAsync();
        await Call(session, ToolNames.TransferToAgent, new { reason = "Wants to speak to their case handler" });

        Assert.Equal(CallStage.QueuedForAgent, session.Summary.Stage);
        Assert.Equal("Wants to speak to their case handler", session.Summary.QueueReason);
        Assert.NotNull(session.Summary.QueuedAt);
    }

    [Fact]
    public async Task Bad_arguments_and_unknown_tools_return_errors_instead_of_throwing()
    {
        var session = await _h.StartCallAsync();
        Assert.Contains("error", await _h.Tools.HandleAsync(session, ToolNames.FindCase, "{not json"));
        Assert.Contains("error", await _h.Tools.HandleAsync(session, "delete_everything", "{}"));
    }
}
