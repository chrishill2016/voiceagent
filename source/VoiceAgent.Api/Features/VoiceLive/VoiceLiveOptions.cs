namespace VoiceAgent.Api.Features.VoiceLive;

public enum VoiceLiveMode
{
    /// <summary>Voice Live drives the Foundry agent created by the provisioning tool (recommended).</summary>
    Agent,

    /// <summary>Voice Live drives a realtime model directly; instructions and tools are sent from this app.</summary>
    Model,
}

public enum VoiceLiveAuthMode
{
    /// <summary>Entra ID token in the Authorization header.</summary>
    Header,

    /// <summary>Entra ID token in the authorization query parameter.</summary>
    Query,
}

public sealed class VoiceLiveOptions
{
    public const string Section = "VoiceLive";

    /// <summary>Foundry resource endpoint, e.g. https://my-foundry.services.ai.azure.com.</summary>
    public string? Endpoint { get; set; }

    public string ApiVersion { get; set; } = "2026-04-10";
    public VoiceLiveMode Mode { get; set; } = VoiceLiveMode.Agent;
    public VoiceLiveAuthMode AuthMode { get; set; } = VoiceLiveAuthMode.Header;
    public string TokenScope { get; set; } = "https://ai.azure.com/.default";

    // Agent mode
    public string? ProjectName { get; set; }
    public string AgentName { get; set; } = Core.Agent.AgentDefinition.Name;

    /// <summary>Leave empty to use the latest version of the agent.</summary>
    public string? AgentVersion { get; set; }

    // Model mode
    public string Model { get; set; } = "gpt-realtime";

    // Audio
    public string Voice { get; set; } = "en-GB-SoniaNeural";
    public string Language { get; set; } = "en-GB";

    /// <summary>Have the agent speak first when the call connects.</summary>
    public bool GreetOnConnect { get; set; } = true;
}
