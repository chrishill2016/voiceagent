using System.Text.Json.Nodes;
using Azure.Communication.CallAutomation;
using Microsoft.Extensions.Options;
using VoiceAgent.Api.Features.Calls;
using VoiceAgent.Core.Customers;
using VoiceAgent.Shared;

namespace VoiceAgent.Api.Features.Telephony;

/// <summary>Webhooks for Azure Communication Services: incoming calls (via Event Grid) and Call Automation callbacks.</summary>
public static class TelephonyEndpoints
{
    public static IEndpointRouteBuilder MapTelephony(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/acs/incoming-call", HandleIncomingCallAsync);
        app.MapPost("/api/acs/callbacks/{callId}", HandleCallbackAsync);
        return app;
    }

    private static async Task<IResult> HandleIncomingCallAsync(
        HttpContext context,
        IOptions<AcsOptions> options,
        CallTracker tracker,
        ICustomerRepository customers,
        IServiceProvider services,
        ILogger<CallTracker> logger)
    {
        var acs = options.Value;
        if (!CallbackToken.IsValid(context, acs)) return Results.Unauthorized();

        var body = await JsonNode.ParseAsync(context.Request.Body);
        var events = body as JsonArray ?? [body];

        foreach (var evt in events.OfType<JsonObject>())
        {
            var eventType = evt["eventType"]?.GetValue<string>();
            var data = evt["data"] as JsonObject;

            if (eventType == "Microsoft.EventGrid.SubscriptionValidationEvent")
                return Results.Ok(new { validationResponse = data?["validationCode"]?.GetValue<string>() });

            if (eventType != "Microsoft.Communication.IncomingCall" || data is null) continue;

            var client = services.GetService<CallAutomationClient>();
            if (client is null || string.IsNullOrWhiteSpace(acs.PublicBaseUrl))
            {
                logger.LogError("Incoming call received but Acs:Endpoint or Acs:PublicBaseUrl is not configured");
                return Results.Problem("Telephony is not configured.", statusCode: 503);
            }

            var incomingCallContext = data["incomingCallContext"]?.GetValue<string>();
            if (string.IsNullOrEmpty(incomingCallContext)) continue;

            var from = data["from"] as JsonObject;
            var callerNumber = from?["phoneNumber"]?["value"]?.GetValue<string>()
                ?? from?["rawId"]?.GetValue<string>()
                ?? "Unknown caller";

            var callId = Guid.NewGuid().ToString("n");
            var baseUrl = acs.PublicBaseUrl.TrimEnd('/');
            var callbackUri = new Uri(CallbackToken.Append($"{baseUrl}/api/acs/callbacks/{callId}", acs));
            var mediaUri = new Uri(CallbackToken.Append($"{baseUrl.Replace("https://", "wss://")}/ws/media/{callId}", acs));

            var session = await tracker.StartAsync(callId, callerNumber, isTestCall: false);
            session.CallerRawId = from?["rawId"]?.GetValue<string>();

            // Caller ID is only a hint for the human agent; the caller must still pass security.
            if (await customers.FindByPhoneNumberAsync(callerNumber) is { } match)
                await tracker.AddEventAsync(session, $"Caller ID matches account {match.MaskedClientNumber} (not verified)");

            var answer = new AnswerCallOptions(incomingCallContext, callbackUri)
            {
                MediaStreamingOptions = new MediaStreamingOptions(MediaStreamingAudioChannel.Mixed, StreamingTransport.Websocket)
                {
                    TransportUri = mediaUri,
                    MediaStreamingContent = MediaStreamingContent.Audio,
                    StartMediaStreaming = true,
                    EnableBidirectional = true,
                    AudioFormat = AudioFormat.Pcm24KMono,
                },
                OperationContext = callId,
            };

            try
            {
                var result = await client.AnswerCallAsync(answer);
                session.CallConnectionId = result.Value.CallConnectionProperties.CallConnectionId;
                await tracker.AddEventAsync(session, "Call answered");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to answer call from {Caller}", callerNumber);
                await tracker.EndAsync(callId, "Failed to answer the call");
            }
        }

        return Results.Ok();
    }

    private static async Task<IResult> HandleCallbackAsync(
        string callId,
        HttpContext context,
        IOptions<AcsOptions> options,
        CallTracker tracker,
        ILogger<CallTracker> logger)
    {
        if (!CallbackToken.IsValid(context, options.Value)) return Results.Unauthorized();

        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();

        foreach (var evt in CallAutomationEventParser.ParseMany(BinaryData.FromString(body)))
        {
            var session = tracker.Get(callId);
            logger.LogInformation("Call {CallId}: {Event}", callId, evt.GetType().Name);
            if (session is null) continue;

            switch (evt)
            {
                case CallConnected:
                    await tracker.AddEventAsync(session, "Call connected");
                    break;

                case MediaStreamingFailed failed:
                    await tracker.AddEventAsync(session, $"Media streaming failed: {failed.ResultInformation?.Message}");
                    break;

                case CallTransferAccepted:
                    await tracker.AddEventAsync(session, "Transfer to agent accepted");
                    await tracker.UpdateAsync(session, s => s with { Stage = CallStage.WithAgent });
                    break;

                case CallTransferFailed failed:
                    await tracker.AddEventAsync(session, $"Transfer failed: {failed.ResultInformation?.Message}");
                    await tracker.UpdateAsync(session, s => s with { Stage = CallStage.QueuedForAgent, AssignedAgent = null });
                    break;

                case CallDisconnected:
                    await tracker.EndAsync(callId, session.Summary.Stage == CallStage.WithAgent
                        ? "Call handed over to agent"
                        : "Caller hung up");
                    break;
            }
        }

        return Results.Ok();
    }
}
