namespace VoiceAgent.Core.Customers;

public interface ICustomerRepository
{
    Task<Customer?> FindByAccountNumberAsync(string accountNumber, CancellationToken ct = default);
    Task<Customer?> FindByCaseReferenceAsync(string caseReference, CancellationToken ct = default);
    Task<Customer?> FindByPhoneNumberAsync(string phoneNumber, CancellationToken ct = default);
    Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default);
}
