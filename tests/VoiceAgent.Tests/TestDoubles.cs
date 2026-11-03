using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VoiceAgent.Api;
using VoiceAgent.Api.Features.Agents;
using VoiceAgent.Api.Features.Calls;
using VoiceAgent.Api.Features.Media;
using VoiceAgent.Api.Features.Telephony;
using VoiceAgent.Api.Features.Tools;
using VoiceAgent.Api.Features.VoiceLive;
using VoiceAgent.Core.Cases;
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
    public List<AgentInfo> Agents { get; } = [];
    public Task AgentUpdatedAsync(AgentInfo agent) { lock (Agents) Agents.Add(agent); return Task.CompletedTask; }
}

public sealed class FakeCallControl : ICallControl
{
    public bool TransferConfigured { get; set; }
    public bool FailTransfers { get; set; }
    public List<string> TransferTargets { get; } = [];
    public int Transfers => TransferTargets.Count;
    public int HoldMusicStarts { get; private set; }

    public bool CanTransfer(CallSession session) => TransferConfigured && session.CallConnectionId is not null;
    public Task StartHoldMusicAsync(CallSession session, CancellationToken ct = default) { HoldMusicStarts++; return Task.CompletedTask; }
    public Task TransferToAgentAsync(CallSession session, string transferTarget, CancellationToken ct = default)
    {
        if (FailTransfers) throw new InvalidOperationException("Target busy");
        TransferTargets.Add(transferTarget);
        return Task.CompletedTask;
    }
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
    public ManualTime Time { get; } = new();
    public AgentDirectory Agents { get; }
    public AgentQueue Queue { get; }
    public AgentToolHandler Tools { get; }

    public TestHarness()
    {
        Tracker = new CallTracker(Store, Notifier, Time, NullLogger<CallTracker>.Instance);
        Agents = new AgentDirectory(new InMemoryAgentStore(), Notifier, Time, NullLogger<AgentDirectory>.Instance);
        Queue = new AgentQueue(Tracker, Agents, CallControl, Time,
            Options.Create(new QueueOptions { OfferTimeoutSeconds = 20, DeclineCooldownSeconds = 60 }));
        Tools = new AgentToolHandler(new InMemoryCaseRepository(SampleData.LoadCases()), Tracker, Queue, NullLogger<AgentToolHandler>.Instance);
    }

    public CallAudioBridge Bridge(VoiceLiveOptions? options = null) => new(
        VoiceLive, Tools, Tracker, Queue, CallControl,
        Options.Create(options ?? new VoiceLiveOptions { GreetOnConnect = true }),
        NullLogger<CallAudioBridge>.Instance);

    public Task<CallSession> StartCallAsync(bool isTestCall = true) =>
        Tracker.StartAsync(Guid.NewGuid().ToString("n"), "+447700900101", isTestCall);

    /// <summary>Creates an agent and sets their status (Available by default).</summary>
    public async Task<AgentInfo> AgentAsync(string name, string target, AgentStatus status = AgentStatus.Available)
    {
        var created = await Agents.CreateAsync(new SaveAgentRequest(name, target));
        Assert.True(created.Success, created.Message);
        Time.Advance(TimeSpan.FromSeconds(1));
        await Agents.SetStatusAsync(created.Agent!.Id, status);
        return Agents.Get(created.Agent.Id)!;
    }

    /// <summary>Amelia and Tom Hughes's purchase, case 1042/2617.</summary>
    public static ConveyancingCase HughesPurchase => SampleData.LoadCases().Single(c => c.CaseNumber == "1042/2617");
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

/// <summary>A clock the tests move forward by hand, so offer timeouts are deterministic.</summary>
public sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}
