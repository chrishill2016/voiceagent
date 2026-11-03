namespace VoiceAgent.Shared;

/// <summary>Where a call currently is in the customer journey.</summary>
public enum CallStage
{
    Connecting,
    IdentifyingCase,
    Verifying,
    Verified,
    QueuedForAgent,
    WithAgent,
    Ended,
}

public enum Speaker
{
    Caller,
    Agent,
    System,
}

public enum VerificationStatus
{
    NotStarted,
    InProgress,
    Passed,
    Failed,
}

public sealed record TranscriptEntry(Speaker Speaker, string Text, DateTimeOffset Timestamp);

public sealed record CallEvent(string Description, DateTimeOffset Timestamp);

/// <summary>Everything a portal user needs to see about a call at a glance.</summary>
public sealed record CallSummary
{
    public required string Id { get; init; }
    public required string CallerNumber { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public CallStage Stage { get; init; } = CallStage.Connecting;
    public bool IsTestCall { get; init; }

    /// <summary>Why the caller says they are calling, as summarised by the voice agent.</summary>
    public string? CallReason { get; init; }
    public string? CallCategory { get; init; }

    /// <summary>Case number (1234/5678) the caller has been matched to. Not proof of identity until verified.</summary>
    public string? CaseReference { get; init; }

    /// <summary>Names on the account. Only populated once the caller has passed security.</summary>
    public string? ClientName { get; init; }
    public string? CaseId { get; init; }

    public VerificationStatus Verification { get; init; } = VerificationStatus.NotStarted;
    public int VerificationAttempts { get; init; }

    public string? QueueReason { get; init; }
    public DateTimeOffset? QueuedAt { get; init; }
    public string? AssignedAgent { get; init; }
    public string? AssignedAgentId { get; init; }

    /// <summary>The available agent this queued call is currently being offered to.</summary>
    public string? OfferedToAgentId { get; init; }
    public string? OfferedToAgentName { get; init; }
    public DateTimeOffset? OfferExpiresAt { get; init; }

    public DateTimeOffset LastModified { get; init; }
}

public sealed record CallDetail(CallSummary Summary, IReadOnlyList<TranscriptEntry> Transcript, IReadOnlyList<CallEvent> Events);

public sealed record TranscriptUpdate(string CallId, TranscriptEntry Entry);

public sealed record CallEventUpdate(string CallId, CallEvent Event);

public sealed record TakeCallRequest(string AgentId);

public sealed record TakeCallResult(bool Success, string Message);

/// <summary>Demo-only view of the sample cases so testers know which answers to give.</summary>
public sealed record DemoCase(
    string CaseNumber,
    string MatterType,
    string Stage,
    string PropertyAddress,
    string PropertyPostcode,
    string Postcode,
    IReadOnlyList<DemoParty> Parties,
    string? PreviousLender);

public sealed record DemoParty(string FullName, DateOnly DateOfBirth, string? PhoneNumber);

public static class CallsHub
{
    public const string Path = "/hubs/calls";
    public const string CallUpdated = nameof(CallUpdated);
    public const string TranscriptAdded = nameof(TranscriptAdded);
    public const string CallEventAdded = nameof(CallEventAdded);
    public const string AgentUpdated = nameof(AgentUpdated);
}
