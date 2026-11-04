using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Communication.CallAutomation;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using VoiceAgent.Api;
using VoiceAgent.Api.Features.Agents;
using VoiceAgent.Api.Features.Calls;
using VoiceAgent.Api.Features.Cases;
using VoiceAgent.Api.Features.Media;
using VoiceAgent.Api.Features.Telephony;
using VoiceAgent.Api.Features.Tools;
using VoiceAgent.Api.Features.VoiceLive;
using VoiceAgent.Core.Cases;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.Configure<AcsOptions>(config.GetSection(AcsOptions.Section));
builder.Services.Configure<CosmosOptions>(config.GetSection(CosmosOptions.Section));
builder.Services.Configure<DemoOptions>(config.GetSection(DemoOptions.Section));
builder.Services.Configure<VoiceLiveOptions>(config.GetSection(VoiceLiveOptions.Section));
builder.Services.Configure<QueueOptions>(config.GetSection(QueueOptions.Section));

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

// Managed identity in Azure, your `az login` locally. No keys or connection strings anywhere.
builder.Services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
builder.Services.AddSingleton(TimeProvider.System);

// Storage: Cosmos DB when configured, otherwise in-memory (local development and tests).
var cosmos = config.GetSection(CosmosOptions.Section).Get<CosmosOptions>() ?? new CosmosOptions();
if (!string.IsNullOrWhiteSpace(cosmos.Endpoint))
{
    builder.Services.AddSingleton(sp => new CosmosClient(cosmos.Endpoint, sp.GetRequiredService<TokenCredential>(), new CosmosClientOptions
    {
        UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        },
    }));
    builder.Services.AddSingleton(sp => new CosmosCallStore(sp.GetRequiredService<CosmosClient>(), cosmos));
    builder.Services.AddSingleton<ICallStore>(sp => sp.GetRequiredService<CosmosCallStore>());
    builder.Services.AddSingleton<CosmosCaseRepository>();
    builder.Services.AddSingleton<ICaseRepository>(sp => sp.GetRequiredService<CosmosCaseRepository>());
    builder.Services.AddHostedService<SampleDataSeeder>();
    builder.Services.AddSingleton<IAgentStore>(sp => new CosmosAgentStore(sp.GetRequiredService<CosmosClient>(), cosmos));
}
else
{
    builder.Services.AddSingleton<ICallStore, InMemoryCallStore>();
    builder.Services.AddSingleton<ICaseRepository>(_ => new InMemoryCaseRepository(SampleData.LoadCases()));
    builder.Services.AddSingleton<IAgentStore, InMemoryAgentStore>();
}

// Telephony: Azure Communication Services Call Automation, when configured.
var acsEndpoint = config[$"{AcsOptions.Section}:{nameof(AcsOptions.Endpoint)}"];
if (!string.IsNullOrWhiteSpace(acsEndpoint))
{
    builder.Services.AddSingleton(sp => new CallAutomationClient(new Uri(acsEndpoint), sp.GetRequiredService<TokenCredential>(), new CallAutomationClientOptions()));
}
builder.Services.AddSingleton<ICallControl>(sp => new AcsCallControl(
    sp.GetRequiredService<IOptions<AcsOptions>>(),
    sp.GetRequiredService<ILogger<AcsCallControl>>(),
    sp.GetService<CallAutomationClient>()));

builder.Services.AddSingleton<ICallNotifier, SignalRCallNotifier>();
builder.Services.AddSingleton<CallTracker>();
builder.Services.AddSingleton<AgentDirectory>();
builder.Services.AddSingleton<AgentQueue>();
builder.Services.AddHostedService<QueueDispatcher>();
builder.Services.AddSingleton<AgentToolHandler>();
builder.Services.AddSingleton<IVoiceLiveConnectionFactory, VoiceLiveConnectionFactory>();
builder.Services.AddSingleton<CallAudioBridge>();

var app = builder.Build();

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
    app.UseWebAssemblyDebugging();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });


// The agent portal (Blazor WebAssembly) is served from this app. In .NET 10 MapStaticAssets replaces
// UseBlazorFrameworkFiles/UseStaticFiles: it maps plain names such as _framework/blazor.webassembly.js
// to the fingerprinted, pre-compressed files the publish step produces.
app.MapStaticAssets();
app.MapHealthChecks("/healthz");
app.MapHub<CallsHubEndpoint>(VoiceAgent.Shared.CallsHub.Path);
app.MapCallApi();
app.MapAgentApi();
app.MapTelephony();
app.MapMedia();
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed for integration tests.</summary>
public partial class Program;
