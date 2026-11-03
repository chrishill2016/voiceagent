using MudBlazor;
using VoiceAgent.Shared;

namespace VoiceAgent.Portal.Services;

public static class CallFormatting
{
    public static string Label(this CallStage stage) => stage switch
    {
        CallStage.Connecting => "Connecting",
        CallStage.IdentifyingCase => "Identifying matter",
        CallStage.Verifying => "Security check",
        CallStage.Verified => "Verified",
        CallStage.QueuedForAgent => "Waiting for agent",
        CallStage.WithAgent => "With agent",
        CallStage.Ended => "Ended",
        _ => stage.ToString(),
    };

    public static Color Colour(this CallStage stage) => stage switch
    {
        CallStage.Connecting => Color.Default,
        CallStage.IdentifyingCase => Color.Info,
        CallStage.Verifying => Color.Warning,
        CallStage.Verified => Color.Success,
        CallStage.QueuedForAgent => Color.Error,
        CallStage.WithAgent => Color.Primary,
        _ => Color.Dark,
    };

    public static string Icon(this CallStage stage) => stage switch
    {
        CallStage.Connecting => Icons.Material.Filled.Phone,
        CallStage.IdentifyingCase => Icons.Material.Filled.ManageSearch,
        CallStage.Verifying => Icons.Material.Filled.Shield,
        CallStage.Verified => Icons.Material.Filled.VerifiedUser,
        CallStage.QueuedForAgent => Icons.Material.Filled.HourglassTop,
        CallStage.WithAgent => Icons.Material.Filled.SupportAgent,
        _ => Icons.Material.Filled.CallEnd,
    };

    public static string Label(this VerificationStatus status) => status switch
    {
        VerificationStatus.NotStarted => "Not started",
        VerificationStatus.InProgress => "In progress",
        VerificationStatus.Passed => "Passed",
        VerificationStatus.Failed => "Failed – needs agent",
        _ => status.ToString(),
    };

    public static Color Colour(this VerificationStatus status) => status switch
    {
        VerificationStatus.Passed => Color.Success,
        VerificationStatus.Failed => Color.Error,
        VerificationStatus.InProgress => Color.Warning,
        _ => Color.Default,
    };

    public static string Label(this AgentStatus status) => status switch
    {
        AgentStatus.Available => "Available",
        AgentStatus.Busy => "Busy",
        AgentStatus.WrapUp => "Wrap-up",
        _ => "Offline",
    };

    public static Color Colour(this AgentStatus status) => status switch
    {
        AgentStatus.Available => Color.Success,
        AgentStatus.Busy => Color.Error,
        AgentStatus.WrapUp => Color.Warning,
        _ => Color.Default,
    };

    public static string SecondsLeft(DateTimeOffset? until) =>
        until is null ? string.Empty : $"{Math.Max(0, (int)Math.Ceiling((until.Value - DateTimeOffset.UtcNow).TotalSeconds))}s";

    public static string Duration(DateTimeOffset start, DateTimeOffset? end = null)
    {
        var span = (end ?? DateTimeOffset.UtcNow) - start;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
    }
}
