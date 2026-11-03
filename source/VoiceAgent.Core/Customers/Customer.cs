namespace VoiceAgent.Core.Customers;

public sealed record Customer
{
    public required string Id { get; init; }
    public required string ClientNumber { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string PhoneNumber { get; init; }
    public required DateOnly DateOfBirth { get; init; }
    public required string Postcode { get; init; }
    public required string MemorableWord { get; init; }
    public IReadOnlyList<CustomerCase> Cases { get; init; } = [];

    public string FullName => $"{FirstName} {LastName}";

    public string MaskedClientNumber =>
        ClientNumber.Length <= 4 ? ClientNumber : $"****{ClientNumber[^4..]}";
}

/// <summary>A conveyancing matter (sale, purchase or remortgage) the firm is handling for the client.</summary>
public sealed record CustomerCase
{
    /// <summary>Matter reference, e.g. CV2041.</summary>
    public required string Reference { get; init; }

    /// <summary>Sale, Purchase, Sale and purchase, Remortgage or Transfer of equity.</summary>
    public required string MatterType { get; init; }

    public required string PropertyAddress { get; init; }

    /// <summary>Current stage, e.g. "Searches", "Enquiries", "Ready to exchange", "Exchanged", "Completed".</summary>
    public required string Stage { get; init; }

    public required DateOnly OpenedOn { get; init; }
    public DateOnly? LastUpdatedOn { get; init; }

    /// <summary>Name of the conveyancer handling the matter.</summary>
    public required string CaseHandler { get; init; }

    public string? LatestUpdate { get; init; }
    public string? NextStep { get; init; }

    /// <summary>Who the matter is waiting on, e.g. "Local authority", "Buyer's solicitor", "You".</summary>
    public string? WaitingOn { get; init; }

    public DateOnly? TargetExchangeDate { get; init; }
    public DateOnly? CompletionDate { get; init; }

    public IReadOnlyList<Milestone> Milestones { get; init; } = [];

    public bool IsCompleted => Stage.Equals("Completed", StringComparison.OrdinalIgnoreCase);
}

public enum MilestoneStatus
{
    NotStarted,
    InProgress,
    Done,
}

public sealed record Milestone(string Name, MilestoneStatus Status, DateOnly? CompletedOn = null);
