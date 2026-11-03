namespace VoiceAgent.Shared;

public enum AgentStatus
{
    /// <summary>Not taking calls (also set automatically when an offered call is missed).</summary>
    Offline,

    /// <summary>Ready: queued calls are offered to available agents, longest-idle first.</summary>
    Available,

    /// <summary>On a call, or otherwise unavailable.</summary>
    Busy,

    /// <summary>Finishing notes after a call. Not offered calls.</summary>
    WrapUp,
}

/// <summary>A member of the client services team who can take calls from the queue.</summary>
public sealed record AgentInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Where this agent's calls are transferred: E.164 phone number (+44...), ACS user (8:acs:...) or Teams user (8:orgid:...).</summary>
    public required string TransferTarget { get; init; }

    public AgentStatus Status { get; init; } = AgentStatus.Offline;
    public DateTimeOffset StatusSince { get; init; }

    /// <summary>The call the agent took most recently, until they make themselves available again.</summary>
    public string? CurrentCallId { get; init; }

    public DateTimeOffset LastModified { get; init; }
}

public sealed record SaveAgentRequest(string Name, string TransferTarget);

public sealed record SetAgentStatusRequest(AgentStatus Status);

public sealed record AgentResult(bool Success, string Message, AgentInfo? Agent = null);

public sealed record DeclineCallRequest(string AgentId);
