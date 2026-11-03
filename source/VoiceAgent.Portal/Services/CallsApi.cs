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
    Task<TakeCallResult> TakeCallAsync(string id, string agentName, CancellationToken ct = default);
    Task<IReadOnlyList<DemoCustomer>> GetDemoCustomersAsync(CancellationToken ct = default);
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

    public async Task<TakeCallResult> TakeCallAsync(string id, string agentName, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"api/calls/{Uri.EscapeDataString(id)}/take", new TakeCallRequest(agentName), Json, ct);
        return await response.Content.ReadFromJsonAsync<TakeCallResult>(Json, ct)
            ?? new TakeCallResult(false, $"Unexpected response ({(int)response.StatusCode}).");
    }

    public async Task<IReadOnlyList<DemoCustomer>> GetDemoCustomersAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("api/demo/customers", ct);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<List<DemoCustomer>>(Json, ct) ?? []
            : [];
    }
}
