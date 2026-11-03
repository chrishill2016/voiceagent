using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoiceAgent.Core.Cases;

/// <summary>Loads the fictional sample cases embedded in this assembly.</summary>
public static class SampleData
{
    private static readonly Lazy<IReadOnlyList<ConveyancingCase>> Cases = new(Load);

    public static IReadOnlyList<ConveyancingCase> LoadCases() => Cases.Value;

    private static IReadOnlyList<ConveyancingCase> Load()
    {
        using var stream = typeof(SampleData).Assembly.GetManifestResourceStream("VoiceAgent.Core.Data.sample-cases.json")
            ?? throw new InvalidOperationException("Sample case data is missing from the assembly.");
        return JsonSerializer.Deserialize<List<ConveyancingCase>>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        }) ?? [];
    }
}
