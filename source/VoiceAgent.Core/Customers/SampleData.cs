using System.Text.Json;

namespace VoiceAgent.Core.Customers;

/// <summary>Loads the fictional sample customers embedded in this assembly.</summary>
public static class SampleData
{
    private static readonly Lazy<IReadOnlyList<Customer>> Customers = new(Load);

    public static IReadOnlyList<Customer> LoadCustomers() => Customers.Value;

    private static IReadOnlyList<Customer> Load()
    {
        using var stream = typeof(SampleData).Assembly.GetManifestResourceStream("VoiceAgent.Core.Data.sample-customers.json")
            ?? throw new InvalidOperationException("Sample customer data is missing from the assembly.");
        return JsonSerializer.Deserialize<List<Customer>>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? [];
    }
}
