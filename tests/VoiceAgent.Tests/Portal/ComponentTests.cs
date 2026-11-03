using Bunit;
using VoiceAgent.Portal.Components;
using VoiceAgent.Portal.Services;
using VoiceAgent.Shared;

namespace VoiceAgent.Tests.Portal;

public class ComponentTests : PortalTestBase
{
    [Theory]
    [InlineData(CallStage.QueuedForAgent, "Waiting for agent")]
    [InlineData(CallStage.Verifying, "Security check")]
    [InlineData(CallStage.Verified, "Verified")]
    public void Stage_chip_uses_friendly_labels(CallStage stage, string label)
    {
        var chip = Render<StageChip>(p => p.Add(x => x.Stage, stage));
        Assert.Contains(label, chip.Markup);
    }

    [Theory]
    [InlineData(LiveConnectionState.Connected, "Live")]
    [InlineData(LiveConnectionState.Reconnecting, "Reconnecting")]
    [InlineData(LiveConnectionState.Disconnected, "Offline")]
    public void Connection_indicator_shows_the_live_connection_state(LiveConnectionState state, string label)
    {
        Live.State = state;
        var indicator = Render<ConnectionIndicator>();
        Assert.Contains(label, indicator.Find("[data-testid=connection-state]").TextContent);
    }

    [Fact]
    public void Transcript_labels_each_speaker()
    {
        var view = Render<TranscriptView>(p => p.Add(x => x.Entries, new List<TranscriptEntry>
        {
            new(Speaker.Agent, "How can I help?", DateTimeOffset.UtcNow),
            new(Speaker.Caller, "When do we complete?", DateTimeOffset.UtcNow),
        }));

        Assert.Contains("Voice agent", view.Markup);
        Assert.Contains("Caller", view.Markup);
        Assert.Single(view.FindAll(".bubble.caller"));
    }

    [Fact]
    public void Take_button_is_disabled_once_an_agent_has_the_call()
    {
        var button = Render<TakeCallButton>(p => p.Add(x => x.Call, Calls.Make("call-20", CallStage.WithAgent)));
        Assert.True(button.Find("[data-testid=take-call]").HasAttribute("disabled"));
    }

    [Fact]
    public void Version_is_shown_without_the_commit_suffix() =>
        Assert.DoesNotContain("+", AppVersion.Current);
}
