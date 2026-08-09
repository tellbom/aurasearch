using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Application.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DualNewsSearch.Api.Health;

public sealed class RuntimeReadinessHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISearchModeState _modeState;

    public RuntimeReadinessHealthCheck(
        IServiceScopeFactory scopeFactory,
        ISearchModeState modeState)
    {
        _scopeFactory = scopeFactory;
        _modeState = modeState;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IReadOnlyDictionary<string, IEngineDiagnostics> diagnostics = scope.ServiceProvider
            .GetServices<IEngineDiagnostics>()
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        string[] requiredEngines = _modeState.Current switch
        {
            SearchMode.EsOnly or SearchMode.Shadow => new[] { "elasticsearch" },
            SearchMode.VespaOnly => new[] { "vespa" },
            _ => new[] { "elasticsearch", "vespa" }
        };

        EngineHealth[] engines = await Task.WhenAll(requiredEngines.Select(async name =>
        {
            if (!diagnostics.TryGetValue(name, out IEngineDiagnostics? diagnostic))
            {
                return new EngineHealth(
                    name,
                    false,
                    null,
                    "Diagnostic adapter not registered.",
                    DateTimeOffset.UtcNow);
            }

            return await diagnostic.CheckAsync(cancellationToken);
        }));
        EngineHealth[] unavailable = engines.Where(x => !x.Reachable).ToArray();
        if (unavailable.Length == 0)
        {
            return HealthCheckResult.Healthy(
                $"Required search engines reachable for mode {_modeState.Current}.");
        }

        return HealthCheckResult.Unhealthy(string.Join(
            "; ",
            unavailable.Select(x => $"{x.Name}: {x.Error ?? "unreachable"}")));
    }
}
