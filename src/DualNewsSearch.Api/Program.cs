using System.Diagnostics;
using System.Text.Json.Serialization;
using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Api.Health;
using DualNewsSearch.Infrastructure;
using DualNewsSearch.Infrastructure.Persistence;
using DualNewsSearch.Worker;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddMvcOptions(options => options.ModelMetadataDetailsProviders.Add(
        new SuppressChildValidationMetadataProvider(typeof(BatchDocumentRequest))));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks()
    .AddCheck<RuntimeReadinessHealthCheck>("search-runtime-ready", tags: new[] { "ready" });

AddValidatedOptions<ElasticsearchOptions>(builder, ElasticsearchOptions.SectionName);
AddValidatedOptions<VespaOptions>(builder, VespaOptions.SectionName);
AddValidatedOptions<FusionOptions>(builder, FusionOptions.SectionName);
AddValidatedOptions<IndexingOptions>(builder, IndexingOptions.SectionName);
AddValidatedOptions<TelemetryOptions>(builder, TelemetryOptions.SectionName);
AddValidatedOptions<SearchModeOptions>(builder, SearchModeOptions.SectionName);
AddValidatedOptions<ReadinessOptions>(builder, ReadinessOptions.SectionName);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddIndexWorkers();

WebApplication app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        IExceptionHandlerFeature? feature = context.Features.Get<IExceptionHandlerFeature>();
        ILoggerFactory loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>();
        loggerFactory.CreateLogger("UnhandledException").LogError(
            feature?.Error,
            "Unhandled request error. CorrelationId={CorrelationId}",
            Activity.Current?.Id ?? context.TraceIdentifier);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Unexpected server error",
            extensions: new Dictionary<string, object?>
            {
                ["correlationId"] = Activity.Current?.Id ?? context.TraceIdentifier
            }).ExecuteAsync(context);
    });
});

app.Use(async (context, next) =>
{
    string correlationId = context.Request.Headers.TryGetValue("X-Correlation-ID", out var supplied)
        && !string.IsNullOrWhiteSpace(supplied)
        ? supplied.ToString()
        : context.TraceIdentifier;
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    using IDisposable? scope = app.Logger.BeginScope(
        new Dictionary<string, object> { ["CorrelationId"] = correlationId });
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

using (IServiceScope scope = app.Services.CreateScope())
{
    _ = scope.ServiceProvider.GetRequiredService<IOptions<ElasticsearchOptions>>().Value;
    _ = scope.ServiceProvider.GetRequiredService<IOptions<VespaOptions>>().Value;
    _ = scope.ServiceProvider.GetRequiredService<IOptions<FusionOptions>>().Value;
    _ = scope.ServiceProvider.GetRequiredService<IOptions<IndexingOptions>>().Value;
    _ = scope.ServiceProvider.GetRequiredService<IOptions<TelemetryOptions>>().Value;
    _ = scope.ServiceProvider.GetRequiredService<IOptions<SearchModeOptions>>().Value;
    _ = scope.ServiceProvider.GetRequiredService<IOptions<ReadinessOptions>>().Value;

    IDbContextFactory<SearchDbContext> factory =
        scope.ServiceProvider.GetRequiredService<IDbContextFactory<SearchDbContext>>();
    await using SearchDbContext db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

await app.RunAsync();

static void AddValidatedOptions<TOptions>(
    WebApplicationBuilder builder,
    string sectionName)
    where TOptions : class
{
    builder.Services
        .AddOptions<TOptions>()
        .Bind(builder.Configuration.GetSection(sectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
}

public partial class Program
{
}
