using System.Text.Json;
using System.Text.Json.Nodes;
using VoiceAgent.Core.Agent;

namespace VoiceAgent.Api.Features.VoiceLive;

/// <summary>Events from Voice Live that the call bridge cares about.</summary>
public abstract record VoiceLiveEvent
{
    /// <summary>session.created: the connection is open, but our session.update may not be applied yet.</summary>
    public sealed record SessionCreated : VoiceLiveEvent;

    /// <summary>session.updated: our settings are applied, so a response can be requested.</summary>
    public sealed record SessionReady : VoiceLiveEvent;

    public sealed record ResponseCreated : VoiceLiveEvent;
    public sealed record AudioDelta(string Base64Audio) : VoiceLiveEvent;
    public sealed record CallerStartedSpeaking : VoiceLiveEvent;
    public sealed record CallerStoppedSpeaking : VoiceLiveEvent;
    public sealed record CallerTranscript(string Text) : VoiceLiveEvent;
    public sealed record AgentTranscript(string Text) : VoiceLiveEvent;
    public sealed record FunctionCall(string CallId, string Name, string Arguments) : VoiceLiveEvent;
    public sealed record ResponseDone : VoiceLiveEvent;
    public sealed record Error(string Message) : VoiceLiveEvent;
    public sealed record Other(string Type) : VoiceLiveEvent;
}

/// <summary>
/// Builds and parses Voice Live realtime WebSocket messages. Kept free of I/O so it can be unit tested.
/// </summary>
public static class VoiceLiveProtocol
{
    public static Uri BuildUri(VoiceLiveOptions options, string? accessToken = null)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint))
            throw new InvalidOperationException("VoiceLive:Endpoint is not configured.");

        var builder = new UriBuilder(options.Endpoint)
        {
            Scheme = "wss",
            Port = -1,
            Path = "/voice-live/realtime",
        };

        var query = new List<(string Key, string? Value)> { ("api-version", options.ApiVersion) };

        if (options.Mode == VoiceLiveMode.Agent)
        {
            if (string.IsNullOrWhiteSpace(options.ProjectName))
                throw new InvalidOperationException("VoiceLive:ProjectName is required in Agent mode.");

            query.Add(("agent-name", options.AgentName));
            query.Add(("agent-project-name", options.ProjectName));
            if (!string.IsNullOrWhiteSpace(options.AgentVersion))
                query.Add(("agent-version", options.AgentVersion));
        }
        else
        {
            query.Add(("model", options.Model));
        }

        if (options.AuthMode == VoiceLiveAuthMode.Query && accessToken is not null)
            query.Add(("authorization", $"Bearer {accessToken}"));

        builder.Query = string.Join('&', query.Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value ?? string.Empty)}"));
        return builder.Uri;
    }

    /// <summary>The session.update sent when the call connects: telephony-friendly audio settings, plus instructions and tools in Model mode.</summary>
    public static JsonObject SessionUpdate(VoiceLiveOptions options)
    {
        var session = new JsonObject
        {
            ["modalities"] = new JsonArray("text", "audio"),
            ["input_audio_format"] = "pcm16",
            ["output_audio_format"] = "pcm16",
            ["input_audio_sampling_rate"] = 24000,
            ["voice"] = new JsonObject { ["name"] = options.Voice, ["type"] = "azure-standard" },
            ["turn_detection"] = new JsonObject
            {
                ["type"] = "azure_semantic_vad",
                ["silence_duration_ms"] = 500,
                ["remove_filler_words"] = true,
            },
            ["input_audio_noise_reduction"] = new JsonObject { ["type"] = "azure_deep_noise_suppression" },
            ["input_audio_echo_cancellation"] = new JsonObject { ["type"] = "server_echo_cancellation" },
            ["input_audio_transcription"] = new JsonObject { ["model"] = "azure-speech", ["language"] = options.Language },
        };

        if (options.Mode == VoiceLiveMode.Model)
        {
            session["instructions"] = AgentDefinition.Instructions;
            session["tools"] = new JsonArray(AgentDefinition.Tools.Select(t => (JsonNode)t.ToFunctionTool()).ToArray());
            session["tool_choice"] = "auto";
        }

        return new JsonObject { ["type"] = "session.update", ["session"] = session };
    }

    public static JsonObject AppendAudio(string base64Pcm) =>
        new() { ["type"] = "input_audio_buffer.append", ["audio"] = base64Pcm };

    public static JsonObject CreateResponse() => new() { ["type"] = "response.create" };

    public static JsonObject FunctionCallOutput(string callId, string output) => new()
    {
        ["type"] = "conversation.item.create",
        ["item"] = new JsonObject
        {
            ["type"] = "function_call_output",
            ["call_id"] = callId,
            ["output"] = output,
        },
    };

    public static VoiceLiveEvent Parse(string json)
    {
        JsonObject? message;
        try
        {
            message = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return new VoiceLiveEvent.Error("Received a message that was not valid JSON.");
        }

        var type = Str(message, "type") ?? string.Empty;

        return type switch
        {
            "session.created" => new VoiceLiveEvent.SessionCreated(),
            "session.updated" => new VoiceLiveEvent.SessionReady(),
            "response.created" => new VoiceLiveEvent.ResponseCreated(),
            "response.audio.delta" or "response.output_audio.delta" => new VoiceLiveEvent.AudioDelta(Str(message, "delta") ?? string.Empty),
            "input_audio_buffer.speech_started" => new VoiceLiveEvent.CallerStartedSpeaking(),
            "input_audio_buffer.speech_stopped" => new VoiceLiveEvent.CallerStoppedSpeaking(),
            "conversation.item.input_audio_transcription.completed" => new VoiceLiveEvent.CallerTranscript(Str(message, "transcript") ?? string.Empty),
            "response.audio_transcript.done" or "response.output_audio_transcript.done" => new VoiceLiveEvent.AgentTranscript(Str(message, "transcript") ?? string.Empty),
            "response.function_call_arguments.done" => new VoiceLiveEvent.FunctionCall(
                Str(message, "call_id") ?? string.Empty,
                Str(message, "name") ?? string.Empty,
                Str(message, "arguments") ?? "{}"),
            "response.done" => new VoiceLiveEvent.ResponseDone(),
            "error" => new VoiceLiveEvent.Error(Str(message?["error"] as JsonObject, "message") ?? "Unknown Voice Live error"),
            _ => new VoiceLiveEvent.Other(type),
        };
    }

    private static string? Str(JsonObject? obj, string name) =>
        obj?[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
