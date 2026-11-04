using Azure.Identity;
using Microsoft.Azure.Cosmos;

namespace VoiceAgent.Tests.Api;

/// <summary>
/// Guards the start-up path used in Azure, which in-memory tests never touch.
/// The Cosmos SDK loads Newtonsoft.Json when the client is created, even with the System.Text.Json serializer;
/// without the package the app crashes on start as soon as Cosmos__Endpoint is set.
/// </summary>
public class StartupTests
{
    [Fact]
    public void Cosmos_client_can_be_created_with_the_apps_options()
    {
        using var client = new CosmosClient("https://localhost:8081/", new DefaultAzureCredential(), new CosmosClientOptions
        {
            UseSystemTextJsonSerializerWithOptions = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web),
        });

        Assert.NotNull(client.GetContainer("voiceagent", "calls"));
    }
}
