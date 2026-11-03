using Microsoft.Extensions.Options;
using VoiceAgent.Api.Features.Agents;
using VoiceAgent.Api.Features.Telephony;
using VoiceAgent.Shared;

namespace VoiceAgent.Api.Features.Calls;

/// <summary>
/// The queue of callers waiting for a member of the client services team.
/// Callers are never locked out: anything the voice agent can't handle ends up here.
/// Waiting calls are offered, oldest first, to the longest-idle available agent. Each agent's calls
/// are transferred to their own number or Teams ID, so several agents can take calls at once.
/// </summary>
public sealed class AgentQueue
{
    private readonly CallTracker _tracker;
    private readonly AgentDirectory _agents;
    private readonly ICallControl _callControl;
    private readonly TimeProvider _time;
    private readonly QueueOptions _options;
    private readonly SemaphoreSlim _dispatchLock = new(1, 1);

    public AgentQueue(CallTracker tracker, AgentDirectory agents, ICallControl callControl, TimeProvider time, IOptions<QueueOptions> options)
    {
        _tracker = tracker;
        _agents = agents;
        _callControl = callControl;
        _time = time;
        _options = options.Value;
        _tracker.CallEnded += OnCallEndedAsync;
    }

    private TimeSpan OfferTimeout => TimeSpan.FromSeconds(Math.Max(5, _options.OfferTimeoutSeconds));
    private TimeSpan DeclineCooldown => TimeSpan.FromSeconds(Math.Max(0, _options.DeclineCooldownSeconds));

    public IReadOnlyList<CallSummary> Waiting() =>
        _tracker.ActiveCalls()
            .Where(c => c.Stage == CallStage.QueuedForAgent)
            .OrderBy(c => c.QueuedAt)
            .ToList();

    /// <summary>Queues the caller for a person. The voice agent finishes its current reply, then stops.</summary>
    public async Task EnqueueAsync(CallSession session, string reason)
    {
        if (session.Summary.Stage is CallStage.QueuedForAgent or CallStage.WithAgent or CallStage.Ended)
            return;

        session.RequestHandOff();
        await _tracker.UpdateAsync(session, s => s with
        {
            Stage = CallStage.QueuedForAgent,
            QueueReason = reason,
            QueuedAt = _time.GetUtcNow(),
        });
        await _tracker.AddEventAsync(session, $"Queued for an agent: {reason}");
        await DispatchAsync();
    }

    /// <summary>An agent takes a call (an offer they accepted, or one they picked from the queue).</summary>
    public async Task<TakeCallResult> TakeAsync(string callId, string agentId, CancellationToken ct = default)
    {
        var session = _tracker.Get(callId);
        if (session is null)
            return new TakeCallResult(false, "This call has already ended.");

        var agent = _agents.Get(agentId);
        if (agent is null)
            return new TakeCallResult(false, "Set up your agent profile before taking calls.");

        var summary = session.Summary;
        if (summary.Stage == CallStage.WithAgent)
            return new TakeCallResult(false, $"{summary.AssignedAgent} has already taken this call.");

        var offeredToMe = summary.OfferedToAgentId == agent.Id;
        if (!offeredToMe && agent.Status != AgentStatus.Available)
            return new TakeCallResult(false, "Set yourself to Available before taking a call.");

        var isPhoneCall = !summary.IsTestCall;
        if (isPhoneCall && !_callControl.CanTransfer(session))
            return new TakeCallResult(false, "This call can't be transferred because telephony isn't configured on the API.");

        session.StopAi();

        if (isPhoneCall)
        {
            try
            {
                await _callControl.TransferToAgentAsync(session, agent.TransferTarget, ct);
            }
            catch (Exception ex)
            {
                await _tracker.AddEventAsync(session, $"Transfer to {agent.Name} failed: {ex.Message}");
                session.RecordDecline(agent.Id, _time.GetUtcNow());
                await ClearOfferAsync(session);
                await DispatchAsync();
                return new TakeCallResult(false, "The transfer failed. The caller is still waiting in the queue.");
            }
        }

        await _tracker.UpdateAsync(session, s => s with
        {
            Stage = CallStage.WithAgent,
            AssignedAgent = agent.Name,
            AssignedAgentId = agent.Id,
            OfferedToAgentId = null,
            OfferedToAgentName = null,
            OfferExpiresAt = null,
        });
        await _agents.AssignCallAsync(agent.Id, callId);
        await _tracker.AddEventAsync(session, isPhoneCall
            ? $"{agent.Name} took the call; transferring the caller to {agent.TransferTarget}"
            : $"{agent.Name} took the test call (no audio transfer for browser test calls)");

        await DispatchAsync();
        return new TakeCallResult(true, isPhoneCall
            ? "Transferring the caller to you now."
            : "Test call taken. Set yourself back to Available when you're done.");
    }

    /// <summary>The agent turned down the offer; it moves on to the next available agent.</summary>
    public async Task<TakeCallResult> DeclineAsync(string callId, string agentId)
    {
        var session = _tracker.Get(callId);
        if (session is null || session.Summary.OfferedToAgentId != agentId)
            return new TakeCallResult(true, "This call is no longer offered to you.");

        var name = session.Summary.OfferedToAgentName;
        session.RecordDecline(agentId, _time.GetUtcNow());
        await ClearOfferAsync(session);
        await _tracker.AddEventAsync(session, $"{name} declined the call");
        await DispatchAsync();
        return new TakeCallResult(true, "Call passed to the next available agent.");
    }

    /// <summary>ACS reported the transfer failed: put the caller back in the queue and free the agent.</summary>
    public async Task TransferFailedAsync(CallSession session, string? reason)
    {
        var agentId = session.Summary.AssignedAgentId;
        await _tracker.AddEventAsync(session, $"Transfer failed: {reason ?? "unknown reason"}. Caller back in the queue");
        await _tracker.UpdateAsync(session, s => s with
        {
            Stage = CallStage.QueuedForAgent,
            AssignedAgent = null,
            AssignedAgentId = null,
        });

        if (agentId is not null)
        {
            session.RecordDecline(agentId, _time.GetUtcNow());
            await _agents.SetStatusAsync(agentId, AgentStatus.Available);
        }

        await DispatchAsync();
    }

    /// <summary>Called when an agent changes status: withdraws their offers if they've stepped away, and re-dispatches.</summary>
    public async Task AgentStatusChangedAsync(string agentId, AgentStatus status)
    {
        if (status != AgentStatus.Available)
        {
            foreach (var call in Waiting().Where(c => c.OfferedToAgentId == agentId))
            {
                if (_tracker.Get(call.Id) is { } session)
                    await ClearOfferAsync(session);
            }
        }

        await DispatchAsync();
    }

    /// <summary>
    /// Expires unanswered offers (the agent is set Offline) and offers each waiting call to the
    /// longest-idle available agent who doesn't already have an offer and hasn't just declined it.
    /// Runs on every queue or agent change, and every second from <see cref="QueueDispatcher"/>.
    /// </summary>
    public async Task DispatchAsync()
    {
        await _dispatchLock.WaitAsync();
        try
        {
            var now = _time.GetUtcNow();

            foreach (var call in Waiting().Where(c => c.OfferedToAgentId is not null && c.OfferExpiresAt <= now))
            {
                if (_tracker.Get(call.Id) is not { } session) continue;

                var agentId = call.OfferedToAgentId!;
                session.RecordDecline(agentId, now);
                await ClearOfferAsync(session);
                await _tracker.AddEventAsync(session, $"{call.OfferedToAgentName} didn't answer; offering to the next agent");
                await _agents.SetStatusAsync(agentId, AgentStatus.Offline);
            }

            var waiting = Waiting();
            var alreadyOffered = waiting.Where(c => c.OfferedToAgentId is not null).Select(c => c.OfferedToAgentId!).ToHashSet();
            var available = _agents.List()
                .Where(a => a.Status == AgentStatus.Available && !alreadyOffered.Contains(a.Id))
                .OrderBy(a => a.StatusSince)
                .ToList();

            foreach (var call in waiting.Where(c => c.OfferedToAgentId is null))
            {
                if (available.Count == 0) break;
                if (_tracker.Get(call.Id) is not { } session) continue;

                var agent = available.FirstOrDefault(a => !session.RecentlyDeclinedBy(a.Id, now, DeclineCooldown));
                if (agent is null) continue;

                available.Remove(agent);
                await _tracker.UpdateAsync(session, s => s with
                {
                    OfferedToAgentId = agent.Id,
                    OfferedToAgentName = agent.Name,
                    OfferExpiresAt = now + OfferTimeout,
                });
                await _tracker.AddEventAsync(session, $"Offered to {agent.Name}");
            }
        }
        finally
        {
            _dispatchLock.Release();
        }
    }

    private Task ClearOfferAsync(CallSession session) =>
        _tracker.UpdateAsync(session, s => s with
        {
            OfferedToAgentId = null,
            OfferedToAgentName = null,
            OfferExpiresAt = null,
        });

    private async Task OnCallEndedAsync(CallSummary call)
    {
        // A browser test call has no phone leg, so when it ends the agent moves to wrap-up.
        // For phone calls the agent is still talking to the caller after the transfer, so they set their own status.
        if (call.IsTestCall && call.AssignedAgentId is { } agentId
            && _agents.Get(agentId) is { Status: AgentStatus.Busy, CurrentCallId: var current } && current == call.Id)
        {
            await _agents.SetStatusAsync(agentId, AgentStatus.WrapUp);
        }

        await DispatchAsync();
    }
}

/// <summary>Ticks the queue every second so unanswered offers time out and move on.</summary>
public sealed class QueueDispatcher(AgentQueue queue, AgentDirectory agents, ILogger<QueueDispatcher> logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await agents.LoadAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await queue.DispatchAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Queue dispatch failed");
            }
        }
    }
}
