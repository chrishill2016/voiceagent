using System.Collections.Concurrent;
using VoiceAgent.Api.Features.Calls;
using VoiceAgent.Api.Features.Telephony;
using VoiceAgent.Api.Features.Tools;
using VoiceAgent.Api.Features.VoiceLive;
using Microsoft.Extensions.Options;
using VoiceAgent.Shared;

namespace VoiceAgent.Api.Features.Media;

/// <summary>
/// Connects one caller to Voice Live: caller audio in, agent audio out, transcripts to the portal,
/// and tool calls to <see cref="AgentToolHandler"/>. When the caller is handed to a person the AI
/// stops, but the caller's line stays open until the call is transferred or they hang up.
/// </summary>
public sealed class CallAudioBridge
{
    private static readonly TimeSpan PrewarmTimeout = TimeSpan.FromSeconds(20);

    /// <summary>How long to wait for Voice Live to confirm the session before asking for the greeting anyway.</summary>
    private static readonly TimeSpan SessionReadyTimeout = TimeSpan.FromSeconds(5);

    private readonly IVoiceLiveConnectionFactory _voiceLive;
    private readonly AgentToolHandler _tools;
    private readonly CallTracker _tracker;
    private readonly AgentQueue _queue;
    private readonly ICallControl _callControl;
    private readonly VoiceLiveOptions _options;
    private readonly ILogger<CallAudioBridge> _logger;
    private readonly ConcurrentDictionary<string, Task<IVoiceLiveConnection>> _prewarmed = new();

    public CallAudioBridge(
        IVoiceLiveConnectionFactory voiceLive,
        AgentToolHandler tools,
        CallTracker tracker,
        AgentQueue queue,
        ICallControl callControl,
        IOptions<VoiceLiveOptions> options,
        ILogger<CallAudioBridge> logger)
    {
        _voiceLive = voiceLive;
        _tools = tools;
        _tracker = tracker;
        _queue = queue;
        _callControl = callControl;
        _options = options.Value;
        _logger = logger;
        _tracker.CallEnded += DiscardPrewarmedAsync;
    }

    /// <summary>
    /// Starts connecting to Voice Live and generating the greeting as soon as a phone call arrives,
    /// in parallel with answering it. Voice Live's replies wait in the WebSocket until the call audio
    /// connects, so the greeting plays almost immediately instead of after a full connect-and-generate.
    /// </summary>
    public void Prewarm(CallSession session) =>
        _prewarmed.TryAdd(session.Id, ConnectAndGreetAsync(session.Id));

    private async Task<IVoiceLiveConnection> ConnectAndGreetAsync(string callId)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(PrewarmTimeout);
        var connection = await _voiceLive.ConnectAsync(timeout.Token);
        await ConfigureAndGreetAsync(connection, callId, timeout.Token);
        _logger.LogInformation("Call {CallId}: Voice Live ready (prewarmed) in {Ms} ms", callId, clock.ElapsedMilliseconds);
        return connection;
    }

    /// <summary>
    /// Sends the session settings, waits for Voice Live to confirm them (session.updated), then asks for the greeting.
    /// Asking for a response before the session is confirmed can be ignored, leaving the caller in silence until they speak.
    /// </summary>
    private async Task ConfigureAndGreetAsync(IVoiceLiveConnection connection, string callId, CancellationToken ct)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await connection.SendAsync(VoiceLiveProtocol.SessionUpdate(_options), ct);

        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(SessionReadyTimeout);
        var ready = false;
        try
        {
            await foreach (var evt in connection.ReadEventsAsync(wait.Token))
            {
                if (evt is VoiceLiveEvent.SessionReady)
                {
                    ready = true;
                    break;
                }

                if (evt is VoiceLiveEvent.Error e)
                    _logger.LogWarning("Call {CallId}: Voice Live error while setting up the session: {Message}", callId, e.Message);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timed out waiting; carry on and ask for the greeting anyway.
        }

        _logger.LogInformation(ready
            ? "Call {CallId}: Voice Live session confirmed in {Ms} ms"
            : "Call {CallId}: no session confirmation from Voice Live after {Ms} ms; requesting the greeting anyway",
            callId, clock.ElapsedMilliseconds);

        if (_options.GreetOnConnect)
        {
            await connection.SendAsync(VoiceLiveProtocol.CallConnectedNote(), ct);
            await connection.SendAsync(VoiceLiveProtocol.CreateResponse(), ct);
        }
    }

    private async Task DiscardPrewarmedAsync(CallSummary call)
    {
        if (!_prewarmed.TryRemove(call.Id, out var pending)) return;
        try
        {
            await (await pending).DisposeAsync();
        }
        catch
        {
            // The prewarm failed; nothing to clean up.
        }
    }

    public async Task RunAsync(CallSession session, ICallerAudioChannel caller, CancellationToken callEnded)
    {
        using var aiCts = CancellationTokenSource.CreateLinkedTokenSource(callEnded, session.AiStopped);
        IVoiceLiveConnection? connection = null;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (_prewarmed.TryRemove(session.Id, out var pending))
            {
                connection = await pending.WaitAsync(aiCts.Token);
                _logger.LogInformation("Call {CallId}: using prewarmed Voice Live connection (waited {Ms} ms)", session.Id, clock.ElapsedMilliseconds);
            }
            else
            {
                connection = await _voiceLive.ConnectAsync(aiCts.Token);
                _logger.LogInformation("Call {CallId}: connected to Voice Live in {Ms} ms", session.Id, clock.ElapsedMilliseconds);
                await ConfigureAndGreetAsync(connection, session.Id, aiCts.Token);
            }

            await _tracker.UpdateAsync(session, s => s with
            {
                Stage = s.Stage == CallStage.Connecting ? CallStage.IdentifyingCase : s.Stage,
            });
            await _tracker.AddEventAsync(session, "Voice agent connected");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not connect call {CallId} to Voice Live", session.Id);
            await _queue.EnqueueAsync(session, "Voice agent unavailable");
            await _callControl.StartHoldMusicAsync(session, CancellationToken.None);
            session.StopAi();
        }

        var agentLoop = connection is null
            ? Task.CompletedTask
            : RunAgentLoopAsync(session, connection, caller, aiCts, clock);

        // Keep the caller's line open for the whole call, forwarding audio only while the AI is active.
        await foreach (var raw in caller.ReceiveAsync(callEnded))
        {
            if (aiCts.IsCancellationRequested || connection is null) continue;
            if (AcsMediaMessages.Parse(raw) is not InboundMedia.Audio audio) continue;

            try
            {
                await connection.SendAsync(VoiceLiveProtocol.AppendAudio(audio.Base64Pcm), aiCts.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or System.Net.WebSockets.WebSocketException)
            {
                // AI side closed; keep the caller connected.
            }
        }

        await aiCts.CancelAsync();
        await agentLoop;
        if (connection is not null) await connection.DisposeAsync();
    }

    private async Task RunAgentLoopAsync(CallSession session, IVoiceLiveConnection connection, ICallerAudioChannel caller, CancellationTokenSource aiCts, System.Diagnostics.Stopwatch clock)
    {
        var ct = aiCts.Token;
        var toolCallsThisResponse = 0;
        var greeted = false;
        long? callerStoppedAt = null;

        try
        {
            await foreach (var evt in connection.ReadEventsAsync(ct))
            {
                switch (evt)
                {
                    case VoiceLiveEvent.AudioDelta { Base64Audio.Length: > 0 } audio:
                        if (!greeted)
                        {
                            greeted = true;
                            _logger.LogInformation("Call {CallId}: first agent audio {Ms} ms after the call audio connected", session.Id, clock.ElapsedMilliseconds);
                        }
                        else if (callerStoppedAt is { } stopped)
                        {
                            _logger.LogInformation("Call {CallId}: agent replied {Ms} ms after the caller stopped speaking", session.Id, clock.ElapsedMilliseconds - stopped);
                        }
                        callerStoppedAt = null;
                        await caller.SendAsync(AcsMediaMessages.OutboundAudio(audio.Base64Audio), ct);
                        break;

                    case VoiceLiveEvent.CallerStoppedSpeaking:
                        callerStoppedAt = clock.ElapsedMilliseconds;
                        break;

                    case VoiceLiveEvent.ResponseCreated when !greeted:
                        _logger.LogInformation("Call {CallId}: greeting response started {Ms} ms after the call audio connected", session.Id, clock.ElapsedMilliseconds);
                        break;

                    case VoiceLiveEvent.CallerStartedSpeaking:
                        // Barge-in: stop playing the agent's reply as soon as the caller talks.
                        await caller.SendAsync(AcsMediaMessages.StopAudio(), ct);
                        break;

                    case VoiceLiveEvent.CallerTranscript t:
                        await _tracker.AddTranscriptAsync(session, Speaker.Caller, t.Text);
                        break;

                    case VoiceLiveEvent.AgentTranscript t:
                        await _tracker.AddTranscriptAsync(session, Speaker.Agent, t.Text);
                        break;

                    case VoiceLiveEvent.FunctionCall call:
                        toolCallsThisResponse++;
                        var toolClock = System.Diagnostics.Stopwatch.StartNew();
                        var output = await _tools.HandleAsync(session, call.Name, call.Arguments, ct);
                        _logger.LogInformation("Call {CallId}: tool {Tool} took {Ms} ms", session.Id, call.Name, toolClock.ElapsedMilliseconds);
                        await connection.SendAsync(VoiceLiveProtocol.FunctionCallOutput(call.CallId, output), ct);
                        break;

                    case VoiceLiveEvent.ResponseDone { Failed: true } failed:
                        // Surface the reason: otherwise a failed agent run just looks like silence.
                        toolCallsThisResponse = 0;
                        _logger.LogWarning("Call {CallId}: Voice Live response {Status}: {Reason}", session.Id, failed.Status, failed.Reason ?? "no reason given");
                        await _tracker.AddEventAsync(session, $"Voice agent response {failed.Status}: {failed.Reason ?? "no reason given"}");
                        break;

                    case VoiceLiveEvent.ResponseDone:
                        if (toolCallsThisResponse > 0)
                        {
                            // Let the agent speak about the tool results.
                            toolCallsThisResponse = 0;
                            await connection.SendAsync(VoiceLiveProtocol.CreateResponse(), ct);
                        }
                        else if (session.HandOffPending)
                        {
                            // The agent has finished telling the caller they're being transferred.
                            await _tracker.AddEventAsync(session, "Voice agent handed over; caller waiting for a person");
                            await _callControl.StartHoldMusicAsync(session, CancellationToken.None);
                            session.StopAi();
                        }
                        break;

                    case VoiceLiveEvent.Error e:
                        _logger.LogWarning("Call {CallId}: Voice Live error: {Message}", session.Id, e.Message);
                        await _tracker.AddEventAsync(session, $"Voice agent error: {e.Message}");
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // AI stopped (hand-off, agent took over, or caller hung up).
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice agent loop failed for call {CallId}", session.Id);
            await _tracker.AddEventAsync(session, "Voice agent disconnected unexpectedly");
        }

        if (!aiCts.IsCancellationRequested && session.Summary.Stage is not (CallStage.QueuedForAgent or CallStage.WithAgent or CallStage.Ended))
        {
            // Voice Live dropped mid-call: never leave the caller stranded.
            await _queue.EnqueueAsync(session, "Voice agent disconnected");
            await _callControl.StartHoldMusicAsync(session, CancellationToken.None);
        }
    }
}
