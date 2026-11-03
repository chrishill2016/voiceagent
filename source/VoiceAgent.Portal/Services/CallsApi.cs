using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceAgent.Shared;

namespace VoiceAgent.Portal.Services;

public interface ICallsApi
{
    Task<IReadOnlyList<CallSummary>> GetActiveAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CallSummary>> GetRecentAsync(int take = 50, CancellationToken ct = default);
    Task<CallDetail?> GetCallAsync(string id, CancellationToken ct = default);
    Task<TakeCallResult> TakeCallAsync(string id, string agentId, CancellationToken ct = default);
    Task<TakeCallResult> DeclineCallAsync(string id, string agentId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentInfo>> GetAgentsAsync(CancellationToken ct = default);
    Task<AgentResult> CreateAgentAsync(SaveAgentRequest request, CancellationToken ct = default);
    Task<AgentResult> UpdateAgentAsync(string id, SaveAgentRequest request, CancellationToken ct = default);
    Task<AgentResult> SetAgentStatusAsync(string id, AgentStatus status, CancellationToken ct = default);
    Task<IReadOnlyList<DemoCase>> GetDemoCasesAsync(CancellationToken ct = default);
}

public sealed class CallsApi(HttpClient http) : ICallsApi
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<IReadOnlyList<CallSummary>> GetActiveAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<CallSummary>>("api/calls/active", Json, ct) ?? [];

    public async Task<IReadOnlyList<CallSummary>> GetRecentAsync(int take = 50, CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<CallSummary>>($"api/calls/recent?take={take}", Json, ct) ?? [];

    public async Task<CallDetail?> GetCallAsync(string id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/calls/{Uri.EscapeDataString(id)}", ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<CallDetail>(Json, ct) : null;
    }

    public Task<TakeCallResult> TakeCallAsync(string id, string agentId, CancellationToken ct = default) =>
        PostAsync<TakeCallResult>($"api/calls/{Uri.EscapeDataString(id)}/take", new TakeCallRequest(agentId), ct,
            status => new TakeCallResult(false, $"Unexpected response ({status})."));

    public Task<TakeCallResult> DeclineCallAsync(string id, string agentId, CancellationToken ct = default) =>
        PostAsync<TakeCallResult>($"api/calls/{Uri.EscapeDataString(id)}/decline", new DeclineCallRequest(agentId), ct,
            status => new TakeCallResult(false, $"Unexpected response ({status})."));

    public async Task<IReadOnlyList<AgentInfo>> GetAgentsAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<AgentInfo>>("api/agents", Json, ct) ?? [];

    public Task<AgentResult> CreateAgentAsync(SaveAgentRequest request, CancellationToken ct = default) =>
        PostAsync<AgentResult>("api/agents", request, ct, status => new AgentResult(false, $"Unexpected response ({status})."));

    public async Task<AgentResult> UpdateAgentAsync(string id, SaveAgentRequest request, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"api/agents/{Uri.EscapeDataString(id)}", request, Json, ct);
        return await ReadAsync(response, ct, status => new AgentResult(false, $"Unexpected response ({status})."));
    }

    public Task<AgentResult> SetAgentStatusAsync(string id, AgentStatus status, CancellationToken ct = default) =>
        PostAsync<AgentResult>($"api/agents/{Uri.EscapeDataString(id)}/status", new SetAgentStatusRequest(status), ct,
            code => new AgentResult(false, $"Unexpected response ({code})."));

    private async Task<T> PostAsync<T>(string url, object body, CancellationToken ct, Func<int, T> fallback)
    {
        var response = await http.PostAsJsonAsync(url, body, Json, ct);
        return await ReadAsync(response, ct, fallback);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct, Func<int, T> fallback)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? fallback((int)response.StatusCode);
        }
        catch (JsonException)
        {
            return fallback((int)response.StatusCode);
        }
    }

    public async Task<IReadOnlyList<DemoCase>> GetDemoCasesAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("api/demo/cases", ct);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<List<DemoCase>>(Json, ct) ?? []
            : [];
    }
}
