using VoiceAgent.Shared;

namespace VoiceAgent.Tests.Api;

public class CallTrackerAndQueueTests
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

    [Fact]
    public async Task Taking_a_test_call_marks_it_with_the_agent_and_stops_the_ai()
    {
        var session = await _h.StartCallAsync(isTestCall: true);
        await _h.Queue.EnqueueAsync(session, "Asked for a person");

        var result = await _h.Queue.TakeAsync(session.Id, "Chris");

        Assert.True(result.Success);
        Assert.Equal(CallStage.WithAgent, session.Summary.Stage);
        Assert.Equal("Chris", session.Summary.AssignedAgent);
        Assert.True(session.AiStopped.IsCancellationRequested);
        Assert.Empty(_h.Queue.Waiting());

        var again = await _h.Queue.TakeAsync(session.Id, "Sam");
        Assert.False(again.Success);
    }

    [Fact]
    public async Task Taking_a_phone_call_transfers_it()
    {
        _h.CallControl.TransferConfigured = true;
        var session = await _h.StartCallAsync(isTestCall: false);
        session.CallConnectionId = "conn-1";

        var result = await _h.Queue.TakeAsync(session.Id, "Chris");

        Assert.True(result.Success);
        Assert.Equal(1, _h.CallControl.Transfers);
    }

    [Fact]
    public async Task Taking_a_phone_call_without_a_transfer_target_explains_what_to_configure()
    {
        var session = await _h.StartCallAsync(isTestCall: false);
        session.CallConnectionId = "conn-1";

        var result = await _h.Queue.TakeAsync(session.Id, "Chris");

        Assert.False(result.Success);
        Assert.Contains("AgentTransferTarget", result.Message);
        Assert.False(session.AiStopped.IsCancellationRequested);
    }

    [Fact]
    public async Task Taking_an_ended_call_fails_cleanly()
    {
        var result = await _h.Queue.TakeAsync("missing", "Chris");
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Queue_is_ordered_by_time_queued()
    {
        var first = await _h.StartCallAsync();
        var second = await _h.StartCallAsync();
        await _h.Queue.EnqueueAsync(second, "b");
        await Task.Delay(5);
        await _h.Queue.EnqueueAsync(first, "a");

        Assert.Equal([second.Id, first.Id], _h.Queue.Waiting().Select(c => c.Id));
    }
}
