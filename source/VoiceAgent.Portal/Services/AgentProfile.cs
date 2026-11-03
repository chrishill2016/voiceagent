using Microsoft.JSInterop;
using VoiceAgent.Shared;

namespace VoiceAgent.Portal.Services;

/// <summary>
/// Which agent is using this browser, remembered on this device. Placeholder until Entra ID sign-in is added.
/// </summary>
public sealed class AgentProfile(IJSRuntime js, ILiveCalls live)
{
    private const string Key = "voiceagent.agentId";
    private bool _loaded;

    public string? AgentId { get; private set; }

    /// <summary>The signed-in agent's live record, or null if not set up (or not loaded yet).</summary>
    public AgentInfo? Current => AgentId is null ? null : live.Agents.FirstOrDefault(a => a.Id == AgentId);

    public event Action? Changed;

    public async Task<string?> LoadAsync()
    {
        if (_loaded) return AgentId;
        try
        {
            AgentId = await js.InvokeAsync<string?>("localStorage.getItem", Key);
        }
        catch (JSException)
        {
            // Storage blocked (private mode etc.); the agent picks their profile each visit.
        }
        _loaded = true;
        Changed?.Invoke();
        return AgentId;
    }

    public async Task SetAsync(string? agentId)
    {
        AgentId = agentId;
        _loaded = true;
        try
        {
            if (agentId is null) await js.InvokeVoidAsync("localStorage.removeItem", Key);
            else await js.InvokeVoidAsync("localStorage.setItem", Key, agentId);
        }
        catch (JSException)
        {
        }
        Changed?.Invoke();
    }
}
