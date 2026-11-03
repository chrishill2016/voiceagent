using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using VoiceAgent.Core.Cases;

namespace VoiceAgent.Api.Features.Cases;

/// <summary>
/// Cases stored in Cosmos DB. The sample dataset is small, so lookups load the list and reuse the
/// same matching as the in-memory store. Swap this for indexed queries or a case management system in production.
/// </summary>
public sealed class CosmosCaseRepository(CosmosClient client, IOptions<CosmosOptions> options) : ICaseRepository
{
    private readonly Container _container = client.GetContainer(options.Value.Database, options.Value.CasesContainer);

    public async Task<ConveyancingCase?> FindByCaseNumberAsync(string caseNumber, CancellationToken ct = default) =>
        await (await SnapshotAsync(ct)).FindByCaseNumberAsync(caseNumber, ct);

    public async Task<IReadOnlyList<ConveyancingCase>> FindByPostcodeAndSurnameAsync(string postcode, string surname, CancellationToken ct = default) =>
        await (await SnapshotAsync(ct)).FindByPostcodeAndSurnameAsync(postcode, surname, ct);

    public async Task<ConveyancingCase?> FindByPhoneNumberAsync(string phoneNumber, CancellationToken ct = default) =>
        await (await SnapshotAsync(ct)).FindByPhoneNumberAsync(phoneNumber, ct);

    public async Task<IReadOnlyList<ConveyancingCase>> ListAsync(CancellationToken ct = default)
    {
        var results = new List<ConveyancingCase>();
        using var feed = _container.GetItemQueryIterator<ConveyancingCase>("SELECT * FROM c");
        while (feed.HasMoreResults)
            results.AddRange(await feed.ReadNextAsync(ct));
        return results;
    }

    public Task UpsertAsync(ConveyancingCase account, CancellationToken ct = default) =>
        _container.UpsertItemAsync(account, new PartitionKey(account.Id), cancellationToken: ct);

    private async Task<InMemoryCaseRepository> SnapshotAsync(CancellationToken ct) => new(await ListAsync(ct));
}

/// <summary>Loads the fictional sample cases into Cosmos DB at startup.</summary>
public sealed class SampleDataSeeder(CosmosCaseRepository repository, IOptions<DemoOptions> demo, ILogger<SampleDataSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!demo.Value.SeedSampleData) return;

        try
        {
            foreach (var account in SampleData.LoadCases())
                await repository.UpsertAsync(account, cancellationToken);
            logger.LogInformation("Seeded sample cases into Cosmos DB");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not seed sample cases into Cosmos DB");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
