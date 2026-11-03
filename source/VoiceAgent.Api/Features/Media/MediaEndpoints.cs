using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using VoiceAgent.Api.Features.Calls;
using VoiceAgent.Api.Features.Telephony;

namespace VoiceAgent.Api.Features.Media;

public static class MediaEndpoints
{
    public static IEndpointRouteBuilder MapMedia(this IEndpointRouteBuilder app)
    {
        // Bidirectional audio for a real phone call, opened by ACS after the call is answered.
        app.Map("/ws/media/{callId}", async (string callId, HttpContext context, IOptions<AcsOptions> options, CallTracker tracker, CallAudioBridge bridge) =>
        {
            if (!context.WebSockets.IsWebSocketRequest) return Results.BadRequest();
            if (!CallbackToken.IsValid(context, options.Value)) return Results.Unauthorized();

            var session = tracker.Get(callId);
            if (session is null) return Results.NotFound();

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await bridge.RunAsync(session, new WebSocketCallerChannel(socket), context.RequestAborted);
            await tracker.EndAsync(callId, "Call audio stream closed");
            return Results.Empty;
        });

        // Browser test call from the portal: same audio format as ACS, no phone number needed.
        app.Map("/ws/test-call", async (HttpContext context, CallTracker tracker, CallAudioBridge bridge) =>
        {
            if (!context.WebSockets.IsWebSocketRequest) return Results.BadRequest();

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var channel = new WebSocketCallerChannel(socket);
            var callId = $"test-{Guid.NewGuid():n}"[..17];
            var session = await tracker.StartAsync(callId, "Browser test call", isTestCall: true);

            await channel.SendAsync(new JsonObject { ["kind"] = "TestCallStarted", ["callId"] = callId }.ToJsonString(), context.RequestAborted);
            await bridge.RunAsync(session, channel, context.RequestAborted);
            await tracker.EndAsync(callId, "Test call ended");
            return Results.Empty;
        });

        return app;
    }
}
