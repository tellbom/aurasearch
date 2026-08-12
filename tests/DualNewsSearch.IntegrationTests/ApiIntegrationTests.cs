using System.Net;
using System.Net.Http.Json;
using DualNewsSearch.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DualNewsSearch.IntegrationTests;

public sealed class ApiIntegrationTests : IAsyncLifetime
{
    private string _databaseName = string.Empty;
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _databaseName = $"dual-news-search-{Guid.NewGuid():N}";
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:SearchDatabase", "test-only");
            builder.UseSetting("Indexing:ElasticsearchSinkEnabled", "false");
            builder.UseSetting("Indexing:VespaSinkEnabled", "false");
            builder.UseSetting("Readiness:CheckIntervalSeconds", "3600");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SearchDatabase"] = "test-only",
                    ["Indexing:ElasticsearchSinkEnabled"] = "false",
                    ["Indexing:VespaSinkEnabled"] = "false",
                    ["Readiness:CheckIntervalSeconds"] = "3600"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDbContextFactory<SearchDbContext>>();
                services.RemoveAll<DbContextOptions<SearchDbContext>>();
                services.AddDbContextFactory<SearchDbContext>(options =>
                    options.UseInMemoryDatabase(_databaseName)
                        .ConfigureWarnings(warnings =>
                            warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        });
        return Task.CompletedTask;
    }

    [Fact]
    public async Task HealthAndIndexApi_WorkWithoutSearchEngines()
    {
        using HttpClient client = _factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            "/api/v1/index/documents/news:test-1",
            new
            {
                sourceId = "test-1",
                sourceType = "news",
                title = "测试标题",
                contentHtml = "<p>测试正文</p>",
                cover = "https://cdn.example.com/test-1.jpg",
                publisher = "测试发布者",
                author = "测试作者",
                publishTime = "2026-07-30T08:00:00+08:00",
                indexVersion = 1
            });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using IServiceScope scope = _factory.Services.CreateScope();
        IDbContextFactory<SearchDbContext> dbFactory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<SearchDbContext>>();
        await using SearchDbContext db = await dbFactory.CreateDbContextAsync();
        DesiredDocumentEntity desired = await db.DesiredDocuments.SingleAsync();
        desired.ContentText.Should().Be("测试正文");
        desired.Cover.Should().Be("https://cdn.example.com/test-1.jpg");
        desired.IndexVersion.Should().Be(1);
    }

    [Fact]
    public async Task InvalidRequest_ReturnsProblemDetailsWithoutStackTrace()
    {
        using HttpClient client = _factory.CreateClient();
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            "/api/v1/index/documents/news:test-2",
            new
            {
                sourceId = "",
                sourceType = "news",
                title = "",
                contentHtml = "",
                indexVersion = 0
            });
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        body.Should().NotContain("System.");
        body.Should().NotContain(" at ");
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
