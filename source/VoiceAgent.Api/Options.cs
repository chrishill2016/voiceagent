namespace VoiceAgent.Api;

public sealed class AcsOptions
{
    public const string Section = "Acs";

    /// <summary>Azure Communication Services endpoint, e.g. https://my-acs.uk.communication.azure.com.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Public https base URL of this app, used for Call Automation callbacks and the media WebSocket.</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// Optional shared token appended to callback and media URLs (as ?token=...) and checked on every request.
    /// Set it as a Container Apps secret, never in source control.
    /// </summary>
    public string? CallbackToken { get; set; }

    /// <summary>
    /// Where queued calls are transferred when a portal user takes the call: an E.164 phone number (+44...),
    /// an ACS user id (8:acs:...) or a Teams user id (8:orgid:...).
    /// </summary>
    public string? AgentTransferTarget { get; set; }

    /// <summary>Optional public URL of a WAV file played to callers while they wait in the agent queue.</summary>
    public string? HoldMusicUrl { get; set; }
}

public sealed class CosmosOptions
{
    public const string Section = "Cosmos";

    /// <summary>Cosmos DB account endpoint. When empty the app uses in-memory storage.</summary>
    public string? Endpoint { get; set; }
    public string Database { get; set; } = "voiceagent";
    public string CallsContainer { get; set; } = "calls";
    public string CustomersContainer { get; set; } = "customers";
}

public sealed class DemoOptions
{
    public const string Section = "Demo";

    /// <summary>Upsert the fictional sample customers into the customer store at startup.</summary>
    public bool SeedSampleData { get; set; } = true;

    /// <summary>Show the sample customers (including their security answers) in the portal. Demo only.</summary>
    public bool ShowSampleData { get; set; } = true;
}
