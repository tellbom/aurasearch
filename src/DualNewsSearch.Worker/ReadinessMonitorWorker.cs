using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.Worker;

public sealed class ReadinessMonitorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISearchModeState _modeState;
    private readonly ReadinessOptions _options;
    private readonly ILogger<ReadinessMonitorWorker> _logger;

    public ReadinessMonitorWorker(
        IServiceScopeFactory scopeFactory,
        ISearchModeState modeState,
        IOptions<ReadinessOptions> options,
        ILogger<ReadinessMonitorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _modeState = modeState;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ISearchReadinessEvaluator evaluator =
                    scope.ServiceProvider.GetRequiredService<ISearchReadinessEvaluator>();
                SearchReadinessReport report = await evaluator.EvaluateAsync(stoppingToken);
                if (!report.ReadyForVespa
                    && _modeState.Current is SearchMode.Rrf or SearchMode.VespaOnly)
                {
                    string reason = string.Join(
                        "; ",
                        report.Checks.Where(x => !x.Passed).Select(x => x.Name));
                    _modeState.Change(SearchMode.EsOnly, "readiness-monitor", reason, automatic: true);
                    _logger.LogError("Search automatically degraded to EsOnly. Reason={Reason}", reason);
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                if (_modeState.Current is SearchMode.Rrf or SearchMode.VespaOnly)
                {
                    _modeState.Change(
                        SearchMode.EsOnly,
                        "readiness-monitor",
                        "Readiness evaluation failed.",
                        automatic: true);
                }
                _logger.LogError(exception, "Readiness monitoring failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.CheckIntervalSeconds), stoppingToken);
        }
    }
}

