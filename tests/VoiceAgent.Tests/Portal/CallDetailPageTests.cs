using Bunit;
using VoiceAgent.Portal.Features.LiveCalls;
using VoiceAgent.Shared;

namespace VoiceAgent.Tests.Portal;

public class CallDetailPageTests : PortalTestBase
{
    [Fact]
    public void Shows_the_saved_transcript_and_streams_new_lines()
    {
        var summary = Calls.Make("call-10", CallStage.Verifying) with { CallReason = "Router not delivered" };
        Api.Details["call-10"] = new CallDetail(summary,
            [new TranscriptEntry(Speaker.Agent, "Thanks for calling Contoso.", DateTimeOffset.UtcNow.AddSeconds(-10))],
            [new CallEvent("Voice agent connected", DateTimeOffset.UtcNow.AddSeconds(-11))]);

        var page = Render<CallDetailPage>(p => p.Add(x => x.Id, "call-10"));
        page.WaitForAssertion(() => Assert.Contains("Thanks for calling Contoso.", page.Find("[data-testid=transcript]").TextContent));
        Assert.Contains("Router not delivered", page.Markup);
        Assert.Contains("Voice agent connected", page.Markup);

        Live.RaiseTranscript(new TranscriptUpdate("call-10", new TranscriptEntry(Speaker.Caller, "My router never came.", DateTimeOffset.UtcNow)));
        Live.RaiseTranscript(new TranscriptUpdate("another-call", new TranscriptEntry(Speaker.Caller, "Not this call", DateTimeOffset.UtcNow)));

        page.WaitForAssertion(() => Assert.Contains("My router never came.", page.Find("[data-testid=transcript]").TextContent));
        Assert.DoesNotContain("Not this call", page.Markup);
    }

    [Fact]
    public void Reflects_live_verification_changes()
    {
        Api.Details["call-11"] = new CallDetail(Calls.Make("call-11", CallStage.Verifying), [], []);
        var page = Render<CallDetailPage>(p => p.Add(x => x.Id, "call-11"));
        page.WaitForAssertion(() => Assert.Contains("Not started", page.Find("[data-testid=verification-status]").TextContent));

        Live.RaiseCallUpdated(Calls.Make("call-11", CallStage.Verified) with
        {
            Verification = VerificationStatus.Passed,
            CustomerName = "Amelia Hughes",
        });

        page.WaitForAssertion(() => Assert.Contains("Passed", page.Find("[data-testid=verification-status]").TextContent));
        Assert.Contains("Amelia Hughes", page.Markup);
    }

    [Fact]
    public void Says_when_a_call_cannot_be_found()
    {
        var page = Render<CallDetailPage>(p => p.Add(x => x.Id, "nope"));
        page.WaitForAssertion(() => Assert.Contains("couldn't be found", page.Markup));
    }
}
