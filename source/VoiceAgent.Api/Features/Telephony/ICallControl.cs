using Azure.Communication;
using Azure.Communication.CallAutomation;
using Microsoft.Extensions.Options;
using VoiceAgent.Api.Features.Calls;

namespace VoiceAgent.Api.Features.Telephony;

/// <summary>Controls the phone leg of a call. Browser test calls have no phone leg, so these become no-ops.</summary>
public interface ICallControl
{
    bool CanTransfer(CallSession session);
    Task StartHoldMusicAsync(CallSession session, CancellationToken ct = default);
    Task TransferToAgentAsync(CallSession session, CancellationToken ct = default);
}

public sealed class AcsCallControl(
    IOptions<AcsOptions> options,
    ILogger<AcsCallControl> logger,
    CallAutomationClient? client = null) : ICallControl
{
    private readonly AcsOptions _options = options.Value;

    public bool CanTransfer(CallSession session) =>
        client is not null && session.CallConnectionId is not null && !string.IsNullOrWhiteSpace(_options.AgentTransferTarget);

    public async Task StartHoldMusicAsync(CallSession session, CancellationToken ct = default)
    {
        if (client is null || session.CallConnectionId is null || string.IsNullOrWhiteSpace(_options.HoldMusicUrl))
            return;

        try
        {
            var media = client.GetCallConnection(session.CallConnectionId).GetCallMedia();
            var play = new PlayToAllOptions([new FileSource(new Uri(_options.HoldMusicUrl))]) { Loop = true };
            await media.PlayToAllAsync(play, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not start hold music for call {CallId}", session.Id);
        }
    }

    public async Task TransferToAgentAsync(CallSession session, CancellationToken ct = default)
    {
        if (!CanTransfer(session))
            throw new InvalidOperationException("Call transfer is not configured. Set Acs__AgentTransferTarget.");

        var connection = client!.GetCallConnection(session.CallConnectionId!);
        try
        {
            await connection.GetCallMedia().CancelAllMediaOperationsAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No media operations to cancel for call {CallId}", session.Id);
        }

        var target = CommunicationIdentifier.FromRawId(ToRawId(_options.AgentTransferTarget!));
        await connection.TransferCallToParticipantAsync(target, ct);
    }

    /// <summary>Accepts a plain E.164 phone number as well as raw ACS / Teams identifiers.</summary>
    public static string ToRawId(string target) =>
        target.StartsWith('+') ? $"4:{target}" : target;
}
