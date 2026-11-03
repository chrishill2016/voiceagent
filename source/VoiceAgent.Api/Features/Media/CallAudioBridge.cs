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
public sealed class CallAudioBridge(
    IVoiceLiveConnectionFactory voiceLive,
    AgentToolHandler tools,
    CallTracker tracker,
    AgentQueue queue,
    ICallControl callControl,
    IOptions<VoiceLiveOptions> options,
    ILogger<CallAudioBridge> logger)
{
    public async Task RunAsync(CallSession session, ICallerAudioChannel caller, CancellationToken callEnded)
    {
        using var aiCts = CancellationTokenSource.CreateLinkedTokenSource(callEnded, session.AiStopped);
        IVoiceLiveConnection? connection = null;

        try
        {
            connection = await voiceLive.ConnectAsync(aiCts.Token);
            await connection.SendAsync(VoiceLiveProtocol.SessionUpdate(options.Value), aiCts.Token);
            if (options.Value.GreetOnConnect)
                await connection.SendAsync(VoiceLiveProtocol.CreateResponse(), aiCts.Token);

            await tracker.UpdateAsync(session, s => s with
            {
                Stage = s.Stage == CallStage.Connecting ? CallStage.IdentifyingCase : s.Stage,
            });
            await tracker.AddEventAsync(session, "Voice agent connected");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not connect call {CallId} to Voice Live", session.Id);
            await queue.EnqueueAsync(session, "Voice agent unavailable");
            await callControl.StartHoldMusicAsync(session, CancellationToken.None);
            session.StopAi();
        }

        var agentLoop = connection is null
            ? Task.CompletedTask
            : RunAgentLoopAsync(session, connection, caller, aiCts);

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

    private async Task RunAgentLoopAsync(CallSession session, IVoiceLiveConnection connection, ICallerAudioChannel caller, CancellationTokenSource aiCts)
    {
        var ct = aiCts.Token;
        var toolCallsThisResponse = 0;

        try
        {
            await foreach (var evt in connection.ReadEventsAsync(ct))
            {
                switch (evt)
                {
                    case VoiceLiveEvent.AudioDelta { Base64Audio.Length: > 0 } audio:
                        await caller.SendAsync(AcsMediaMessages.OutboundAudio(audio.Base64Audio), ct);
                        break;

                    case VoiceLiveEvent.CallerStartedSpeaking:
                        // Barge-in: stop playing the agent's reply as soon as the caller talks.
                        await caller.SendAsync(AcsMediaMessages.StopAudio(), ct);
                        break;

                    case VoiceLiveEvent.CallerTranscript t:
                        await tracker.AddTranscriptAsync(session, Speaker.Caller, t.Text);
                        break;

                    case VoiceLiveEvent.AgentTranscript t:
                        await tracker.AddTranscriptAsync(session, Speaker.Agent, t.Text);
                        break;

                    case VoiceLiveEvent.FunctionCall call:
                        toolCallsThisResponse++;
                        logger.LogInformation("Call {CallId}: tool {Tool}", session.Id, call.Name);
                        var output = await tools.HandleAsync(session, call.Name, call.Arguments, ct);
                        await connection.SendAsync(VoiceLiveProtocol.FunctionCallOutput(call.CallId, output), ct);
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
                            await tracker.AddEventAsync(session, "Voice agent handed over; caller waiting for a person");
                            await callControl.StartHoldMusicAsync(session, CancellationToken.None);
                            session.StopAi();
                        }
                        break;

                    case VoiceLiveEvent.Error e:
                        logger.LogWarning("Voice Live error on call {CallId}: {Message}", session.Id, e.Message);
                        await tracker.AddEventAsync(session, $"Voice agent error: {e.Message}");
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
            logger.LogError(ex, "Voice agent loop failed for call {CallId}", session.Id);
            await tracker.AddEventAsync(session, "Voice agent disconnected unexpectedly");
        }

        if (!aiCts.IsCancellationRequested && session.Summary.Stage is not (CallStage.QueuedForAgent or CallStage.WithAgent or CallStage.Ended))
        {
            // Voice Live dropped mid-call: never leave the caller stranded.
            await queue.EnqueueAsync(session, "Voice agent disconnected");
            await callControl.StartHoldMusicAsync(session, CancellationToken.None);
        }
    }
}
