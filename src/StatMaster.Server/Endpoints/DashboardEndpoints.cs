using Microsoft.AspNetCore.Http;
using StatMaster.Server.Contracts;
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

        group.MapPost("/metrics/custom-schedule", async (
            HttpRequest request,
            DashboardReadService service,
            CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);
            string returnUrl = form["returnUrl"].ToString();
            if (string.IsNullOrWhiteSpace(returnUrl) ||
                !returnUrl.StartsWith("/", StringComparison.Ordinal) ||
                returnUrl.StartsWith("//", StringComparison.Ordinal))
            {
                returnUrl = "/";
            }

            var current = await service.GetCustomMetricSettingsAsync(ct);
            var updates = new List<CustomMetricSettingDto>(current.Count);
            foreach (var item in current)
            {
                string enabledRaw = form[$"{item.Key}.enabled"].ToString();
                string intervalRaw = form[$"{item.Key}.interval"].ToString();
                int interval = int.TryParse(intervalRaw, out var parsed) ? parsed : item.IntervalSeconds;
                bool enabled = enabledRaw
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(v, "on", StringComparison.OrdinalIgnoreCase));
                updates.Add(new CustomMetricSettingDto
                {
                    Key = item.Key,
                    Description = item.Description,
                    Enabled = enabled,
                    IntervalSeconds = interval
                });
            }

            try
            {
                await service.SaveCustomMetricSettingsAsync(updates, ct);
                string separator = returnUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
                return Results.Redirect($"{returnUrl}{separator}scheduleStatus=saved");
            }
            catch (OperationCanceledException)
            {
                string separator = returnUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
                return Results.Redirect($"{returnUrl}{separator}scheduleStatus=timeout");
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
