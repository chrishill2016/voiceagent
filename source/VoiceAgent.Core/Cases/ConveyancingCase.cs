namespace VoiceAgent.Core.Cases;

/// <summary>
/// A client account: one conveyancing case (sale, purchase or remortgage) and the people on it.
/// </summary>
public sealed record ConveyancingCase
{
    public required string Id { get; init; }

    /// <summary>The case number clients quote, in the firm's format 1234/5678.</summary>
    public required string CaseNumber { get; init; }

    /// <summary>Everyone on the account, e.g. joint buyers.</summary>
    public required IReadOnlyList<Party> Parties { get; init; }

    /// <summary>Client's correspondence postcode.</summary>
    public required string Postcode { get; init; }

    /// <summary>Security answer: the client's previous mortgage lender. Null means they've never had one.</summary>
    public string? PreviousLender { get; init; }

    /// <summary>Sale, Purchase, Sale and purchase, Remortgage or Transfer of equity.</summary>
    public required string MatterType { get; init; }

    public required string PropertyAddress { get; init; }
    public required string PropertyPostcode { get; init; }

    /// <summary>Current stage, e.g. "Searches", "Enquiries", "Ready to exchange", "Exchanged", "Completed".</summary>
    public required string Stage { get; init; }

    public required DateOnly OpenedOn { get; init; }
    public DateOnly? LastUpdatedOn { get; init; }

    /// <summary>Name of the conveyancer handling the case.</summary>
    public required string CaseHandler { get; init; }

    public string? LatestUpdate { get; init; }
    public string? NextStep { get; init; }

    /// <summary>Who the case is waiting on, e.g. "Local authority", "Buyer's solicitor", "You".</summary>
    public string? WaitingOn { get; init; }

    public DateOnly? TargetExchangeDate { get; init; }
    public DateOnly? CompletionDate { get; init; }

    public IReadOnlyList<Milestone> Milestones { get; init; } = [];

    public bool IsCompleted => Stage.Equals("Completed", StringComparison.OrdinalIgnoreCase);

    /// <summary>"Amelia Hughes and Tom Hughes".</summary>
    public string ClientNames => Parties.Count switch
    {
        0 => "Unknown",
        1 => Parties[0].FullName,
        _ => $"{string.Join(", ", Parties.Take(Parties.Count - 1).Select(p => p.FullName))} and {Parties[^1].FullName}",
    };
}

/// <summary>A person on the account.</summary>
public sealed record Party(string FirstName, string LastName, DateOnly DateOfBirth, string? PhoneNumber = null)
{
    public string FullName => $"{FirstName} {LastName}";
}

public enum MilestoneStatus
{
    NotStarted,
    InProgress,
    Done,
}

public sealed record Milestone(string Name, MilestoneStatus Status, DateOnly? CompletedOn = null);
