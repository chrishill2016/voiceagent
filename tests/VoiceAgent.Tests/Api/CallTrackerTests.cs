using VoiceAgent.Shared;

namespace VoiceAgent.Tests.Api;

public class CallTrackerTests
{
    private readonly TestHarness _h = new();

    [Fact]
    public async Task Calls_are_persisted_broadcast_and_removed_when_ended()
    {
        var session = await _h.StartCallAsync();
        await _h.Tracker.AddTranscriptAsync(session, Speaker.Caller, "Hello?");

        Assert.Single(_h.Tracker.ActiveCalls());
        Assert.Single(_h.Notifier.Transcripts);
        Assert.Single((await _h.Store.GetAsync(session.Id))!.Transcript);

        await _h.Tracker.EndAsync(session.Id, "Caller hung up");
        await _h.Tracker.EndAsync(session.Id, "Called twice is fine");

        Assert.Empty(_h.Tracker.ActiveCalls());
        var stored = await _h.Store.GetAsync(session.Id);
        Assert.Equal(CallStage.Ended, stored!.Summary.Stage);
        Assert.NotNull(stored.Summary.EndedAt);
        Assert.Equal(CallStage.Ended, _h.Notifier.Summaries[^1].Stage);
        Assert.True(session.AiStopped.IsCancellationRequested);
    }

    [Fact]
    public async Task Blank_transcript_lines_are_ignored()
    {
        var session = await _h.StartCallAsync();
        await _h.Tracker.AddTranscriptAsync(session, Speaker.Agent, "   ");
        Assert.Empty(session.Snapshot().Transcript);
    }
}
