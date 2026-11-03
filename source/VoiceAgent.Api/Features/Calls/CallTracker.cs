using System.Collections.Concurrent;
using VoiceAgent.Shared;

namespace VoiceAgent.Api.Features.Calls;

/// <summary>
/// Owns all live calls. Every change is saved to the <see cref="ICallStore"/> and pushed to the portal.
/// </summary>
public sealed class CallTracker(ICallStore store, ICallNotifier notifier, TimeProvider time, ILogger<CallTracker> logger)
{
    private readonly ConcurrentDictionary<string, CallSession> _active = new();

    /// <summary>Raised after a call has ended and been removed from the live list.</summary>
    public event Func<CallSummary, Task>? CallEnded;

    public async Task<CallSession> StartAsync(string callId, string callerNumber, bool isTestCall)
    {
        var now = time.GetUtcNow();
        var session = new CallSession(new CallSummary
        {
            Id = callId,
            CallerNumber = callerNumber,
            StartedAt = now,
            IsTestCall = isTestCall,
            LastModified = now,
        });

        _active[callId] = session;
        session.AddEvent(new CallEvent(isTestCall ? "Browser test call started" : $"Incoming call from {callerNumber}", now));
        await PublishAsync(session, notifySummary: true);
        return session;
    }

    public CallSession? Get(string callId) => _active.GetValueOrDefault(callId);

    public IReadOnlyList<CallSummary> ActiveCalls() =>
        _active.Values.Select(s => s.Summary).OrderBy(s => s.StartedAt).ToList();

    public async Task<CallDetail?> GetDetailAsync(string callId, CancellationToken ct = default) =>
        Get(callId)?.Snapshot() ?? await store.GetAsync(callId, ct);

    public Task UpdateAsync(CallSession session, Func<CallSummary, CallSummary> change)
    {
        session.Apply(change, time.GetUtcNow());
        return PublishAsync(session, notifySummary: true);
    }

    public async Task AddTranscriptAsync(CallSession session, Speaker speaker, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var entry = new TranscriptEntry(speaker, text.Trim(), time.GetUtcNow());
        session.AddTranscript(entry);
        await SafeAsync(() => notifier.TranscriptAddedAsync(new TranscriptUpdate(session.Id, entry)));
        await PublishAsync(session, notifySummary: false);
    }

    public async Task AddEventAsync(CallSession session, string description)
    {
        var evt = new CallEvent(description, time.GetUtcNow());
        session.AddEvent(evt);
        await SafeAsync(() => notifier.CallEventAddedAsync(new CallEventUpdate(session.Id, evt)));
        await PublishAsync(session, notifySummary: false);
    }

    /// <summary>Marks the call as ended and removes it from the live list. Safe to call more than once.</summary>
    public async Task EndAsync(string callId, string reason)
    {
        if (!_active.TryRemove(callId, out var session)) return;

        session.StopAi();
        session.AddEvent(new CallEvent(reason, time.GetUtcNow()));
        var ended = session.Apply(s => s with
        {
            Stage = CallStage.Ended,
            EndedAt = time.GetUtcNow(),
            OfferedToAgentId = null,
            OfferedToAgentName = null,
            OfferExpiresAt = null,
        }, time.GetUtcNow());
        await PublishAsync(session, notifySummary: true);

        if (CallEnded is { } handler)
            await SafeAsync(() => handler(ended));
    }

    private async Task PublishAsync(CallSession session, bool notifySummary)
    {
        var snapshot = session.Snapshot();
        await SafeAsync(() => store.SaveAsync(snapshot));
        if (notifySummary)
            await SafeAsync(() => notifier.CallUpdatedAsync(snapshot.Summary));
    }

    private async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            // Storage or SignalR problems must never drop a live phone call.
            logger.LogError(ex, "Failed to persist or broadcast a call update");
        }
    }
}
