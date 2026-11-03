using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace VoiceAgent.Api.Features.VoiceLive;

public interface IVoiceLiveConnection : IAsyncDisposable
{
    Task SendAsync(JsonObject message, CancellationToken ct);
    IAsyncEnumerable<VoiceLiveEvent> ReadEventsAsync(CancellationToken ct);
}

public interface IVoiceLiveConnectionFactory
{
    Task<IVoiceLiveConnection> ConnectAsync(CancellationToken ct);
}

public sealed class VoiceLiveConnectionFactory(
    IOptions<VoiceLiveOptions> options,
    TokenCredential credential,
    ILogger<VoiceLiveConnection> logger) : IVoiceLiveConnectionFactory
{
    public async Task<IVoiceLiveConnection> ConnectAsync(CancellationToken ct)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.Endpoint))
            throw new InvalidOperationException("VoiceLive:Endpoint is not configured.");

        var token = await credential.GetTokenAsync(new TokenRequestContext([opts.TokenScope]), ct);

        var socket = new ClientWebSocket();
        if (opts.AuthMode == VoiceLiveAuthMode.Header)
            socket.Options.SetRequestHeader("Authorization", $"Bearer {token.Token}");
        socket.Options.SetRequestHeader("x-ms-client-request-id", Guid.NewGuid().ToString());

        var uri = VoiceLiveProtocol.BuildUri(opts, token.Token);
        await socket.ConnectAsync(uri, ct);
        logger.LogInformation("Connected to Voice Live ({Mode} mode)", opts.Mode);
        return new VoiceLiveConnection(socket, logger);
    }
}

public sealed class VoiceLiveConnection(ClientWebSocket socket, ILogger logger) : IVoiceLiveConnection
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public async Task SendAsync(JsonObject message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _sendLock.WaitAsync(ct);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async IAsyncEnumerable<VoiceLiveEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var message in WebSocketText.ReadMessagesAsync(socket, ct))
            yield return VoiceLiveProtocol.Parse(message);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Call finished", timeout.Token);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error closing Voice Live socket");
        }
        finally
        {
            socket.Dispose();
            _sendLock.Dispose();
        }
    }
}

/// <summary>Reads whole text messages from a WebSocket.</summary>
public static class WebSocketText
{
    public static async IAsyncEnumerable<string> ReadMessagesAsync(WebSocket socket, [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, ct);
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
            {
                yield break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                yield break;

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            if (result.MessageType == WebSocketMessageType.Text)
                yield return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);

            message.SetLength(0);
        }
    }
}
