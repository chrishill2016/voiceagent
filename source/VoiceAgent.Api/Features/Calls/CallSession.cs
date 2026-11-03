using VoiceAgent.Core.Customers;
using VoiceAgent.Shared;

namespace VoiceAgent.Api.Features.Calls;

/// <summary>
/// Live, server-side state of a single call. Holds data the portal and the voice model must not see
/// (e.g. the unverified customer record) alongside the public <see cref="CallSummary"/>.
/// </summary>
public sealed class CallSession(CallSummary summary)
{
    private readonly Lock _gate = new();
    private readonly List<TranscriptEntry> _transcript = [];
    private readonly List<CallEvent> _events = [];
    private readonly CancellationTokenSource _aiStop = new();
    private CallSummary _summary = summary;

    public string Id => _summary.Id;

    /// <summary>ACS call connection id; null for browser test calls.</summary>
    public string? CallConnectionId { get; set; }

    /// <summary>Raw ACS identifier of the caller, needed for hold / transfer.</summary>
    public string? CallerRawId { get; set; }

    /// <summary>Customer the caller claims to be (found by account number / case reference). Not yet verified.</summary>
    public Customer? ClaimedCustomer { get; private set; }

    /// <summary>Set only once the caller has passed security for <see cref="ClaimedCustomer"/>.</summary>
    public Customer? VerifiedCustomer { get; private set; }

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

    public void SetClaimedCustomer(Customer customer)
    {
        lock (_gate)
        {
            if (ClaimedCustomer?.Id != customer.Id)
            {
                // A different account means security starts again.
                VerifiedCustomer = null;
                FailedVerificationAttempts = 0;
            }

            ClaimedCustomer = customer;
        }
    }

    public void MarkVerified()
    {
        lock (_gate) VerifiedCustomer = ClaimedCustomer;
    }

    public void RecordFailedVerification()
    {
        lock (_gate) FailedVerificationAttempts++;
    }

    public void RequestHandOff()
    {
        lock (_gate) HandOffPending = true;
    }

    public void StopAi()
    {
        if (!_aiStop.IsCancellationRequested) _aiStop.Cancel();
    }
}
