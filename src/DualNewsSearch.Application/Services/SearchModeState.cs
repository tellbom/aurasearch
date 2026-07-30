using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Contracts;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.Application.Services;

public sealed record ModeAuditEntry(
    DateTimeOffset ChangedAt,
    SearchMode Previous,
    SearchMode Current,
    string Operator,
    string Reason,
    bool Automatic);

public interface ISearchModeState
{
    SearchMode Current { get; }

    IReadOnlyList<ModeAuditEntry> Audit { get; }

    void Change(SearchMode mode, string operatorName, string reason, bool automatic);
}

public sealed class SearchModeState : ISearchModeState
{
    private readonly object _sync = new();
    private readonly List<ModeAuditEntry> _audit = new();
    private SearchMode _current;

    public SearchModeState(IOptions<SearchModeOptions> options)
    {
        _current = options.Value.Default;
    }

    public SearchMode Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public IReadOnlyList<ModeAuditEntry> Audit
    {
        get
        {
            lock (_sync)
            {
                return _audit.ToArray();
            }
        }
    }

    public void Change(SearchMode mode, string operatorName, string reason, bool automatic)
    {
        if (string.IsNullOrWhiteSpace(operatorName) || string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Operator and reason are required.");
        }

        lock (_sync)
        {
            if (_current == mode)
            {
                return;
            }

            SearchMode previous = _current;
            _current = mode;
            _audit.Add(new ModeAuditEntry(
                DateTimeOffset.UtcNow,
                previous,
                mode,
                operatorName.Trim(),
                reason.Trim(),
                automatic));
            if (_audit.Count > 1_000)
            {
                _audit.RemoveRange(0, _audit.Count - 1_000);
            }
        }
    }
}

public sealed record ReadinessCheck(
    string Name,
    bool Passed,
    string Detail);

public sealed record SearchReadinessReport(
    bool ReadyForVespa,
    DateTimeOffset CheckedAt,
    IReadOnlyList<ReadinessCheck> Checks,
    IndexingSnapshot Indexing,
    IReadOnlyList<EngineHealth> Engines,
    ConsistencyReport Consistency);

public interface ISearchReadinessEvaluator
{
    Task<SearchReadinessReport> EvaluateAsync(CancellationToken cancellationToken);
}
