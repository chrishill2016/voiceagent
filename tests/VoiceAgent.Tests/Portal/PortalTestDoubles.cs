using VoiceAgent.Portal.Services;
using VoiceAgent.Shared;

namespace VoiceAgent.Tests.Portal;

public sealed class FakeLiveCalls : ILiveCalls
{
    public LiveConnectionState State { get; set; } = LiveConnectionState.Connected;
    public DateTimeOffset? LastUpdate { get; set; }
    public List<CallSummary> Calls { get; } = [];
    public IReadOnlyList<CallSummary> ActiveCalls => Calls;
    public IReadOnlyList<CallSummary> Queue => Calls.Where(c => c.Stage == CallStage.QueuedForAgent).ToList();

    public event Action? Changed;
    public event Action<CallSummary>? CallUpdated;
    public event Action<TranscriptUpdate>? TranscriptAdded;
    public event Action<CallEventUpdate>? CallEventAdded;

    public Task StartAsync() => Task.CompletedTask;

    public void RaiseChanged() => Changed?.Invoke();
    public void RaiseTranscript(TranscriptUpdate update) => TranscriptAdded?.Invoke(update);
    public void RaiseCallUpdated(CallSummary summary) => CallUpdated?.Invoke(summary);
    public void RaiseEvent(CallEventUpdate update) => CallEventAdded?.Invoke(update);
}

public sealed class FakeCallsApi : ICallsApi
{
    public Dictionary<string, CallDetail> Details { get; } = [];
    public List<CallSummary> Recent { get; } = [];
    public List<(string Id, string Agent)> Taken { get; } = [];

    public Task<IReadOnlyList<CallSummary>> GetActiveAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CallSummary>>([]);
    public Task<IReadOnlyList<CallSummary>> GetRecentAsync(int take = 50, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CallSummary>>(Recent);
    public Task<CallDetail?> GetCallAsync(string id, CancellationToken ct = default) => Task.FromResult(Details.GetValueOrDefault(id));

    public Task<TakeCallResult> TakeCallAsync(string id, string agentName, CancellationToken ct = default)
    {
        Taken.Add((id, agentName));
        return Task.FromResult(new TakeCallResult(true, "ok"));
    }

    public Task<IReadOnlyList<DemoCustomer>> GetDemoCustomersAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DemoCustomer>>([new("10234567", "Amelia Hughes", "+447700900101", new DateOnly(1985, 3, 14), "LS1 4AP", "lighthouse", ["CV2041 – Purchase, Searches"])]);
}

public static class Calls
{
    public static CallSummary Make(string id, CallStage stage, Action<CallSummary>? _ = null) => new()
    {
        Id = id,
        CallerNumber = $"+4477009001{id[^2..]}",
        StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
        Stage = stage,
        LastModified = DateTimeOffset.UtcNow,
    };
}
