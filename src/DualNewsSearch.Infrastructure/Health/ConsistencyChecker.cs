using DualNewsSearch.Application.Abstractions;
using DualNewsSearch.Application.Contracts;

namespace DualNewsSearch.Infrastructure.Health;

public sealed class ConsistencyChecker : IConsistencyChecker
{
    private static readonly string[] SourceTypes = { "news", "announcement", "portal" };
    private readonly IOutboxRepository _repository;
    private readonly IReadOnlyList<IEngineConsistencyProbe> _probes;
    private readonly IClock _clock;

    public ConsistencyChecker(
        IOutboxRepository repository,
        IEnumerable<IEngineConsistencyProbe> probes,
        IClock clock)
    {
        _repository = repository;
        _probes = probes.ToArray();
        _clock = clock;
    }

    public async Task<ConsistencyReport> CheckAsync(
        int hashSampleSize,
        CancellationToken cancellationToken)
    {
        IndexingSnapshot snapshot = await _repository.GetSnapshotAsync(cancellationToken);
        IReadOnlyList<DesiredHashSample> samples =
            await _repository.GetHashSamplesAsync(hashSampleSize, cancellationToken);
        var engines = new List<EngineConsistencyResult>(_probes.Count);
        foreach (IEngineConsistencyProbe probe in _probes)
        {
            try
            {
                long count = await probe.CountAsync(null, cancellationToken);
                var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (string sourceType in SourceTypes)
                {
                    counts[sourceType] = await probe.CountAsync(sourceType, cancellationToken);
                }

                var mismatches = new List<string>();
                foreach (DesiredHashSample sample in samples)
                {
                    (string ContentHash, long IndexVersion)? actual =
                        await probe.GetVersionHashAsync(sample.NewsId, cancellationToken);
                    if (actual is null
                        || actual.Value.ContentHash != sample.ContentHash
                        || actual.Value.IndexVersion != sample.IndexVersion)
                    {
                        mismatches.Add(sample.NewsId);
                    }
                }

                engines.Add(new EngineConsistencyResult(
                    probe.Name,
                    count,
                    counts,
                    samples.Count,
                    mismatches,
                    null));
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                engines.Add(new EngineConsistencyResult(
                    probe.Name,
                    0,
                    new Dictionary<string, long>(),
                    0,
                    Array.Empty<string>(),
                    exception.Message));
            }
        }

        return new ConsistencyReport(
            _clock.UtcNow,
            snapshot.DesiredUpserts,
            snapshot.DesiredBySourceType,
            engines);
    }
}

