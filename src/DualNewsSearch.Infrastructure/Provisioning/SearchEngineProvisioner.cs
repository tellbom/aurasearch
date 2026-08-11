using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DualNewsSearch.Application.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.Infrastructure.Provisioning;

public sealed class SearchEngineProvisioner
{
    public const string ElasticsearchClientName = "provisioning-elasticsearch";
    public const string VespaConfigClientName = "provisioning-vespa-config";

    private const string ElasticsearchTemplateResource =
        "DualNewsSearch.Provisioning.elasticsearch-index.json";

    private static readonly IReadOnlyDictionary<string, string> VespaResources =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hosts.xml"] = "DualNewsSearch.Provisioning.Vespa.hosts.xml",
            ["services.xml"] = "DualNewsSearch.Provisioning.Vespa.services.xml",
            ["schemas/news.sd"] = "DualNewsSearch.Provisioning.Vespa.schemas.news.sd"
        };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ElasticsearchOptions _elasticsearch;
    private readonly VespaOptions _vespa;
    private readonly ILogger<SearchEngineProvisioner> _logger;

    public SearchEngineProvisioner(
        IHttpClientFactory httpClientFactory,
        IOptions<ElasticsearchOptions> elasticsearch,
        IOptions<VespaOptions> vespa,
        ILogger<SearchEngineProvisioner> logger)
    {
        _httpClientFactory = httpClientFactory;
        _elasticsearch = elasticsearch.Value;
        _vespa = vespa.Value;
        _logger = logger;
    }

    public async Task ProvisionAsync(CancellationToken cancellationToken)
    {
        if (_elasticsearch.ProvisioningEnabled)
        {
            await ProvisionElasticsearchAsync(cancellationToken);
        }

        if (_vespa.ProvisioningEnabled)
        {
            await ProvisionVespaAsync(cancellationToken);
        }
    }

    private async Task ProvisionElasticsearchAsync(CancellationToken cancellationToken)
    {
        HttpClient client = _httpClientFactory.CreateClient(ElasticsearchClientName);
        using (HttpResponseMessage versionResponse = await client.GetAsync(string.Empty, cancellationToken))
        {
            versionResponse.EnsureSuccessStatusCode();
            using JsonDocument version = await JsonDocument.ParseAsync(
                await versionResponse.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            string? number = version.RootElement.GetProperty("version").GetProperty("number").GetString();
            if (number is null || !number.StartsWith("7.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Elasticsearch provisioning requires major version 7; actual='{number ?? "unknown"}'.");
            }
        }

        string escapedIndex = Uri.EscapeDataString(_elasticsearch.IndexName);
        using HttpResponseMessage indexProbe = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, escapedIndex),
            cancellationToken);
        if (indexProbe.StatusCode == HttpStatusCode.NotFound)
        {
            string escapedExistingAlias = Uri.EscapeDataString(_elasticsearch.IndexAlias);
            using HttpResponseMessage existingAlias = await client.GetAsync(
                $"_alias/{escapedExistingAlias}",
                cancellationToken);
            if (existingAlias.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Elasticsearch index '{_elasticsearch.IndexName}' is missing, but alias " +
                    $"'{_elasticsearch.IndexAlias}' already exists. Refusing to create a second alias target; " +
                    "use the explicit reindex and alias-cutover procedure.");
            }
            if (existingAlias.StatusCode != HttpStatusCode.NotFound)
            {
                existingAlias.EnsureSuccessStatusCode();
            }

            JsonObject template = await ReadJsonResourceAsync(
                ElasticsearchTemplateResource,
                cancellationToken);
            template["aliases"] = new JsonObject
            {
                [_elasticsearch.IndexAlias] = new JsonObject()
            };
            using HttpResponseMessage createResponse = await client.PutAsJsonAsync(
                escapedIndex,
                template,
                cancellationToken);
            if (!createResponse.IsSuccessStatusCode)
            {
                using HttpResponseMessage raceProbe = await client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Head, escapedIndex),
                    cancellationToken);
                if (!raceProbe.IsSuccessStatusCode)
                {
                    string body = await createResponse.Content.ReadAsStringAsync(cancellationToken);
                    throw new HttpRequestException(
                        $"Elasticsearch index creation failed: HTTP {(int)createResponse.StatusCode}: {body}");
                }
            }
            _logger.LogInformation(
                "Created Elasticsearch index {IndexName} with alias {IndexAlias}.",
                _elasticsearch.IndexName,
                _elasticsearch.IndexAlias);
            return;
        }
        indexProbe.EnsureSuccessStatusCode();

        string escapedAlias = Uri.EscapeDataString(_elasticsearch.IndexAlias);
        using HttpResponseMessage aliasProbe = await client.GetAsync(
            $"_alias/{escapedAlias}",
            cancellationToken);
        if (aliasProbe.StatusCode == HttpStatusCode.NotFound)
        {
            var request = new
            {
                actions = new[]
                {
                    new { add = new { index = _elasticsearch.IndexName, alias = _elasticsearch.IndexAlias } }
                }
            };
            using HttpResponseMessage aliasResponse = await client.PostAsJsonAsync(
                "_aliases",
                request,
                cancellationToken);
            aliasResponse.EnsureSuccessStatusCode();
            _logger.LogInformation(
                "Added Elasticsearch alias {IndexAlias} to {IndexName}.",
                _elasticsearch.IndexAlias,
                _elasticsearch.IndexName);
            return;
        }
        aliasProbe.EnsureSuccessStatusCode();
        using JsonDocument aliases = await JsonDocument.ParseAsync(
            await aliasProbe.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        if (!aliases.RootElement.TryGetProperty(_elasticsearch.IndexName, out _))
        {
            throw new InvalidOperationException(
                $"Elasticsearch alias '{_elasticsearch.IndexAlias}' exists but does not point to " +
                $"the configured index '{_elasticsearch.IndexName}'.");
        }

        _logger.LogInformation(
            "Elasticsearch index {IndexName} and alias {IndexAlias} already exist; skipped creation.",
            _elasticsearch.IndexName,
            _elasticsearch.IndexAlias);
    }

    private async Task ProvisionVespaAsync(CancellationToken cancellationToken)
    {
        HttpClient client = _httpClientFactory.CreateClient(VespaConfigClientName);
        using var archiveStream = new MemoryStream();
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string entryName, string resourceName) in VespaResources)
            {
                ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                await using Stream target = entry.Open();
                await using Stream source = OpenResource(resourceName);
                await source.CopyToAsync(target, cancellationToken);
            }
        }
        archiveStream.Position = 0;
        using var content = new StreamContent(archiveStream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        using HttpResponseMessage response = await client.PostAsync(
            "application/v2/tenant/default/prepareandactivate",
            content,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Vespa Application Package deployment failed: HTTP {(int)response.StatusCode}: {body}");
        }
        _logger.LogInformation("Vespa Application Package prepared and activated.");
    }

    private static async Task<JsonObject> ReadJsonResourceAsync(
        string resourceName,
        CancellationToken cancellationToken)
    {
        await using Stream stream = OpenResource(resourceName);
        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
        JsonNode? node = JsonNode.Parse(document.RootElement.GetRawText());
        return node as JsonObject
            ?? throw new InvalidDataException($"Embedded resource is not a JSON object: {resourceName}");
    }

    private static Stream OpenResource(string resourceName)
    {
        return typeof(SearchEngineProvisioner).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded provisioning resource is missing: {resourceName}");
    }
}
