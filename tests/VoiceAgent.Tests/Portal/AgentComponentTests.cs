using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VoiceAgent.Portal.Components;
using VoiceAgent.Portal.Features.Agents;
using VoiceAgent.Portal.Features.LiveCalls;
using VoiceAgent.Portal.Services;
using VoiceAgent.Shared;

namespace VoiceAgent.Tests.Portal;

public class AgentComponentTests : PortalTestBase
{
    private async Task SignInAsAsync(AgentInfo agent)
    {
        Live.AgentList.Add(agent);
        await Ctx.Services.GetRequiredService<AgentProfile>().SetAsync(agent.Id);
    }

    [Fact]
    public void Status_menu_asks_new_users_to_set_up_an_agent()
    {
        var menu = Render<AgentStatusMenu>();
        Assert.NotNull(menu.Find("[data-testid=agent-setup]"));
    }

    [Fact]
    public async Task Status_menu_shows_the_signed_in_agent_and_their_status()
    {
        await SignInAsAsync(Agents.Make("a1", "Sarah", AgentStatus.WrapUp));

        var menu = Render<AgentStatusMenu>();

        Assert.Contains("Sarah · Wrap-up", menu.Find("[data-testid=agent-status]").TextContent);
    }

    [Fact]
    public async Task Offer_banner_appears_only_for_the_agent_it_is_offered_to_and_accept_takes_the_call()
    {
        await SignInAsAsync(Agents.Make("a1", "Sarah"));
        Live.Calls.Add(Calls.Make("call-30", CallStage.QueuedForAgent) with
        {
            OfferedToAgentId = "someone-else",
            QueueReason = "Wants their case handler",
        });

        var banner = Render<CallOfferBanner>();
        Assert.Empty(banner.FindAll("[data-testid=call-offer]"));

        Live.Calls[0] = Live.Calls[0] with { OfferedToAgentId = "a1", OfferExpiresAt = DateTimeOffset.UtcNow.AddSeconds(15), CaseReference = "1042/2617" };
        Live.RaiseChanged();

        banner.WaitForAssertion(() => Assert.Contains("Wants their case handler", banner.Find("[data-testid=call-offer]").TextContent));
        Assert.Contains("1042/2617", banner.Markup);

        banner.Find("[data-testid=accept-offer]").Click();

        banner.WaitForAssertion(() => Assert.Contains(("call-30", "a1"), Api.Taken));
    }

    [Fact]
    public async Task Offer_banner_decline_passes_the_call_on()
    {
        await SignInAsAsync(Agents.Make("a1", "Sarah"));
        Live.Calls.Add(Calls.Make("call-31", CallStage.QueuedForAgent) with { OfferedToAgentId = "a1", OfferExpiresAt = DateTimeOffset.UtcNow.AddSeconds(15) });

        var banner = Render<CallOfferBanner>();
        banner.FindAll("button").Single(b => b.TextContent.Contains("Decline")).Click();

        banner.WaitForAssertion(() => Assert.Contains(("call-31", "a1"), Api.Declined));
    }

    [Fact]
    public void Agents_page_lists_each_agent_with_status_and_transfer_target()
    {
        Live.AgentList.Add(Agents.Make("a1", "Sarah", AgentStatus.Available));
        Live.AgentList.Add(Agents.Make("a2", "James", AgentStatus.Busy) with { TransferTarget = "+447700900202", CurrentCallId = "call-9" });

        var page = Render<AgentsPage>();

        var table = page.Find("[data-testid=agents-table]").TextContent;
        Assert.Contains("Sarah", table);
        Assert.Contains("James", table);
        Assert.Contains("+447700900202", table);
        Assert.Contains("View call", table);
    }

    [Fact]
    public void Queue_shows_who_a_call_is_offered_to_or_that_no_agents_are_available()
    {
        Live.Calls.Add(Calls.Make("call-40", CallStage.QueuedForAgent) with { QueueReason = "a" });
        var page = Render<LiveCallsPage>();
        Assert.Contains("No agents available", page.Find("[data-testid=queue-table]").TextContent);

        Live.Calls[0] = Live.Calls[0] with { OfferedToAgentName = "Sarah", OfferExpiresAt = DateTimeOffset.UtcNow.AddSeconds(10) };
        Live.RaiseChanged();

        page.WaitForAssertion(() => Assert.Contains("Sarah", page.Find("[data-testid=queue-table]").TextContent));
    }
}
