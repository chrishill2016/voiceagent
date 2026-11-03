using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;
using VoiceAgent.Core.Agent;

// Creates (or adds a new version of) the customer service voice agent in Azure AI Foundry.
// Usage:
//   dotnet run --project source/VoiceAgent.Provisioning -- \
//       --project-endpoint https://<foundry>.services.ai.azure.com/api/projects/<project> \
//       [--model gpt-4.1-mini] [--api-version 2025-11-15-preview] [--dry-run]

var options = ProvisioningOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("""
        Usage: VoiceAgent.Provisioning --project-endpoint <url> [--model <deployment>] [--api-version <version>] [--dry-run]

          --project-endpoint  Foundry project endpoint (Foundry portal > project > Overview), or set FOUNDRY_PROJECT_ENDPOINT.
          --model             Model deployment name the agent uses for reasoning. Default: gpt-4.1-mini.
          --api-version       Foundry Agents API version. Default: 2025-11-15-preview.
          --dry-run           Print the agent definition JSON without calling Azure.
        """);
    return 1;
}

var body = AgentPayload.Build(options.Model);

if (options.DryRun)
{
    Console.WriteLine(body.ToJsonString(new() { WriteIndented = true }));
    return 0;
}

var credential = new DefaultAzureCredential();
var token = await credential.GetTokenAsync(new TokenRequestContext(["https://ai.azure.com/.default"]));

using var http = new HttpClient();
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

var url = $"{options.ProjectEndpoint.TrimEnd('/')}/agents/{AgentDefinition.Name}/versions?api-version={options.ApiVersion}";
Console.WriteLine($"Creating agent '{AgentDefinition.Name}' in {options.ProjectEndpoint} ...");

using var response = await http.PostAsync(url, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
var responseText = await response.Content.ReadAsStringAsync();

if (!response.IsSuccessStatusCode)
{
    Console.Error.WriteLine($"Failed: {(int)response.StatusCode} {response.ReasonPhrase}");
    Console.Error.WriteLine(responseText);
    return 2;
}

var created = JsonNode.Parse(responseText);
Console.WriteLine($"Done. Agent '{created?["name"] ?? AgentDefinition.Name}' version {created?["version"] ?? "?"} is ready.");
Console.WriteLine("Next: in the Foundry portal, open the agent and check Voice Live is enabled (see docs/SETUP.md).");
return 0;

internal sealed record ProvisioningOptions(string ProjectEndpoint, string Model, string ApiVersion, bool DryRun)
{
    public static ProvisioningOptions? Parse(string[] args)
    {
        string? Value(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        var dryRun = args.Contains("--dry-run");
        var endpoint = Value("--project-endpoint") ?? Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint) && !dryRun) return null;

        return new ProvisioningOptions(
            endpoint ?? string.Empty,
            Value("--model") ?? "gpt-4.1-mini",
            Value("--api-version") ?? "2025-11-15-preview",
            dryRun);
    }
}

public static class AgentPayload
{
    /// <summary>The Foundry prompt-agent definition, built from <see cref="AgentDefinition"/>.</summary>
    public static JsonObject Build(string model) => new()
    {
        ["description"] = AgentDefinition.Description,
        ["metadata"] = new JsonObject { ["app"] = "voiceagent-customer-services" },
        ["definition"] = new JsonObject
        {
            ["kind"] = "prompt",
            ["model"] = model,
            ["instructions"] = AgentDefinition.Instructions,
            ["tools"] = new JsonArray(AgentDefinition.Tools.Select(t => (JsonNode)t.ToFunctionTool()).ToArray()),
        },
    };
}
