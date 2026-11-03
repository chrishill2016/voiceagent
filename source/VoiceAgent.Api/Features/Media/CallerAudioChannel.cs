using System.Net.WebSockets;
using System.Text;
using VoiceAgent.Api.Features.VoiceLive;

namespace VoiceAgent.Api.Features.Media;

/// <summary>The caller's side of the audio: ACS media streaming, or the browser test call.</summary>
public interface ICallerAudioChannel
{
    IAsyncEnumerable<string> ReceiveAsync(CancellationToken ct);
    Task SendAsync(string message, CancellationToken ct);
}

public sealed class WebSocketCallerChannel(WebSocket socket) : ICallerAudioChannel
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public IAsyncEnumerable<string> ReceiveAsync(CancellationToken ct) => WebSocketText.ReadMessagesAsync(socket, ct);

    public async Task SendAsync(string message, CancellationToken ct)
    {
        if (socket.State != WebSocketState.Open) return;

        await _sendLock.WaitAsync(ct);
        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        catch (WebSocketException)
        {
            // Caller has gone; the receive loop will notice and end the call.
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
