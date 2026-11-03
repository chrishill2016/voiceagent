namespace VoiceAgent.Core.Customers;

/// <summary>Customer store backed by a fixed list. Used for local development and tests.</summary>
public sealed class InMemoryCustomerRepository(IEnumerable<Customer> customers) : ICustomerRepository
{
    private readonly IReadOnlyList<Customer> _customers = customers.ToList();

    public Task<Customer?> FindByClientNumberAsync(string clientNumber, CancellationToken ct = default)
    {
        var wanted = Identifiers.NormaliseClientNumber(clientNumber);
        return Task.FromResult(_customers.FirstOrDefault(c => Identifiers.NormaliseClientNumber(c.ClientNumber) == wanted));
    }

    public Task<Customer?> FindByCaseReferenceAsync(string caseReference, CancellationToken ct = default)
    {
        var wanted = Identifiers.NormaliseCaseReference(caseReference);
        return Task.FromResult(_customers.FirstOrDefault(c =>
            c.Cases.Any(x => Identifiers.NormaliseCaseReference(x.Reference) == wanted)));
    }

    public Task<Customer?> FindByPhoneNumberAsync(string phoneNumber, CancellationToken ct = default)
    {
        var wanted = Identifiers.NormalisePhoneNumber(phoneNumber);
        return Task.FromResult(_customers.FirstOrDefault(c => Identifiers.NormalisePhoneNumber(c.PhoneNumber) == wanted));
    }

    public Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default) => Task.FromResult(_customers);
}
