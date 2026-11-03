using Bunit;
using VoiceAgent.Portal.Features.LiveCalls;
using VoiceAgent.Shared;

namespace VoiceAgent.Tests.Portal;

public class LiveCallsPageTests : PortalTestBase
{
    [Fact]
    public void Shows_empty_states_when_there_are_no_calls()
    {
        var page = Render<LiveCallsPage>();

        Assert.Contains("No callers waiting", page.Markup);
        Assert.Contains("No calls in progress", page.Markup);
    }

    [Fact]
    public void Splits_queued_callers_from_calls_with_the_voice_agent()
    {
        Live.Calls.Add(Calls.Make("call-01", CallStage.Verifying) with { CallReason = "Progress update on sale", ClaimedClient = "****4567" });
        Live.Calls.Add(Calls.Make("call-02", CallStage.QueuedForAgent) with { QueueReason = "Could not pass security questions", Verification = VerificationStatus.Failed });

        var page = Render<LiveCallsPage>();

        var queue = page.Find("[data-testid=queue-table]");
        Assert.Contains("Could not pass security questions", queue.TextContent);
        Assert.Contains("Take call", queue.TextContent);

        var card = Assert.Single(page.FindAll("[data-testid=call-card]"));
        Assert.Contains("Progress update on sale", card.TextContent);
        Assert.Contains("****4567", card.TextContent);
        Assert.Contains("Security check", card.TextContent);
    }

    [Fact]
    public void Updates_when_a_new_call_arrives()
    {
        var page = Render<LiveCallsPage>();
        Assert.Empty(page.FindAll("[data-testid=call-card]"));

        Live.Calls.Add(Calls.Make("call-03", CallStage.IdentifyingCase));
        Live.RaiseChanged();

        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-testid=call-card]")));
    }
}
