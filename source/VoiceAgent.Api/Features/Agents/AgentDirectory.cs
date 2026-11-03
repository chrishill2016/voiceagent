using System.Collections.Concurrent;
using VoiceAgent.Api.Features.Calls;
using VoiceAgent.Shared;

namespace VoiceAgent.Api.Features.Agents;

/// <summary>
/// The client services team: each agent's profile, live status and own transfer target.
/// Profiles are persisted; live status is held here and broadcast to the portal.
/// </summary>
public sealed class AgentDirectory(IAgentStore store, ICallNotifier notifier, TimeProvider time, ILogger<AgentDirectory> logger)
{
    private readonly ConcurrentDictionary<string, AgentInfo> _agents = new();

    /// <summary>Loads saved profiles. Everyone starts Offline after a restart so no call is offered to someone who isn't there.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var now = time.GetUtcNow();
            foreach (var agent in await store.ListAsync(ct))
                _agents[agent.Id] = agent with { Status = AgentStatus.Offline, StatusSince = now, CurrentCallId = null };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not load agent profiles");
        }
    }

    public IReadOnlyList<AgentInfo> List() => _agents.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public AgentInfo? Get(string? id) => id is null ? null : _agents.GetValueOrDefault(id);

    public async Task<AgentResult> CreateAsync(SaveAgentRequest request)
    {
        if (!Validate(request, out var name, out var target, out var error))
            return new AgentResult(false, error);

        if (_agents.Values.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return new AgentResult(false, $"There is already an agent called {name}. Pick them from the list instead.");

        var now = time.GetUtcNow();
        var agent = new AgentInfo
        {
            Id = Guid.NewGuid().ToString("n"),
            Name = name,
            TransferTarget = target,
            Status = AgentStatus.Offline,
            StatusSince = now,
            LastModified = now,
        };

        await SaveAsync(agent);
        return new AgentResult(true, $"Welcome, {name}.", agent);
    }

    public async Task<AgentResult> UpdateAsync(string id, SaveAgentRequest request)
    {
        if (Get(id) is not { } existing)
            return new AgentResult(false, "That agent no longer exists.");

        if (!Validate(request, out var name, out var target, out var error))
            return new AgentResult(false, error);

        var agent = existing with { Name = name, TransferTarget = target };
        await SaveAsync(agent);
        return new AgentResult(true, "Profile saved.", agent);
    }

    public async Task<AgentResult> SetStatusAsync(string id, AgentStatus status)
    {
        if (Get(id) is not { } existing)
            return new AgentResult(false, "That agent no longer exists.");

        if (existing.Status == status)
            return new AgentResult(true, "No change.", existing);

        var agent = existing with
        {
            Status = status,
            StatusSince = time.GetUtcNow(),
            // Becoming available (or going offline) means the previous call is finished with.
            CurrentCallId = status is AgentStatus.Available or AgentStatus.Offline ? null : existing.CurrentCallId,
        };

        await SaveAsync(agent);
        return new AgentResult(true, $"You're now {Describe(status)}.", agent);
    }

    /// <summary>Marks the agent Busy on the call they've just taken.</summary>
    public async Task AssignCallAsync(string id, string callId)
    {
        if (Get(id) is not { } existing) return;
        await SaveAsync(existing with { Status = AgentStatus.Busy, StatusSince = time.GetUtcNow(), CurrentCallId = callId });
    }

    public static string Describe(AgentStatus status) => status switch
    {
        AgentStatus.Available => "available",
        AgentStatus.Busy => "busy",
        AgentStatus.WrapUp => "in wrap-up",
        _ => "offline",
    };

    private async Task SaveAsync(AgentInfo agent)
    {
        agent = agent with { LastModified = time.GetUtcNow() };
        _agents[agent.Id] = agent;

        try
        {
            await store.SaveAsync(agent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not save agent {AgentId}", agent.Id);
        }

        try
        {
            await notifier.AgentUpdatedAsync(agent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not broadcast agent {AgentId}", agent.Id);
        }
    }

    private static bool Validate(SaveAgentRequest request, out string name, out string target, out string error)
    {
        name = (request.Name ?? string.Empty).Trim();
        target = string.Empty;

        if (name.Length is 0 or > 60)
        {
            error = "Enter your name (up to 60 characters).";
            return false;
        }

        return TransferTargets.TryNormalise(request.TransferTarget, out target, out error);
    }
}
