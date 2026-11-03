using Microsoft.AspNetCore.SignalR;
using VoiceAgent.Shared;

namespace VoiceAgent.Api.Features.Calls;

/// <summary>Pushes live call updates to the portal.</summary>
public interface ICallNotifier
{
    Task CallUpdatedAsync(CallSummary summary);
    Task TranscriptAddedAsync(TranscriptUpdate update);
    Task CallEventAddedAsync(CallEventUpdate update);
    Task AgentUpdatedAsync(AgentInfo agent);
}

/// <summary>SignalR hub the portal connects to. All messages are server-to-client.</summary>
public sealed class CallsHubEndpoint : Hub;

public sealed class SignalRCallNotifier(IHubContext<CallsHubEndpoint> hub) : ICallNotifier
{
    public Task CallUpdatedAsync(CallSummary summary) => hub.Clients.All.SendAsync(CallsHub.CallUpdated, summary);

    public Task TranscriptAddedAsync(TranscriptUpdate update) => hub.Clients.All.SendAsync(CallsHub.TranscriptAdded, update);

    public Task CallEventAddedAsync(CallEventUpdate update) => hub.Clients.All.SendAsync(CallsHub.CallEventAdded, update);

    public Task AgentUpdatedAsync(AgentInfo agent) => hub.Clients.All.SendAsync(CallsHub.AgentUpdated, agent);
}
