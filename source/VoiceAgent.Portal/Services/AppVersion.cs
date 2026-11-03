using System.Reflection;

namespace VoiceAgent.Portal.Services;

public static class AppVersion
{
    /// <summary>Version stamped by the pipeline (e.g. 1.0.42), without the source commit suffix.</summary>
    public static string Current { get; } =
        (typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];
}
