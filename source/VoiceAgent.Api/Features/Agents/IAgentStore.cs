using System.Collections.Concurrent;
using Microsoft.Azure.Cosmos;
using VoiceAgent.Shared;

namespace VoiceAgent.Api.Features.Agents;

/// <summary>Durable storage for agent profiles.</summary>
public interface IAgentStore
{
    Task<IReadOnlyList<AgentInfo>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(AgentInfo agent, CancellationToken ct = default);
}

public sealed class InMemoryAgentStore : IAgentStore
{
    private readonly ConcurrentDictionary<string, AgentInfo> _agents = new();

    public Task<IReadOnlyList<AgentInfo>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AgentInfo>>(_agents.Values.ToList());

    public Task SaveAsync(AgentInfo agent, CancellationToken ct = default)
    {
        _agents[agent.Id] = agent;
        return Task.CompletedTask;
    }
}

public sealed class CosmosAgentStore(CosmosClient client, CosmosOptions options) : IAgentStore
{
    private readonly Container _container = client.GetContainer(options.Database, options.AgentsContainer);

    public async Task<IReadOnlyList<AgentInfo>> ListAsync(CancellationToken ct = default)
    {
        var results = new List<AgentInfo>();
        using var feed = _container.GetItemQueryIterator<AgentInfo>("SELECT * FROM c");
        while (feed.HasMoreResults)
            results.AddRange(await feed.ReadNextAsync(ct));
        return results;
    }

    public Task SaveAsync(AgentInfo agent, CancellationToken ct = default) =>
        _container.UpsertItemAsync(agent, new PartitionKey(agent.Id), cancellationToken: ct);
}
