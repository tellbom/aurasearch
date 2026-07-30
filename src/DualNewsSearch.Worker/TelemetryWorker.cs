using System.Threading.Channels;
using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.Worker;

public sealed class SearchTelemetryQueue : ISearchTelemetryQueue
{
    private readonly Channel<SearchTelemetryEnvelope> _channel =
        Channel.CreateBounded<SearchTelemetryEnvelope>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    public bool TryEnqueue(SearchTelemetryEnvelope envelope) => _channel.Writer.TryWrite(envelope);

    public ValueTask<SearchTelemetryEnvelope> DequeueAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}

public sealed class TelemetryWorker : BackgroundService
{
    private readonly ISearchTelemetryQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelemetryWorker> _logger;

    public TelemetryWorker(
        ISearchTelemetryQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<TelemetryWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            SearchTelemetryEnvelope envelope = await _queue.DequeueAsync(stoppingToken);
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ISearchTelemetryRepository repository =
                    scope.ServiceProvider.GetRequiredService<ISearchTelemetryRepository>();
                await repository.SaveSearchAsync(envelope, stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(
                    exception,
                    "Telemetry persistence failed. SearchTraceId={SearchTraceId}",
                    envelope.Execution.Response.SearchTraceId);
            }
        }
    }
}

public sealed class TelemetryCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelemetryOptions _options;

    public TelemetryCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<TelemetryOptions> options)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISearchTelemetryRepository repository =
                scope.ServiceProvider.GetRequiredService<ISearchTelemetryRepository>();
            while (await repository.CleanupExpiredAsync(_options.CleanupBatchSize, stoppingToken) > 0)
            {
                await Task.Yield();
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}

