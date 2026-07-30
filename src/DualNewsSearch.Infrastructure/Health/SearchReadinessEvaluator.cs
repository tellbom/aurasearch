using DualNewsSearch.Application.Abstractions;
using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Application.Services;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.Infrastructure.Health;

public sealed class SearchReadinessEvaluator : ISearchReadinessEvaluator
{
    private readonly IOutboxRepository _outbox;
    private readonly IReadOnlyList<IEngineDiagnostics> _diagnostics;
    private readonly ReadinessOptions _options;
    private readonly IClock _clock;
    private readonly IConsistencyChecker _consistency;

    public SearchReadinessEvaluator(
        IOutboxRepository outbox,
        IEnumerable<IEngineDiagnostics> diagnostics,
        IOptions<ReadinessOptions> options,
        IClock clock,
        IConsistencyChecker consistency)
    {
        _outbox = outbox;
        _diagnostics = diagnostics.ToArray();
        _options = options.Value;
        _clock = clock;
        _consistency = consistency;
    }

    public async Task<SearchReadinessReport> EvaluateAsync(CancellationToken cancellationToken)
    {
        IndexingSnapshot snapshot = await _outbox.GetSnapshotAsync(cancellationToken);
        EngineHealth[] engines = await Task.WhenAll(
            _diagnostics.Select(x => x.CheckAsync(cancellationToken)));
        ConsistencyReport consistency = await _consistency.CheckAsync(
            _options.HashSampleSize,
            cancellationToken);
        double esHour = Rate(snapshot.LastHourElasticsearchApplied, snapshot.LastHourDesired);
        double vespaHour = Rate(snapshot.LastHourVespaApplied, snapshot.LastHourDesired);
        double esDay = Rate(snapshot.Last24HoursElasticsearchApplied, snapshot.Last24HoursDesired);
        double vespaDay = Rate(snapshot.Last24HoursVespaApplied, snapshot.Last24HoursDesired);
        double lagMinutes = snapshot.OldestOutboxAt.HasValue
            ? Math.Max(0, (_clock.UtcNow - snapshot.OldestOutboxAt.Value).TotalMinutes)
            : 0;

        var checks = new List<ReadinessCheck>
        {
            new("backfillComplete", _options.BackfillComplete,
                _options.BackfillComplete ? "Configured complete." : "Not confirmed by Operator."),
            new("vespaReachable", engines.Any(x => x.Name == "vespa" && x.Reachable),
                EngineDetail(engines, "vespa")),
            new("elasticsearchReachable", engines.Any(x => x.Name == "elasticsearch" && x.Reachable),
                EngineDetail(engines, "elasticsearch")),
            new("oneHourSyncRate",
                esHour >= _options.MinimumOneHourSyncRate
                    && vespaHour >= _options.MinimumOneHourSyncRate,
                $"es={esHour:P2}; vespa={vespaHour:P2}; minimum={_options.MinimumOneHourSyncRate:P2}"),
            new("twentyFourHourSyncRate",
                esDay >= _options.Minimum24HourSyncRate
                    && vespaDay >= _options.Minimum24HourSyncRate,
                $"es={esDay:P2}; vespa={vespaDay:P2}; minimum={_options.Minimum24HourSyncRate:P2}"),
            new("outboxBacklog",
                snapshot.OutboxBacklog <= _options.MaximumOutboxBacklog,
                $"actual={snapshot.OutboxBacklog}; maximum={_options.MaximumOutboxBacklog}"),
            new("maximumLag",
                lagMinutes <= _options.MaximumLagMinutes,
                $"actualMinutes={lagMinutes:F2}; maximum={_options.MaximumLagMinutes}"),
            new("localCountParity",
                snapshot.DesiredUpserts == snapshot.ElasticsearchApplied
                    && snapshot.DesiredUpserts == snapshot.VespaApplied,
                $"desired={snapshot.DesiredUpserts}; es={snapshot.ElasticsearchApplied}; vespa={snapshot.VespaApplied}"),
            new("engineCountAndHashParity",
                consistency.Passed,
                consistency.Passed
                    ? $"Counts and {consistency.Engines.Min(x => x.HashSamplesChecked)} hash samples match."
                    : string.Join("; ", consistency.Engines.Select(x =>
                        $"{x.Engine}: count={x.Count}, mismatches={x.MismatchedNewsIds.Count}, error={x.Error ?? "none"}")))
        };

        return new SearchReadinessReport(
            checks.All(x => x.Passed),
            _clock.UtcNow,
            checks,
            snapshot,
            engines,
            consistency);
    }

    private static double Rate(long applied, long desired) => desired == 0 ? 1 : applied / (double)desired;

    private static string EngineDetail(IEnumerable<EngineHealth> engines, string name)
    {
        EngineHealth? health = engines.SingleOrDefault(x => x.Name == name);
        return health is null
            ? "Diagnostic adapter not registered."
            : health.Error ?? $"Reachable; version={health.Version ?? "not-reported"}.";
    }
}
