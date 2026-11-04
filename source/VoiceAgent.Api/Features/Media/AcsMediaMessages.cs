using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Communication.CallAutomation;

namespace VoiceAgent.Api.Features.Media;

public abstract record InboundMedia
{
    public sealed record Audio(string Base64Pcm, bool Silent) : InboundMedia;
    public sealed record Metadata(int SampleRate, int Channels) : InboundMedia;
    public sealed record Ignored(string Kind) : InboundMedia;
}

/// <summary>
/// The JSON messages Azure Communication Services uses for bidirectional media streaming.
/// The browser test call page speaks the same format, so both share one code path.
/// Outbound messages are built with the Call Automation SDK so the casing ("Kind", "AudioData", "Data")
/// is exactly what ACS expects; inbound parsing accepts either casing.
/// </summary>
public static class AcsMediaMessages
{
    private static readonly JsonNodeOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    public static InboundMedia Parse(string json)
    {
        JsonObject? message;
        try
        {
            message = JsonNode.Parse(json, CaseInsensitive) as JsonObject;
        }
        catch (JsonException)
        {
            return new InboundMedia.Ignored("invalid-json");
        }

        var kind = Str(message, "kind") ?? string.Empty;

        if (kind.Equals("AudioData", StringComparison.OrdinalIgnoreCase)
            && message?["audioData"] is JsonObject audio
            && Str(audio, "data") is { Length: > 0 } data)
        {
            var silent = audio["silent"] is JsonValue s && s.TryGetValue<bool>(out var b) && b;
            return new InboundMedia.Audio(data, silent);
        }

        if (kind.Equals("AudioMetadata", StringComparison.OrdinalIgnoreCase) && message?["audioMetadata"] is JsonObject meta)
        {
            return new InboundMedia.Metadata(
                meta["sampleRate"]?.GetValue<int>() ?? 0,
                meta["channels"]?.GetValue<int>() ?? 0);
        }

        return new InboundMedia.Ignored(kind);
    }

    public static string OutboundAudio(string base64Pcm) =>
        OutStreamingData.GetAudioDataForOutbound(Convert.FromBase64String(base64Pcm));

    /// <summary>Tells ACS to drop any agent audio still buffered, used when the caller interrupts.</summary>
    public static string StopAudio() => OutStreamingData.GetStopAudioForOutbound();

    private static string? Str(JsonObject? obj, string name) =>
        obj?[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
