using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.Worker;

public sealed class IndexOutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IndexingOptions _options;
    private readonly ILogger<IndexOutboxWorker> _logger;

    public IndexOutboxWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<IndexingOptions> options,
        ILogger<IndexOutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool worked = await ProcessOneAsync(stoppingToken);
            if (!worked)
            {
                await Task.Delay(_options.WorkerPollIntervalMs, stoppingToken);
            }
        }
    }

    internal async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IOutboxRepository repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        OutboxWorkItem? item = await repository.ClaimNextAsync(
            TimeSpan.FromMinutes(2),
            cancellationToken);
        if (item is null)
        {
            return false;
        }

        IReadOnlyList<IIndexSink> sinks = scope.ServiceProvider
            .GetServices<IIndexSink>()
            .Where(IsEnabled)
            .ToArray();
        var completions = new List<EngineApplyCompletion>(sinks.Count);
        foreach (IIndexSink sink in sinks)
        {
            try
            {
                IndexApplyResult result = await sink.ApplyAsync(item.Write, cancellationToken);
                completions.Add(new EngineApplyCompletion(
                    sink.Name,
                    item.Write.Document.IndexVersion,
                    result));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Index sink failed. Engine={Engine} NewsId={NewsId} IndexVersion={IndexVersion}",
                    sink.Name,
                    item.Write.Document.NewsId,
                    item.Write.Document.IndexVersion);
                completions.Add(new EngineApplyCompletion(
                    sink.Name,
                    item.Write.Document.IndexVersion,
                    new IndexApplyResult(IndexApplyStatus.TransientFailure, exception.Message)));
            }
        }

        await repository.CompleteAsync(
            item,
            completions,
            _options.ElasticsearchSinkEnabled,
            _options.VespaSinkEnabled,
            _options.MaxRetryCount,
            cancellationToken);
        return true;
    }

    private bool IsEnabled(IIndexSink sink)
    {
        return sink.Name.Equals("elasticsearch", StringComparison.OrdinalIgnoreCase)
            ? _options.ElasticsearchSinkEnabled
            : _options.VespaSinkEnabled;
    }
}

