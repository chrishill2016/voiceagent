using VoiceAgent.Api.Features.Telephony;
using VoiceAgent.Shared;

namespace VoiceAgent.Api.Features.Calls;

/// <summary>
/// The queue of callers waiting for a human customer service agent.
/// Callers are never locked out: anything the voice agent can't handle ends up here.
/// </summary>
public sealed class AgentQueue(CallTracker tracker, ICallControl callControl, TimeProvider time)
{
    public IReadOnlyList<CallSummary> Waiting() =>
        tracker.ActiveCalls()
            .Where(c => c.Stage == CallStage.QueuedForAgent)
            .OrderBy(c => c.QueuedAt)
            .ToList();

    /// <summary>Queues the caller for a human. The voice agent finishes its current reply, then stops.</summary>
    public async Task EnqueueAsync(CallSession session, string reason)
    {
        if (session.Summary.Stage is CallStage.QueuedForAgent or CallStage.WithAgent or CallStage.Ended)
            return;

        session.RequestHandOff();
        await tracker.UpdateAsync(session, s => s with
        {
            Stage = CallStage.QueuedForAgent,
            QueueReason = reason,
            QueuedAt = time.GetUtcNow(),
        });
        await tracker.AddEventAsync(session, $"Queued for an agent: {reason}");
    }

    /// <summary>A portal user takes the call: the AI stops immediately and the phone call is transferred to them.</summary>
    public async Task<TakeCallResult> TakeAsync(string callId, string agentName, CancellationToken ct = default)
    {
        var session = tracker.Get(callId);
        if (session is null)
            return new TakeCallResult(false, "This call has already ended.");

        if (session.Summary.Stage == CallStage.WithAgent)
            return new TakeCallResult(false, $"{session.Summary.AssignedAgent} has already taken this call.");

        var name = string.IsNullOrWhiteSpace(agentName) ? "An agent" : agentName.Trim();
        var isPhoneCall = !session.Summary.IsTestCall;

        if (isPhoneCall && !callControl.CanTransfer(session))
            return new TakeCallResult(false, "Call transfer isn't configured. Set Acs__AgentTransferTarget on the API.");

        session.StopAi();

        if (isPhoneCall)
        {
            try
            {
                await callControl.TransferToAgentAsync(session, ct);
            }
            catch (Exception ex)
            {
                await tracker.AddEventAsync(session, $"Transfer to {name} failed: {ex.Message}");
                return new TakeCallResult(false, "The transfer failed. The caller is still waiting in the queue.");
            }
        }

        await tracker.UpdateAsync(session, s => s with { Stage = CallStage.WithAgent, AssignedAgent = name });
        await tracker.AddEventAsync(session, isPhoneCall
            ? $"{name} took the call; transferring the caller"
            : $"{name} took the test call (no audio transfer for browser test calls)");

        return new TakeCallResult(true, isPhoneCall ? "Transferring the caller to you now." : "Test call marked as taken.");
    }
}
