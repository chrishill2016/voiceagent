using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VoiceAgent.Api;
using VoiceAgent.Api.Features.Calls;
using VoiceAgent.Api.Features.Media;
using VoiceAgent.Api.Features.Telephony;
using VoiceAgent.Api.Features.Tools;
using VoiceAgent.Api.Features.VoiceLive;
using VoiceAgent.Core.Customers;
using VoiceAgent.Shared;

namespace VoiceAgent.Tests;

public sealed class RecordingNotifier : ICallNotifier
{
    public List<CallSummary> Summaries { get; } = [];
    public List<TranscriptUpdate> Transcripts { get; } = [];
    public List<CallEventUpdate> Events { get; } = [];

    public Task CallUpdatedAsync(CallSummary summary) { lock (Summaries) Summaries.Add(summary); return Task.CompletedTask; }
    public Task TranscriptAddedAsync(TranscriptUpdate update) { lock (Transcripts) Transcripts.Add(update); return Task.CompletedTask; }
    public Task CallEventAddedAsync(CallEventUpdate update) { lock (Events) Events.Add(update); return Task.CompletedTask; }
}

public sealed class FakeCallControl : ICallControl
{
    public bool TransferConfigured { get; set; }
    public int Transfers { get; private set; }
    public int HoldMusicStarts { get; private set; }

    public bool CanTransfer(CallSession session) => TransferConfigured && session.CallConnectionId is not null;
    public Task StartHoldMusicAsync(CallSession session, CancellationToken ct = default) { HoldMusicStarts++; return Task.CompletedTask; }
    public Task TransferToAgentAsync(CallSession session, CancellationToken ct = default) { Transfers++; return Task.CompletedTask; }
}

/// <summary>A fake Voice Live service: the test pushes events, and inspects what the bridge sent.</summary>
public sealed class FakeVoiceLive : IVoiceLiveConnectionFactory, IVoiceLiveConnection
{
    private readonly Channel<VoiceLiveEvent> _events = Channel.CreateUnbounded<VoiceLiveEvent>();
    public List<JsonObject> Sent { get; } = [];
    public bool FailToConnect { get; set; }
    public bool Disposed { get; private set; }

    public void Push(VoiceLiveEvent evt) => _events.Writer.TryWrite(evt);

    public IEnumerable<string> SentTypes { get { lock (Sent) return Sent.Select(s => s["type"]!.GetValue<string>()).ToList(); } }

    public Task<IVoiceLiveConnection> ConnectAsync(CancellationToken ct) =>
        FailToConnect ? throw new InvalidOperationException("No Voice Live") : Task.FromResult<IVoiceLiveConnection>(this);

    public Task SendAsync(JsonObject message, CancellationToken ct)
    {
        lock (Sent) Sent.Add(message);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<VoiceLiveEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (await _events.Reader.WaitToReadAsync(ct))
            while (_events.Reader.TryRead(out var evt))
                yield return evt;
    }

    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}

/// <summary>Fake caller line: the test pushes ACS media messages and reads what was played back.</summary>
public sealed class FakeCaller : ICallerAudioChannel
{
    private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>();
    public List<string> Received { get; } = [];

    public void Say(string base64Audio) =>
        _inbound.Writer.TryWrite($$$"""{"kind":"AudioData","audioData":{"data":"{{{base64Audio}}}","silent":false}}""");

    public void HangUp() => _inbound.Writer.TryComplete();

    public async IAsyncEnumerable<string> ReceiveAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var m in _inbound.Reader.ReadAllAsync(ct))
            yield return m;
    }

    public Task SendAsync(string message, CancellationToken ct)
    {
        lock (Received) Received.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>Wires up the real services with in-memory storage and fakes for Azure.</summary>
public sealed class TestHarness
{
    public RecordingNotifier Notifier { get; } = new();
    public InMemoryCallStore Store { get; } = new();
    public FakeCallControl CallControl { get; } = new();
    public FakeVoiceLive VoiceLive { get; } = new();
    public CallTracker Tracker { get; }
    public AgentQueue Queue { get; }
    public AgentToolHandler Tools { get; }

    public TestHarness()
    {
        Tracker = new CallTracker(Store, Notifier, TimeProvider.System, NullLogger<CallTracker>.Instance);
        Queue = new AgentQueue(Tracker, CallControl, TimeProvider.System);
        Tools = new AgentToolHandler(new InMemoryCustomerRepository(SampleData.LoadCustomers()), Tracker, Queue, NullLogger<AgentToolHandler>.Instance);
    }

    public CallAudioBridge Bridge(VoiceLiveOptions? options = null) => new(
        VoiceLive, Tools, Tracker, Queue, CallControl,
        Options.Create(options ?? new VoiceLiveOptions { GreetOnConnect = true }),
        NullLogger<CallAudioBridge>.Instance);

    public Task<CallSession> StartCallAsync(bool isTestCall = true) =>
        Tracker.StartAsync(Guid.NewGuid().ToString("n"), "+447700900101", isTestCall);

    public static Customer Amelia => SampleData.LoadCustomers().Single(c => c.Id == "cust-001");
}

public static class Eventually
{
    public static async Task TrueAsync(Func<bool> condition, string because)
    {
        for (var i = 0; i < 200; i++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
        Assert.Fail($"Timed out waiting until {because}");
    }
}
