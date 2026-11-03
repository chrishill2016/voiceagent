using Microsoft.Extensions.Options;
using VoiceAgent.Core.Customers;
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
            var result = await queue.TakeAsync(id, request.AgentName, ct);
            return result.Success ? Results.Ok(result) : Results.Conflict(result);
        });

        api.MapGet("/demo/customers", async (ICustomerRepository customers, IOptions<DemoOptions> demo, CancellationToken ct) =>
        {
            if (!demo.Value.ShowSampleData) return Results.NotFound();

            var list = await customers.ListAsync(ct);
            return Results.Ok(list.Select(c => new DemoCustomer(
                c.ClientNumber, c.FullName, c.PhoneNumber, c.DateOfBirth, c.Postcode, c.MemorableWord,
                c.Cases.Select(x => $"{x.Reference} – {x.MatterType}, {x.Stage}").ToList())));
        });

        return app;
    }
}
