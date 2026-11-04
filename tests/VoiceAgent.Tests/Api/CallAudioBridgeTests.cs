using VoiceAgent.Api.Features.VoiceLive;
using VoiceAgent.Core.Agent;
using VoiceAgent.Shared;

namespace VoiceAgent.Tests.Api;

public class CallAudioBridgeTests
{
    private readonly TestHarness _h = new();
    private readonly FakeCaller _caller = new();

    [Fact]
    public async Task Relays_audio_both_ways_and_records_the_transcript()
    {
        var session = await _h.StartCallAsync();
        var run = _h.Bridge().RunAsync(session, _caller, CancellationToken.None);

        await Eventually.TrueAsync(() => _h.VoiceLive.SentTypes.Contains("response.create"), "the agent is asked to greet");
        Assert.Equal("session.update", _h.VoiceLive.SentTypes.First());

        _caller.Say("AQI=");
        _h.VoiceLive.Push(new VoiceLiveEvent.AudioDelta("AQID"));
        _h.VoiceLive.Push(new VoiceLiveEvent.AgentTranscript("Thanks for calling."));
        _h.VoiceLive.Push(new VoiceLiveEvent.CallerStartedSpeaking());
        _h.VoiceLive.Push(new VoiceLiveEvent.CallerTranscript("When will I exchange?"));

        await Eventually.TrueAsync(() => session.Snapshot().Transcript.Count == 2, "both transcript lines arrive");
        await Eventually.TrueAsync(() => _h.VoiceLive.SentTypes.Contains("input_audio_buffer.append"), "caller audio reaches Voice Live");

        lock (_caller.Received)
        {
            Assert.Contains(_caller.Received, m => m.Contains("\"Data\":\"AQID\""));
            Assert.Contains(_caller.Received, m => m.Contains("StopAudio"));
        }
        Assert.Equal(CallStage.IdentifyingCase, session.Summary.Stage);

        _caller.HangUp();
        await run;
        Assert.True(_h.VoiceLive.Disposed);
    }

    [Fact]
    public async Task Runs_tools_and_asks_the_agent_to_respond_after_the_tool_turn()
    {
        var session = await _h.StartCallAsync();
        var run = _h.Bridge(new VoiceLiveOptions { GreetOnConnect = false }).RunAsync(session, _caller, CancellationToken.None);
        await Eventually.TrueAsync(() => _h.VoiceLive.SentTypes.Contains("session.update"), "session configured");

        _h.VoiceLive.Push(new VoiceLiveEvent.FunctionCall("call-1", ToolNames.FindCase, """{"case_number":"1042/2617"}"""));
        _h.VoiceLive.Push(new VoiceLiveEvent.ResponseDone());

        await Eventually.TrueAsync(() => _h.VoiceLive.SentTypes.Contains("response.create"), "agent asked to continue");
        var output = _h.VoiceLive.Sent.Single(m => m["type"]!.GetValue<string>() == "conversation.item.create");
        Assert.Equal("call-1", output["item"]!["call_id"]!.GetValue<string>());
        Assert.Equal(CallStage.Verifying, session.Summary.Stage);

        _caller.HangUp();
        await run;
    }

    [Fact]
    public async Task Hands_off_after_the_agent_finishes_its_transfer_message_but_keeps_the_caller_connected()
    {
        var session = await _h.StartCallAsync();
        var run = _h.Bridge(new VoiceLiveOptions { GreetOnConnect = false }).RunAsync(session, _caller, CancellationToken.None);
        await Eventually.TrueAsync(() => _h.VoiceLive.SentTypes.Contains("session.update"), "session configured");

        _h.VoiceLive.Push(new VoiceLiveEvent.FunctionCall("call-1", ToolNames.TransferToAgent, """{"reason":"Wants a person"}"""));
        _h.VoiceLive.Push(new VoiceLiveEvent.ResponseDone()); // tool turn -> response.create
        await Eventually.TrueAsync(() => _h.VoiceLive.SentTypes.Contains("response.create"), "agent asked to speak");
        Assert.False(session.AiStopped.IsCancellationRequested);

        _h.VoiceLive.Push(new VoiceLiveEvent.ResponseDone()); // "I'm transferring you now" finished
        await Eventually.TrueAsync(() => session.AiStopped.IsCancellationRequested, "AI stops after the hand-off message");

        Assert.Equal(CallStage.QueuedForAgent, session.Summary.Stage);
        Assert.Equal(1, _h.CallControl.HoldMusicStarts);
        Assert.False(run.IsCompleted, "the caller's line must stay open while they wait");

        var appendsBefore = _h.VoiceLive.SentTypes.Count(t => t == "input_audio_buffer.append");
        _caller.Say("AQI=");
        await Task.Delay(50);
        Assert.Equal(appendsBefore, _h.VoiceLive.SentTypes.Count(t => t == "input_audio_buffer.append"));

        _caller.HangUp();
        await run;
    }

    [Fact]
    public async Task Queues_the_caller_if_voice_live_is_unavailable()
    {
        _h.VoiceLive.FailToConnect = true;
        var session = await _h.StartCallAsync();

        var run = _h.Bridge().RunAsync(session, _caller, CancellationToken.None);
        await Eventually.TrueAsync(() => session.Summary.Stage == CallStage.QueuedForAgent, "caller queued");
        Assert.Equal("Voice agent unavailable", session.Summary.QueueReason);

        _caller.HangUp();
        await run;
    }
}
