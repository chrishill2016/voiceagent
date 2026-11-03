using VoiceAgent.Api.Features.Agents;
using VoiceAgent.Shared;

namespace VoiceAgent.Tests.Api;

public class AgentQueueTests
{
    private readonly TestHarness _h = new();

    private async Task<VoiceAgent.Api.Features.Calls.CallSession> QueuedPhoneCallAsync(string reason = "Wants their case handler")
    {
        _h.CallControl.TransferConfigured = true;
        var session = await _h.StartCallAsync(isTestCall: false);
        session.CallConnectionId = $"conn-{session.Id}";
        _h.Time.Advance(TimeSpan.FromSeconds(1));
        await _h.Queue.EnqueueAsync(session, reason);
        return session;
    }

    [Fact]
    public async Task Queued_calls_are_offered_to_the_longest_idle_available_agent()
    {
        var sarah = await _h.AgentAsync("Sarah", "+447700900201");
        var james = await _h.AgentAsync("James", "+447700900202");
        await _h.AgentAsync("Niamh", "+447700900203", AgentStatus.Busy);

        var call = await QueuedPhoneCallAsync();

        Assert.Equal(sarah.Id, call.Summary.OfferedToAgentId);
        Assert.Equal("Sarah", call.Summary.OfferedToAgentName);
        Assert.Equal(_h.Time.GetUtcNow().AddSeconds(20), call.Summary.OfferExpiresAt);

        var second = await QueuedPhoneCallAsync();
        Assert.Equal(james.Id, second.Summary.OfferedToAgentId);
    }

    [Fact]
    public async Task Calls_wait_unoffered_when_nobody_is_available_and_are_offered_when_someone_becomes_available()
    {
        var sarah = await _h.AgentAsync("Sarah", "+447700900201", AgentStatus.WrapUp);
        var call = await QueuedPhoneCallAsync();
        Assert.Null(call.Summary.OfferedToAgentId);

        await _h.Agents.SetStatusAsync(sarah.Id, AgentStatus.Available);
        await _h.Queue.AgentStatusChangedAsync(sarah.Id, AgentStatus.Available);

        Assert.Equal(sarah.Id, call.Summary.OfferedToAgentId);
    }

    [Fact]
    public async Task Two_agents_taking_two_calls_are_each_transferred_to_their_own_number()
    {
        var sarah = await _h.AgentAsync("Sarah", "+447700900201");
        var james = await _h.AgentAsync("James", "07700 900202");
        var first = await QueuedPhoneCallAsync();
        var second = await QueuedPhoneCallAsync();

        Assert.True((await _h.Queue.TakeAsync(first.Id, sarah.Id)).Success);
        Assert.True((await _h.Queue.TakeAsync(second.Id, james.Id)).Success);

        Assert.Equal(["+447700900201", "+447700900202"], _h.CallControl.TransferTargets);
        Assert.Equal("Sarah", first.Summary.AssignedAgent);
        Assert.Equal(james.Id, second.Summary.AssignedAgentId);
        Assert.Equal(AgentStatus.Busy, _h.Agents.Get(sarah.Id)!.Status);
        Assert.Equal(first.Id, _h.Agents.Get(sarah.Id)!.CurrentCallId);
        Assert.Equal(CallStage.WithAgent, second.Summary.Stage);
        Assert.Null(second.Summary.OfferedToAgentId);
    }

    [Fact]
    public async Task An_unanswered_offer_times_out_moves_to_the_next_agent_and_sets_the_first_offline()
    {
        var sarah = await _h.AgentAsync("Sarah", "+447700900201");
        var james = await _h.AgentAsync("James", "+447700900202");
        var call = await QueuedPhoneCallAsync();
        Assert.Equal(sarah.Id, call.Summary.OfferedToAgentId);

        _h.Time.Advance(TimeSpan.FromSeconds(19));
        await _h.Queue.DispatchAsync();
        Assert.Equal(sarah.Id, call.Summary.OfferedToAgentId);

        _h.Time.Advance(TimeSpan.FromSeconds(2));
        await _h.Queue.DispatchAsync();

        Assert.Equal(james.Id, call.Summary.OfferedToAgentId);
        Assert.Equal(AgentStatus.Offline, _h.Agents.Get(sarah.Id)!.Status);
    }

    [Fact]
    public async Task Declining_passes_the_call_on_and_the_agent_is_not_reoffered_it_straight_away()
    {
        var sarah = await _h.AgentAsync("Sarah", "+447700900201");
        var call = await QueuedPhoneCallAsync();

        await _h.Queue.DeclineAsync(call.Id, sarah.Id);
        Assert.Null(call.Summary.OfferedToAgentId);
        Assert.Equal(AgentStatus.Available, _h.Agents.Get(sarah.Id)!.Status);

        var james = await _h.AgentAsync("James", "+447700900202");
        await _h.Queue.DispatchAsync();
        Assert.Equal(james.Id, call.Summary.OfferedToAgentId);

        await _h.Queue.DeclineAsync(call.Id, james.Id);
        _h.Time.Advance(TimeSpan.FromSeconds(61));
        await _h.Queue.DispatchAsync();
        Assert.NotNull(call.Summary.OfferedToAgentId);
    }

    [Fact]
    public async Task Going_offline_withdraws_an_offer()
    {
        var sarah = await _h.AgentAsync("Sarah", "+447700900201");
        var call = await QueuedPhoneCallAsync();

        await _h.Agents.SetStatusAsync(sarah.Id, AgentStatus.Offline);
        await _h.Queue.AgentStatusChangedAsync(sarah.Id, AgentStatus.Offline);

        Assert.Null(call.Summary.OfferedToAgentId);
    }

    [Fact]
    public async Task Picking_a_call_from_the_queue_requires_being_available()
    {
        var sarah = await _h.AgentAsync("Sarah", "+447700900201", AgentStatus.WrapUp);
        var call = await QueuedPhoneCallAsync();

        var result = await _h.Queue.TakeAsync(call.Id, sarah.Id);

        Assert.False(result.Success);
        Assert.Contains("Available", result.Message);
        Assert.Equal(0, _h.CallControl.Transfers);
        Assert.False(call.AiStopped.IsCancellationRequested);
    }

    [Fact]
    public async Task Taking_a_call_needs_an_agent_profile()
    {
        var call = await QueuedPhoneCallAsync();
        var result = await _h.Queue.TakeAsync(call.Id, "nobody");
        Assert.False(result.Success);
        Assert.Contains("profile", result.Message);
    }

    [Fact]
    public async Task A_failed_transfer_keeps_the_caller_queued_and_offers_the_next_agent()
    {
        var sarah = await _h.AgentAsync("Sarah", "+447700900201");
        var james = await _h.AgentAsync("James", "+447700900202");
        var call = await QueuedPhoneCallAsync();
        _h.CallControl.FailTransfers = true;

        var result = await _h.Queue.TakeAsync(call.Id, sarah.Id);

        Assert.False(result.Success);
        Assert.Equal(CallStage.QueuedForAgent, call.Summary.Stage);
        Assert.Equal(james.Id, call.Summary.OfferedToAgentId);
    }

    [Fact]
    public async Task Transfer_failure_reported_by_acs_puts_the_caller_back_and_frees_the_agent()
    {
        var sarah = await _h.AgentAsync("Sarah", "+447700900201");
        var call = await QueuedPhoneCallAsync();
        await _h.Queue.TakeAsync(call.Id, sarah.Id);

        await _h.Queue.TransferFailedAsync(call, "Call not answered");

        Assert.Equal(CallStage.QueuedForAgent, call.Summary.Stage);
        Assert.Null(call.Summary.AssignedAgentId);
        Assert.Equal(AgentStatus.Available, _h.Agents.Get(sarah.Id)!.Status);
    }

    [Fact]
    public async Task Taking_a_test_call_needs_no_phone_transfer_and_ending_it_moves_the_agent_to_wrap_up()
    {
        var sarah = await _h.AgentAsync("Sarah", "+447700900201");
        var session = await _h.StartCallAsync(isTestCall: true);
        await _h.Queue.EnqueueAsync(session, "Asked for a person");

        var result = await _h.Queue.TakeAsync(session.Id, sarah.Id);

        Assert.True(result.Success);
        Assert.Equal(0, _h.CallControl.Transfers);
        Assert.True(session.AiStopped.IsCancellationRequested);
        Assert.False((await _h.Queue.TakeAsync(session.Id, sarah.Id)).Success);

        await _h.Tracker.EndAsync(session.Id, "Test call ended");
        Assert.Equal(AgentStatus.WrapUp, _h.Agents.Get(sarah.Id)!.Status);
    }

    [Fact]
    public async Task Phone_calls_cannot_be_taken_without_telephony()
    {
        var sarah = await _h.AgentAsync("Sarah", "+447700900201");
        var session = await _h.StartCallAsync(isTestCall: false);
        await _h.Queue.EnqueueAsync(session, "x");

        var result = await _h.Queue.TakeAsync(session.Id, sarah.Id);

        Assert.False(result.Success);
        Assert.Contains("telephony", result.Message);
    }

    [Fact]
    public async Task Taking_an_ended_call_fails_cleanly()
    {
        var sarah = await _h.AgentAsync("Sarah", "+447700900201");
        Assert.False((await _h.Queue.TakeAsync("missing", sarah.Id)).Success);
    }

    [Fact]
    public async Task Queue_is_ordered_by_time_queued()
    {
        var first = await _h.StartCallAsync();
        var second = await _h.StartCallAsync();
        await _h.Queue.EnqueueAsync(second, "b");
        _h.Time.Advance(TimeSpan.FromSeconds(1));
        await _h.Queue.EnqueueAsync(first, "a");

        Assert.Equal([second.Id, first.Id], _h.Queue.Waiting().Select(c => c.Id));
    }
}

public class AgentDirectoryTests
{
    private readonly TestHarness _h = new();

    [Theory]
    [InlineData("+44 7700 900123", "+447700900123")]
    [InlineData("07700 900123", "+447700900123")]
    [InlineData("0044 7700 900123", "+447700900123")]
    [InlineData("8:orgid:6f1c2b7e-1111-2222-3333-444455556666", "8:orgid:6f1c2b7e-1111-2222-3333-444455556666")]
    public void Transfer_targets_accept_phone_numbers_and_teams_ids(string input, string expected)
    {
        Assert.True(TransferTargets.TryNormalise(input, out var target, out _));
        Assert.Equal(expected, target);
    }

    [Theory]
    [InlineData("")]
    [InlineData("call me")]
    [InlineData("+44 12")]
    [InlineData("8:")]
    public void Invalid_transfer_targets_are_rejected_with_a_helpful_message(string input)
    {
        Assert.False(TransferTargets.TryNormalise(input, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task New_agents_start_offline_and_names_must_be_unique()
    {
        var created = await _h.Agents.CreateAsync(new SaveAgentRequest("Sarah Mitchell", "+447700900201"));
        Assert.True(created.Success);
        Assert.Equal(AgentStatus.Offline, created.Agent!.Status);

        var duplicate = await _h.Agents.CreateAsync(new SaveAgentRequest("sarah mitchell", "+447700900299"));
        Assert.False(duplicate.Success);
    }

    [Fact]
    public async Task Status_changes_are_broadcast_and_becoming_available_clears_the_last_call()
    {
        var sarah = await _h.AgentAsync("Sarah", "+447700900201");
        await _h.Agents.AssignCallAsync(sarah.Id, "call-1");
        Assert.Equal("call-1", _h.Agents.Get(sarah.Id)!.CurrentCallId);

        await _h.Agents.SetStatusAsync(sarah.Id, AgentStatus.WrapUp);
        Assert.Equal("call-1", _h.Agents.Get(sarah.Id)!.CurrentCallId);

        await _h.Agents.SetStatusAsync(sarah.Id, AgentStatus.Available);
        Assert.Null(_h.Agents.Get(sarah.Id)!.CurrentCallId);
        Assert.Equal(AgentStatus.Available, _h.Notifier.Agents[^1].Status);
    }

    [Fact]
    public async Task Profiles_survive_a_restart_but_everyone_starts_offline()
    {
        var store = new InMemoryAgentStore();
        var before = new AgentDirectory(store, _h.Notifier, _h.Time, Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentDirectory>.Instance);
        var sarah = (await before.CreateAsync(new SaveAgentRequest("Sarah", "+447700900201"))).Agent!;
        await before.SetStatusAsync(sarah.Id, AgentStatus.Available);

        var after = new AgentDirectory(store, _h.Notifier, _h.Time, Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentDirectory>.Instance);
        await after.LoadAsync();

        Assert.Equal(AgentStatus.Offline, after.Get(sarah.Id)!.Status);
        Assert.Equal("+447700900201", after.Get(sarah.Id)!.TransferTarget);
    }
}
