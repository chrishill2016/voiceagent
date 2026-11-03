namespace VoiceAgent.Core.Customers;

public sealed record Customer
{
    public required string Id { get; init; }
    public required string AccountNumber { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string PhoneNumber { get; init; }
    public required DateOnly DateOfBirth { get; init; }
    public required string Postcode { get; init; }
    public required string MemorableWord { get; init; }
    public IReadOnlyList<CustomerCase> Cases { get; init; } = [];

    public string FullName => $"{FirstName} {LastName}";

    public string MaskedAccountNumber =>
        AccountNumber.Length <= 4 ? AccountNumber : $"****{AccountNumber[^4..]}";
}

public sealed record CustomerCase
{
    public required string Reference { get; init; }
    public required string Title { get; init; }
    public required string Category { get; init; }
    public required string Status { get; init; }
    public required DateOnly OpenedOn { get; init; }
    public DateOnly? LastUpdatedOn { get; init; }
    public string? LatestUpdate { get; init; }
    public string? NextStep { get; init; }
}
