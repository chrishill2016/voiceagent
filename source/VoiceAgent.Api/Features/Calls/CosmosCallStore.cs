using System.Net;
using Microsoft.Azure.Cosmos;
using VoiceAgent.Shared;

namespace VoiceAgent.Api.Features.Calls;

public sealed class CosmosCallStore(CosmosClient client, CosmosOptions options) : ICallStore
{
    private readonly Container _container = client.GetContainer(options.Database, options.CallsContainer);

    /// <summary>One document per call, partitioned by call id.</summary>
    public sealed record CallDocument(string Id, CallSummary Summary, IReadOnlyList<TranscriptEntry> Transcript, IReadOnlyList<CallEvent> Events);

    public Task SaveAsync(CallDetail call, CancellationToken ct = default) =>
        _container.UpsertItemAsync(
            new CallDocument(call.Summary.Id, call.Summary, call.Transcript, call.Events),
            new PartitionKey(call.Summary.Id),
            cancellationToken: ct);

    public async Task<CallDetail?> GetAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var doc = (await _container.ReadItemAsync<CallDocument>(id, new PartitionKey(id), cancellationToken: ct)).Resource;
            return new CallDetail(doc.Summary, doc.Transcript, doc.Events);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<CallSummary>> ListRecentAsync(int take, CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT TOP @take VALUE c.summary FROM c ORDER BY c.summary.startedAt DESC")
            .WithParameter("@take", take);

        var results = new List<CallSummary>();
        using var feed = _container.GetItemQueryIterator<CallSummary>(query);
        while (feed.HasMoreResults)
            results.AddRange(await feed.ReadNextAsync(ct));

        return results;
    }
}
