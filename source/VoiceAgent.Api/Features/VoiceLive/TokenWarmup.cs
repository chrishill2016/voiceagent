using Azure.Core;
using Microsoft.Extensions.Options;

namespace VoiceAgent.Api.Features.VoiceLive;

/// <summary>
/// Fetches the managed identity token for Voice Live at start-up, so the first call after a deploy
/// or restart doesn't wait for it. The credential caches and refreshes the token from then on.
/// </summary>
public sealed class TokenWarmup(TokenCredential credential, IOptions<VoiceLiveOptions> options, ILogger<TokenWarmup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.Endpoint)) return;

        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await credential.GetTokenAsync(new TokenRequestContext([options.Value.TokenScope]), stoppingToken);
            logger.LogInformation("Voice Live token ready in {Ms} ms", clock.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not get a Voice Live token at start-up; the first call will try again");
        }
    }
}
