using Microsoft.JSInterop;

namespace VoiceAgent.Portal.Services;

/// <summary>
/// The portal user's display name, remembered in this browser. Placeholder until Entra ID sign-in is added.
/// </summary>
public sealed class AgentProfile(IJSRuntime js)
{
    private const string Key = "voiceagent.agentName";
    private bool _loaded;

    public string Name { get; private set; } = string.Empty;

    public async Task<string> GetNameAsync()
    {
        if (_loaded) return Name;
        try
        {
            Name = await js.InvokeAsync<string?>("localStorage.getItem", Key) ?? string.Empty;
        }
        catch (JSException)
        {
            // Storage blocked (private mode etc.); fall back to asking each time.
        }
        _loaded = true;
        return Name;
    }

    public async Task SetNameAsync(string name)
    {
        Name = name.Trim();
        _loaded = true;
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", Key, Name);
        }
        catch (JSException)
        {
        }
    }
}
