using Microsoft.EntityFrameworkCore;
using StatMaster.Server.Contracts;

namespace StatMaster.Server.Queries;

public sealed class DashboardReadService
{
    private readonly IDbContextFactory<StatMasterDbContext> _dbFactory;

    public DashboardReadService(IDbContextFactory<StatMasterDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<AgentSummaryDto>> GetAgentsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var targets = await db.AgentTargets
            .AsNoTracking()
            .ToListAsync(ct);

        var latestByAgent = await db.MetricSamples
            .AsNoTracking()
            .GroupBy(x => x.AgentId)
            .Select(g => new { AgentId = g.Key, LastId = g.Max(x => x.Id) })
            .ToListAsync(ct);

        var latestIds = latestByAgent.Select(x => x.LastId).ToList();
        var latestSamples = latestIds.Count == 0
            ? new List<MetricSampleModel>()
            : await db.MetricSamples
                .AsNoTracking()
                .Where(x => latestIds.Contains(x.Id))
                .ToListAsync(ct);

        var lastSeenMap = latestSamples.ToDictionary(x => x.AgentId, x => x.CapturedAtUtc);
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(2));
        var onlineThreshold = now.AddMinutes(-2);

        var targetMap = targets.ToDictionary(t => t.AgentId, StringComparer.OrdinalIgnoreCase);
        var observedAgentIds = latestByAgent.Select(x => x.AgentId);
        var allAgentIds = targetMap.Keys
            .Concat(observedAgentIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var result = new List<AgentSummaryDto>(allAgentIds.Count);

        foreach (var agentId in allAgentIds)
        {
            targetMap.TryGetValue(agentId, out var target);
            lastSeenMap.TryGetValue(agentId, out var lastSeen);

            result.Add(new AgentSummaryDto
            {
                AgentId = agentId,
                DisplayName = target?.DisplayName,
                HostOrIp = target?.HostOrIp,
                Enabled = target?.Enabled ?? true,
                LastSeen = lastSeen,
                Online = lastSeen >= onlineThreshold
            });
        }

        return result;
    }

    public async Task<AgentSummaryDto?> GetAgentByIdAsync(
        string agentId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return null;

        var agents = await GetAgentsAsync(ct);
        return agents.FirstOrDefault(
            x => string.Equals(x.AgentId, agentId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> AgentExistsAsync(string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return false;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        bool existsInTargets = await db.AgentTargets
            .AsNoTracking()
            .AnyAsync(x => x.AgentId == agentId, ct);
        if (existsInTargets)
            return true;

        return await db.MetricSamples
            .AsNoTracking()
            .AnyAsync(x => x.AgentId == agentId, ct);
    }

    public async Task<IReadOnlyList<LatestMetricDto>> GetLatestMetricsAsync(
        string agentId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var rows = await db.MetricSamples
            .AsNoTracking()
            .Where(x => x.AgentId == agentId)
            .OrderByDescending(x => x.Id)
            .Take(5000)
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.MetricKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.MetricKey)
            .Select(x => new LatestMetricDto
            {
                AgentId = x.AgentId,
                MetricKey = x.MetricKey,
                RawResponse = x.RawResponse,
                ValueText = x.ValueText,
                ErrorText = x.ErrorText,
                IsError = x.IsError,
                CapturedAt = x.CapturedAtUtc
            })
            .ToList();
    }

    public async Task<IReadOnlyList<MetricPointDto>> GetMetricHistoryAsync(
        string agentId,
        string key,
        int minutes = 60,
        int limit = 1000,
        CancellationToken ct = default)
    {
        minutes = Math.Clamp(minutes, 1, 30 * 24 * 60);
        limit = Math.Clamp(limit, 1, 5000);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var from = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(2)).AddMinutes(-minutes);

        // SQLite + DateTimeOffset in WHERE/ORDER BY can behave inconsistently.
        // We load a bounded recent set and apply precise time filtering in memory.
        var rows = await db.MetricSamples
            .AsNoTracking()
            .Where(x => x.AgentId == agentId)
            .Where(x => x.MetricKey == key)
            .OrderByDescending(x => x.Id)
            .Take(5000)
            .ToListAsync(ct);

        return rows
            .Where(x => x.CapturedAtUtc >= from)
            .Take(limit)
            .OrderBy(x => x.Id)
            .Select(x => new MetricPointDto
            {
                CapturedAt = x.CapturedAtUtc,
                ValueText = x.ValueText,
                IsError = x.IsError,
                RawResponse = x.RawResponse
            })
            .ToList();
    }

    public async Task<bool> DeleteAgentDataAsync(string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return false;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        int deletedSamples = await db.MetricSamples
            .Where(x => x.AgentId == agentId)
            .ExecuteDeleteAsync(ct);

        int deletedTargets = await db.AgentTargets
            .Where(x => x.AgentId == agentId)
            .ExecuteDeleteAsync(ct);

        return deletedSamples > 0 || deletedTargets > 0;
    }
}
