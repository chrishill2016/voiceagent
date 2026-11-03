using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using VoiceAgent.Shared;

namespace VoiceAgent.Portal.Services;

public enum LiveConnectionState
{
    Connecting,
    Connected,
    Reconnecting,
    Disconnected,
}

/// <summary>Live view of calls in progress, kept up to date over SignalR.</summary>
public interface ILiveCalls
{
    LiveConnectionState State { get; }
    DateTimeOffset? LastUpdate { get; }
    IReadOnlyList<CallSummary> ActiveCalls { get; }
    IReadOnlyList<CallSummary> Queue { get; }

    event Action? Changed;
    event Action<CallSummary>? CallUpdated;
    event Action<TranscriptUpdate>? TranscriptAdded;
    event Action<CallEventUpdate>? CallEventAdded;

    Task StartAsync();
}

public sealed class LiveCalls(NavigationManager navigation, ICallsApi api) : ILiveCalls, IAsyncDisposable
{
    private readonly Dictionary<string, CallSummary> _calls = [];
    private readonly Lock _gate = new();
    private HubConnection? _hub;

    public LiveConnectionState State { get; private set; } = LiveConnectionState.Connecting;
    public DateTimeOffset? LastUpdate { get; private set; }

    public IReadOnlyList<CallSummary> ActiveCalls
    {
        get { lock (_gate) return _calls.Values.OrderBy(c => c.StartedAt).ToList(); }
    }

    public IReadOnlyList<CallSummary> Queue => ActiveCalls
        .Where(c => c.Stage == CallStage.QueuedForAgent)
        .OrderBy(c => c.QueuedAt)
        .ToList();

    public event Action? Changed;
    public event Action<CallSummary>? CallUpdated;
    public event Action<TranscriptUpdate>? TranscriptAdded;
    public event Action<CallEventUpdate>? CallEventAdded;

    public async Task StartAsync()
    {
        if (_hub is not null) return;

        _hub = new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri(CallsHub.Path.TrimStart('/')))
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        _hub.On<CallSummary>(CallsHub.CallUpdated, summary =>
        {
            Apply(summary);
            CallUpdated?.Invoke(summary);
            Touch();
        });
        _hub.On<TranscriptUpdate>(CallsHub.TranscriptAdded, update =>
        {
            TranscriptAdded?.Invoke(update);
            Touch();
        });
        _hub.On<CallEventUpdate>(CallsHub.CallEventAdded, update =>
        {
            CallEventAdded?.Invoke(update);
            Touch();
        });

        _hub.Reconnecting += _ => SetState(LiveConnectionState.Reconnecting);
        _hub.Reconnected += async _ =>
        {
            await LoadActiveAsync();
            await SetState(LiveConnectionState.Connected);
        };
        _hub.Closed += _ => SetState(LiveConnectionState.Disconnected);

        await ConnectWithRetryAsync();
    }

    private async Task ConnectWithRetryAsync()
    {
        while (true)
        {
            try
            {
                await _hub!.StartAsync();
                await LoadActiveAsync();
                await SetState(LiveConnectionState.Connected);
                return;
            }
            catch
            {
                await SetState(LiveConnectionState.Disconnected);
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }

    private async Task LoadActiveAsync()
    {
        var active = await api.GetActiveAsync();
        lock (_gate)
        {
            _calls.Clear();
            foreach (var call in active) _calls[call.Id] = call;
        }
        Touch();
    }

    private void Apply(CallSummary summary)
    {
        lock (_gate)
        {
            if (summary.Stage == CallStage.Ended) _calls.Remove(summary.Id);
            else _calls[summary.Id] = summary;
        }
    }

    private void Touch()
    {
        LastUpdate = DateTimeOffset.Now;
        Changed?.Invoke();
    }

    private Task SetState(LiveConnectionState state)
    {
        State = state;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
    }

    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext context) =>
            TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(context.PreviousRetryCount, 5))));
    }
}
