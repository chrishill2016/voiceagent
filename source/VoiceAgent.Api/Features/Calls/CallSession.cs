using VoiceAgent.Core.Cases;
using VoiceAgent.Shared;

namespace VoiceAgent.Api.Features.Calls;

/// <summary>
/// Live, server-side state of a single call. Holds data the portal and the voice model must not see
/// (e.g. the unverified case record) alongside the public <see cref="CallSummary"/>.
/// </summary>
public sealed class CallSession(CallSummary summary)
{
    private readonly Lock _gate = new();
    private readonly List<TranscriptEntry> _transcript = [];
    private readonly List<CallEvent> _events = [];
    private readonly CancellationTokenSource _aiStop = new();
    private readonly Dictionary<string, DateTimeOffset> _declinedBy = [];
    private CallSummary _summary = summary;

    public string Id => _summary.Id;

    /// <summary>ACS call connection id; null for browser test calls.</summary>
    public string? CallConnectionId { get; set; }

    /// <summary>Raw ACS identifier of the caller, needed for hold / transfer.</summary>
    public string? CallerRawId { get; set; }

    /// <summary>The case the caller has been matched to (by case number, or postcode and surname). Not yet verified.</summary>
    public ConveyancingCase? ClaimedCase { get; private set; }

    /// <summary>Set only once the caller has passed security for <see cref="ClaimedCase"/>.</summary>
    public ConveyancingCase? VerifiedCase { get; private set; }

    public int FailedVerificationAttempts { get; private set; }

    /// <summary>True once the voice agent should stop after it finishes its current reply (caller is being handed to a person).</summary>
    public bool HandOffPending { get; private set; }

    /// <summary>Cancelled when the AI must stop talking to the caller immediately.</summary>
    public CancellationToken AiStopped => _aiStop.Token;

    public CallSummary Summary
    {
        get { lock (_gate) return _summary; }
    }

    public CallDetail Snapshot()
    {
        lock (_gate) return new CallDetail(_summary, [.. _transcript], [.. _events]);
    }

    internal CallSummary Apply(Func<CallSummary, CallSummary> change, DateTimeOffset now)
    {
        lock (_gate) return _summary = change(_summary) with { LastModified = now };
    }

    internal void AddTranscript(TranscriptEntry entry)
    {
        lock (_gate) _transcript.Add(entry);
    }

    internal void AddEvent(CallEvent evt)
    {
        lock (_gate) _events.Add(evt);
    }

    public void SetClaimedCase(ConveyancingCase account)
    {
        lock (_gate)
        {
            if (ClaimedCase?.Id != account.Id)
            {
                // A different case means security starts again.
                VerifiedCase = null;
                FailedVerificationAttempts = 0;
            }

            ClaimedCase = account;
        }
    }

    public void MarkVerified()
    {
        lock (_gate) VerifiedCase = ClaimedCase;
    }

    public void RecordFailedVerification()
    {
        lock (_gate) FailedVerificationAttempts++;
    }

    public void RequestHandOff()
    {
        lock (_gate) HandOffPending = true;
    }

    /// <summary>Remembers that an agent declined or missed this call, so it moves on to someone else.</summary>
    public void RecordDecline(string agentId, DateTimeOffset at)
    {
        lock (_gate) _declinedBy[agentId] = at;
    }

    public bool RecentlyDeclinedBy(string agentId, DateTimeOffset now, TimeSpan cooldown)
    {
        lock (_gate) return _declinedBy.TryGetValue(agentId, out var at) && now - at < cooldown;
    }

    public void StopAi()
    {
        if (!_aiStop.IsCancellationRequested) _aiStop.Cancel();
    }
}
