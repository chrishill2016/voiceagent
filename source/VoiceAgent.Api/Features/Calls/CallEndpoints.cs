using Microsoft.Extensions.Options;
using VoiceAgent.Core.Cases;
using VoiceAgent.Shared;

namespace VoiceAgent.Api.Features.Calls;

/// <summary>REST API used by the agent portal.</summary>
public static class CallEndpoints
{
    public static IEndpointRouteBuilder MapCallApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/calls/active", (CallTracker tracker) => tracker.ActiveCalls());

        api.MapGet("/calls/recent", (ICallStore store, int? take, CancellationToken ct) =>
            store.ListRecentAsync(Math.Clamp(take ?? 50, 1, 200), ct));

        api.MapGet("/calls/{id}", async (string id, CallTracker tracker, CancellationToken ct) =>
            await tracker.GetDetailAsync(id, ct) is { } detail ? Results.Ok(detail) : Results.NotFound());

        api.MapPost("/calls/{id}/take", async (string id, TakeCallRequest request, AgentQueue queue, CancellationToken ct) =>
        {
            var result = await queue.TakeAsync(id, request.AgentId, ct);
            return result.Success ? Results.Ok(result) : Results.Conflict(result);
        });

        api.MapPost("/calls/{id}/decline", async (string id, DeclineCallRequest request, AgentQueue queue) =>
            Results.Ok(await queue.DeclineAsync(id, request.AgentId)));

        api.MapGet("/demo/cases", async (ICaseRepository cases, IOptions<DemoOptions> demo, CancellationToken ct) =>
        {
            if (!demo.Value.ShowSampleData) return Results.NotFound();

            var list = await cases.ListAsync(ct);
            return Results.Ok(list.OrderBy(c => c.CaseNumber).Select(c => new DemoCase(
                c.CaseNumber, c.MatterType, c.Stage, c.PropertyAddress, c.PropertyPostcode, c.Postcode,
                c.Parties.Select(p => new DemoParty(p.FullName, p.DateOfBirth, p.PhoneNumber)).ToList(),
                c.PreviousLender)));
        });

        return app;
    }
}
