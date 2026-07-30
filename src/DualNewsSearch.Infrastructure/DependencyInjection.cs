using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Abstractions;
using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Application.Services;
using DualNewsSearch.Infrastructure.Content;
using DualNewsSearch.Infrastructure.Persistence;
using DualNewsSearch.Infrastructure.Search;
using DualNewsSearch.Infrastructure.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace DualNewsSearch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string sqlitePath = configuration[$"{IndexingOptions.SectionName}:SqlitePath"]
            ?? throw new InvalidOperationException("Indexing:SqlitePath is required.");

        string? directory = Path.GetDirectoryName(Path.GetFullPath(sqlitePath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        services.AddDbContextFactory<SearchDbContext>(options =>
            options.UseSqlite($"Data Source={sqlitePath};Cache=Shared"));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IHtmlTextCleaner, HtmlAgilityTextCleaner>();
        services.AddScoped<IDesiredDocumentStore, DesiredDocumentStore>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<ISearchTelemetryRepository, SearchTelemetryRepository>();
        services.AddScoped<IndexDocumentService>();
        services.AddScoped<SearchOrchestrator>();
        services.AddSingleton<ISearchModeState, SearchModeState>();
        services.AddScoped<ISearchReadinessEvaluator, SearchReadinessEvaluator>();
        services.AddScoped<IConsistencyChecker, ConsistencyChecker>();

        services.AddHttpClient<ElasticsearchAdapter>((provider, client) =>
            {
                ElasticsearchOptions options =
                    provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ElasticsearchOptions>>().Value;
                client.BaseAddress = EnsureTrailingSlash(options.Endpoint);
                client.Timeout = TimeSpan.FromMilliseconds(options.TimeoutMs);
                client.DefaultRequestVersion = new Version(1, 1);
            })
            .AddPolicyHandler(CreateRetryPolicy())
            .AddPolicyHandler(CreateCircuitBreakerPolicy());
        services.AddHttpClient<VespaAdapter>((provider, client) =>
            {
                VespaOptions options =
                    provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<VespaOptions>>().Value;
                client.BaseAddress = EnsureTrailingSlash(options.Endpoint);
                client.Timeout = TimeSpan.FromMilliseconds(options.TimeoutMs);
                client.DefaultRequestVersion = new Version(1, 1);
            })
            .AddPolicyHandler(CreateRetryPolicy())
            .AddPolicyHandler(CreateCircuitBreakerPolicy());
        services.AddScoped<ISearchEngineAdapter>(provider =>
            provider.GetRequiredService<ElasticsearchAdapter>());
        services.AddScoped<ISearchEngineAdapter>(provider =>
            provider.GetRequiredService<VespaAdapter>());
        services.AddScoped<ISuggestAdapter>(provider =>
            provider.GetRequiredService<ElasticsearchAdapter>());
        services.AddScoped<IIndexSink>(provider =>
            provider.GetRequiredService<ElasticsearchAdapter>());
        services.AddScoped<IIndexSink>(provider =>
            provider.GetRequiredService<VespaAdapter>());
        services.AddScoped<IEngineDiagnostics>(provider =>
            provider.GetRequiredService<ElasticsearchAdapter>());
        services.AddScoped<IEngineDiagnostics>(provider =>
            provider.GetRequiredService<VespaAdapter>());
        services.AddScoped<IEngineConsistencyProbe>(provider =>
            provider.GetRequiredService<ElasticsearchAdapter>());
        services.AddScoped<IEngineConsistencyProbe>(provider =>
            provider.GetRequiredService<VespaAdapter>());
        services.AddScoped<IQueryDiagnosticsRenderer>(provider =>
            provider.GetRequiredService<ElasticsearchAdapter>());
        services.AddScoped<IQueryDiagnosticsRenderer>(provider =>
            provider.GetRequiredService<VespaAdapter>());

        return services;
    }

    private static Uri EnsureTrailingSlash(string endpoint)
    {
        return new Uri(endpoint.TrimEnd('/') + "/", UriKind.Absolute);
    }

    private static IAsyncPolicy<HttpResponseMessage> CreateRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(2, attempt => TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)));
    }

    private static IAsyncPolicy<HttpResponseMessage> CreateCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
    }
}
