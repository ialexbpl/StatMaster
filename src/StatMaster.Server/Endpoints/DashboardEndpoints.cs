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

        group.MapPost("/agents/{agentId}/delete", async (
            string agentId,
            HttpRequest request,
            DashboardReadService service,
            CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);
            string confirmText = form["confirmText"].ToString();
            string returnUrl = NormalizeLocalReturnUrl(form["returnUrl"].ToString(), agentId);

            if (!string.Equals(confirmText?.Trim(), "DELETE", StringComparison.OrdinalIgnoreCase))
                return Results.Redirect($"{returnUrl}?deleteStatus=confirm");

            try
            {
                bool deleted = await service.DeleteAgentDataAsync(agentId, ct);
                if (!deleted)
                    return Results.Redirect($"{returnUrl}?deleteStatus=notfound");

                return Results.Redirect("/?deleteStatus=deleted");
            }
            catch (OperationCanceledException)
            {
                return Results.Redirect($"{returnUrl}?deleteStatus=timeout");
            }
        });
    }

    private static string NormalizeLocalReturnUrl(string? returnUrl, string agentId)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) &&
            returnUrl.StartsWith("/", StringComparison.Ordinal) &&
            !returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return returnUrl;
        }

        return $"/agent/{Uri.EscapeDataString(agentId)}";
    }
}
