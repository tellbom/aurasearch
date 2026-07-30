using Microsoft.Extensions.DependencyInjection;
using DualNewsSearch.Application.Contracts;

namespace DualNewsSearch.Worker;

public static class DependencyInjection
{
    public static IServiceCollection AddIndexWorkers(this IServiceCollection services)
    {
        services.AddSingleton<ISearchTelemetryQueue, SearchTelemetryQueue>();
        services.AddHostedService<IndexOutboxWorker>();
        services.AddHostedService<TelemetryWorker>();
        services.AddHostedService<TelemetryCleanupWorker>();
        services.AddHostedService<ReadinessMonitorWorker>();
        return services;
    }
}
