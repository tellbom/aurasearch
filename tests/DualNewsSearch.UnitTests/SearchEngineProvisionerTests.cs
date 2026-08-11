using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Infrastructure.Provisioning;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.UnitTests;

public sealed class SearchEngineProvisionerTests
{
    [Fact]
    public async Task MissingElasticsearchIndexAndVespaPackageAreProvisionedFromEmbeddedResources()
    {
        string? createdIndexJson = null;
        byte[]? vespaArchive = null;
        var elasticsearchHandler = new DelegateHandler(async request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/")
            {
                return Json(HttpStatusCode.OK, "{\"version\":{\"number\":\"7.17.9\"}}");
            }
            if (request.Method == HttpMethod.Head && path == "/news-auto")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (request.Method == HttpMethod.Get && path == "/_alias/news-read")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (request.Method == HttpMethod.Put && path == "/news-auto")
            {
                createdIndexJson = await request.Content!.ReadAsStringAsync();
                return Json(HttpStatusCode.OK, "{\"acknowledged\":true}");
            }
            throw new InvalidOperationException($"Unexpected Elasticsearch request: {request.Method} {path}");
        });
        var vespaHandler = new DelegateHandler(async request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.Should().Be(
                "/application/v2/tenant/default/prepareandactivate");
            request.Content!.Headers.ContentType!.MediaType.Should().Be("application/zip");
            vespaArchive = await request.Content.ReadAsByteArrayAsync();
            return Json(HttpStatusCode.OK, "{\"message\":\"Session 2 for tenant 'default' prepared and activated.\"}");
        });
        SearchEngineProvisioner provisioner = CreateProvisioner(
            elasticsearchHandler,
            vespaHandler,
            new ElasticsearchOptions
            {
                Endpoint = "http://es/",
                IndexName = "news-auto",
                IndexAlias = "news-read"
            },
            new VespaOptions
            {
                Endpoint = "http://vespa-query/",
                ConfigEndpoint = "http://vespa-config/",
                Namespace = "news",
                DocumentType = "news",
                RankProfile = "cjk_bm25_all"
            });

        await provisioner.ProvisionAsync(CancellationToken.None);

        createdIndexJson.Should().NotBeNull();
        using (JsonDocument index = JsonDocument.Parse(createdIndexJson!))
        {
            index.RootElement.GetProperty("settings").GetProperty("analysis")
                .GetProperty("analyzer").GetProperty("news_ik")
                .GetProperty("tokenizer").GetString().Should().Be("ik_max_word");
            index.RootElement.GetProperty("aliases").TryGetProperty("news-read", out _)
                .Should().BeTrue();
        }

        vespaArchive.Should().NotBeNull();
        using var archiveStream = new MemoryStream(vespaArchive!);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        archive.Entries.Select(x => x.FullName).Should().BeEquivalentTo(
            "hosts.xml",
            "services.xml",
            "schemas/news.sd");
        using StreamReader schemaReader = new(
            archive.GetEntry("schemas/news.sd")!.Open(),
            Encoding.UTF8);
        (await schemaReader.ReadToEndAsync()).Should().Contain("rank-profile cjk_bm25_all");
    }

    [Fact]
    public async Task ExistingConfiguredElasticsearchIndexAndAliasAreNotModified()
    {
        var requests = new List<string>();
        var elasticsearchHandler = new DelegateHandler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            requests.Add($"{request.Method} {path}");
            return Task.FromResult((request.Method, path) switch
            {
                ({ } method, "/") when method == HttpMethod.Get =>
                    Json(HttpStatusCode.OK, "{\"version\":{\"number\":\"7.17.9\"}}"),
                ({ } method, "/news-v1") when method == HttpMethod.Head =>
                    new HttpResponseMessage(HttpStatusCode.OK),
                ({ } method, "/_alias/news-read") when method == HttpMethod.Get =>
                    Json(HttpStatusCode.OK, "{\"news-v1\":{\"aliases\":{\"news-read\":{}}}}"),
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {path}")
            });
        });
        SearchEngineProvisioner provisioner = CreateProvisioner(
            elasticsearchHandler,
            new DelegateHandler(_ => throw new InvalidOperationException("Vespa must be disabled.")),
            new ElasticsearchOptions
            {
                Endpoint = "http://es/",
                IndexName = "news-v1",
                IndexAlias = "news-read"
            },
            new VespaOptions
            {
                Endpoint = "http://vespa-query/",
                ConfigEndpoint = "http://vespa-config/",
                Namespace = "news",
                DocumentType = "news",
                RankProfile = "cjk_bm25_all",
                ProvisioningEnabled = false
            });

        await provisioner.ProvisionAsync(CancellationToken.None);

        requests.Should().Equal("GET /", "HEAD /news-v1", "GET /_alias/news-read");
    }

    [Fact]
    public async Task MissingIndexDoesNotCreateASecondTargetForAnExistingAlias()
    {
        var elasticsearchHandler = new DelegateHandler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/")
            {
                return Task.FromResult(Json(
                    HttpStatusCode.OK,
                    "{\"version\":{\"number\":\"7.17.9\"}}"));
            }
            if (request.Method == HttpMethod.Head && path == "/news-v2")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            if (request.Method == HttpMethod.Get && path == "/_alias/news-read")
            {
                return Task.FromResult(Json(
                    HttpStatusCode.OK,
                    "{\"news-v1\":{\"aliases\":{\"news-read\":{}}}}"));
            }
            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        });
        SearchEngineProvisioner provisioner = CreateProvisioner(
            elasticsearchHandler,
            new DelegateHandler(_ => throw new InvalidOperationException("Vespa must be disabled.")),
            new ElasticsearchOptions
            {
                Endpoint = "http://es/",
                IndexName = "news-v2",
                IndexAlias = "news-read"
            },
            new VespaOptions
            {
                Endpoint = "http://vespa-query/",
                ConfigEndpoint = "http://vespa-config/",
                Namespace = "news",
                DocumentType = "news",
                RankProfile = "cjk_bm25_all",
                ProvisioningEnabled = false
            });

        Func<Task> act = () => provisioner.ProvisionAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Refusing to create a second alias target*");
    }

    private static SearchEngineProvisioner CreateProvisioner(
        HttpMessageHandler elasticsearchHandler,
        HttpMessageHandler vespaHandler,
        ElasticsearchOptions elasticsearch,
        VespaOptions vespa)
    {
        var clients = new Dictionary<string, HttpClient>(StringComparer.Ordinal)
        {
            [SearchEngineProvisioner.ElasticsearchClientName] = new(elasticsearchHandler)
            {
                BaseAddress = new Uri(elasticsearch.Endpoint)
            },
            [SearchEngineProvisioner.VespaConfigClientName] = new(vespaHandler)
            {
                BaseAddress = new Uri(vespa.ConfigEndpoint)
            }
        };
        return new SearchEngineProvisioner(
            new DictionaryHttpClientFactory(clients),
            Options.Create(elasticsearch),
            Options.Create(vespa),
            NullLogger<SearchEngineProvisioner>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class DictionaryHttpClientFactory : IHttpClientFactory
    {
        private readonly IReadOnlyDictionary<string, HttpClient> _clients;

        public DictionaryHttpClientFactory(IReadOnlyDictionary<string, HttpClient> clients)
        {
            _clients = clients;
        }

        public HttpClient CreateClient(string name) => _clients[name];
    }

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => _handler(request);
    }
}
