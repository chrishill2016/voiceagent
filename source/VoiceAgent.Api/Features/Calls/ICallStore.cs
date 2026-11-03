using System.Collections.Concurrent;
using VoiceAgent.Shared;

namespace VoiceAgent.Api.Features.Calls;

/// <summary>Durable storage for call records and transcripts.</summary>
public interface ICallStore
{
    Task SaveAsync(CallDetail call, CancellationToken ct = default);
    Task<CallDetail?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<CallSummary>> ListRecentAsync(int take, CancellationToken ct = default);
}

public sealed class InMemoryCallStore : ICallStore
{
    private readonly ConcurrentDictionary<string, CallDetail> _calls = new();

    public Task SaveAsync(CallDetail call, CancellationToken ct = default)
    {
        _calls.AddOrUpdate(call.Summary.Id, call, (_, existing) =>
            existing.Summary.LastModified > call.Summary.LastModified ? existing : call);
        return Task.CompletedTask;
    }

    public Task<CallDetail?> GetAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(_calls.GetValueOrDefault(id));

    public Task<IReadOnlyList<CallSummary>> ListRecentAsync(int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CallSummary>>(_calls.Values
            .Select(c => c.Summary)
            .OrderByDescending(s => s.StartedAt)
            .Take(take)
            .ToList());
}
