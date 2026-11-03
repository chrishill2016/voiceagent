using VoiceAgent.Api.Features.Calls;
using VoiceAgent.Shared;

namespace VoiceAgent.Api.Features.Agents;

public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/agents");

        api.MapGet("", (AgentDirectory agents) => agents.List());

        api.MapGet("/{id}", (string id, AgentDirectory agents) =>
            agents.Get(id) is { } agent ? Results.Ok(agent) : Results.NotFound());

        api.MapPost("", async (SaveAgentRequest request, AgentDirectory agents) =>
            ToResult(await agents.CreateAsync(request)));

        api.MapPut("/{id}", async (string id, SaveAgentRequest request, AgentDirectory agents) =>
            ToResult(await agents.UpdateAsync(id, request)));

        api.MapPost("/{id}/status", async (string id, SetAgentStatusRequest request, AgentDirectory agents, AgentQueue queue) =>
        {
            var result = await agents.SetStatusAsync(id, request.Status);
            if (result.Success)
                await queue.AgentStatusChangedAsync(id, request.Status);
            return ToResult(result);
        });

        return app;
    }

    private static IResult ToResult(AgentResult result) =>
        result.Success ? Results.Ok(result) : Results.BadRequest(result);
}
