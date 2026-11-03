using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using VoiceAgent.Core.Customers;

namespace VoiceAgent.Api.Features.Customers;

/// <summary>
/// Customers stored in Cosmos DB. The sample dataset is small, so lookups load the list and reuse the
/// same normalisation as the in-memory store; swap this for indexed queries or a CRM call in production.
/// </summary>
public sealed class CosmosCustomerRepository(CosmosClient client, IOptions<CosmosOptions> options) : ICustomerRepository
{
    private readonly Container _container = client.GetContainer(options.Value.Database, options.Value.CustomersContainer);

    public async Task<Customer?> FindByAccountNumberAsync(string accountNumber, CancellationToken ct = default) =>
        await new InMemoryCustomerRepository(await ListAsync(ct)).FindByAccountNumberAsync(accountNumber, ct);

    public async Task<Customer?> FindByCaseReferenceAsync(string caseReference, CancellationToken ct = default) =>
        await new InMemoryCustomerRepository(await ListAsync(ct)).FindByCaseReferenceAsync(caseReference, ct);

    public async Task<Customer?> FindByPhoneNumberAsync(string phoneNumber, CancellationToken ct = default) =>
        await new InMemoryCustomerRepository(await ListAsync(ct)).FindByPhoneNumberAsync(phoneNumber, ct);

    public async Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default)
    {
        var results = new List<Customer>();
        using var feed = _container.GetItemQueryIterator<Customer>("SELECT * FROM c");
        while (feed.HasMoreResults)
            results.AddRange(await feed.ReadNextAsync(ct));
        return results;
    }

    public Task UpsertAsync(Customer customer, CancellationToken ct = default) =>
        _container.UpsertItemAsync(customer, new PartitionKey(customer.Id), cancellationToken: ct);
}

/// <summary>Loads the fictional sample customers into Cosmos DB at startup.</summary>
public sealed class SampleDataSeeder(CosmosCustomerRepository repository, IOptions<DemoOptions> demo, ILogger<SampleDataSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!demo.Value.SeedSampleData) return;

        try
        {
            foreach (var customer in SampleData.LoadCustomers())
                await repository.UpsertAsync(customer, cancellationToken);
            logger.LogInformation("Seeded sample customers into Cosmos DB");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not seed sample customers into Cosmos DB");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
