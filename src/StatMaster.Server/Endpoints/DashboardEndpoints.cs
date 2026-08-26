using Microsoft.AspNetCore.Http;
using StatMaster.Server.Queries;

namespace StatMaster.Server.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .RequireAuthorization("DashboardAccess");

        group.MapGet("/agents", async (DashboardReadService service, CancellationToken ct) =>
        {
            try
            {
                var data = await service.GetAgentsAsync(ct);
                return Results.Ok(data);
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(StatusCodes.Status408RequestTimeout);
            }
        });

        group.MapGet("/metrics/latest", async (
            string agentId,
            DashboardReadService service,
            CancellationToken ct) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(agentId))
                    return Results.BadRequest("agentId is required.");

                if (!await service.AgentExistsAsync(agentId, ct))
                    return Results.NotFound($"Agent '{agentId}' not found.");

                var data = await service.GetLatestMetricsAsync(agentId, ct);
                return Results.Ok(data);
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(StatusCodes.Status408RequestTimeout);
            }
        });

        group.MapGet("/metrics/history", async (
            string agentId,
            string key,
            int? minutes,
            int? limit,
            DashboardReadService service,
            CancellationToken ct) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(agentId))
                    return Results.BadRequest("agentId is required.");

                if (string.IsNullOrWhiteSpace(key))
                    return Results.BadRequest("key is required.");

                if (!await service.AgentExistsAsync(agentId, ct))
                    return Results.NotFound($"Agent '{agentId}' not found.");

                var data = await service.GetMetricHistoryAsync(
                    agentId,
                    key,
                    minutes ?? 60,
                    limit ?? 1000,
                    ct);

                return Results.Ok(data);
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(StatusCodes.Status408RequestTimeout);
            }
        });
    }
}
