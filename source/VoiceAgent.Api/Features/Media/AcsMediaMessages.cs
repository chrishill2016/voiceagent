using System.Text.Json;
using System.Text.Json.Nodes;

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
/// </summary>
public static class AcsMediaMessages
{
    public static InboundMedia Parse(string json)
    {
        JsonObject? message;
        try
        {
            message = JsonNode.Parse(json) as JsonObject;
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
        new JsonObject
        {
            ["kind"] = "AudioData",
            ["audioData"] = new JsonObject { ["data"] = base64Pcm },
            ["stopAudio"] = null,
        }.ToJsonString();

    /// <summary>Tells ACS to drop any agent audio still buffered, used when the caller interrupts.</summary>
    public static string StopAudio() =>
        new JsonObject
        {
            ["kind"] = "StopAudio",
            ["audioData"] = null,
            ["stopAudio"] = new JsonObject(),
        }.ToJsonString();

    private static string? Str(JsonObject? obj, string name) =>
        obj?[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
