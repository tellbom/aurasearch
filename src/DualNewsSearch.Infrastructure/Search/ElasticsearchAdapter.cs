using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DualNewsSearch.Application.Abstractions;
using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Domain;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.Infrastructure.Search;

public sealed class ElasticsearchAdapter :
    ISearchEngineAdapter,
    ISuggestAdapter,
    IIndexSink,
    IEngineDiagnostics,
    IEngineConsistencyProbe,
    IQueryDiagnosticsRenderer
{
    private readonly HttpClient _client;
    private readonly ElasticsearchOptions _options;
    private readonly IClock _clock;

    public ElasticsearchAdapter(
        HttpClient client,
        IOptions<ElasticsearchOptions> options,
        IClock clock)
    {
        _client = client;
        _options = options.Value;
        _clock = clock;
    }

    public string Name => "elasticsearch";

    public async Task<EngineSearchResult> SearchAsync(
        SearchQuery query,
        int topK,
        Guid searchTraceId,
        CancellationToken cancellationToken)
    {
        JsonObject body = BuildSearchBody(query, topK);
        string diagnostic = body.ToJsonString();
        var watch = Stopwatch.StartNew();
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            $"{Uri.EscapeDataString(_options.IndexAlias)}/_search",
            body,
            cancellationToken);
        watch.Stop();
        if (!response.IsSuccessStatusCode)
        {
            return new EngineSearchResult(
                Name,
                Array.Empty<SearchCandidate>(),
                watch.ElapsedMilliseconds,
                false,
                $"HTTP {(int)response.StatusCode}",
                diagnostic);
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement hits = json.RootElement.GetProperty("hits").GetProperty("hits");
        var candidates = new List<SearchCandidate>();
        int rank = 1;
        foreach (JsonElement hit in hits.EnumerateArray())
        {
            JsonElement source = hit.GetProperty("_source");
            string newsId = JsonSearchParsing.StringProperty(source, "news_id")
                ?? hit.GetProperty("_id").GetString()
                ?? string.Empty;
            string? highlight = null;
            if (hit.TryGetProperty("highlight", out JsonElement highlights))
            {
                highlight = highlights.EnumerateObject()
                    .SelectMany(x => x.Value.EnumerateArray())
                    .Select(x => x.GetString())
                    .FirstOrDefault(x => x is not null);
            }

            candidates.Add(new SearchCandidate(
                newsId,
                JsonSearchParsing.StringProperty(source, "title") ?? string.Empty,
                highlight,
                JsonSearchParsing.StringProperty(source, "publisher") ?? string.Empty,
                JsonSearchParsing.StringProperty(source, "author") ?? string.Empty,
                JsonSearchParsing.ParseSourceType(JsonSearchParsing.StringProperty(source, "source_type")),
                JsonSearchParsing.DateProperty(source, "publish_time"),
                rank++,
                JsonSearchParsing.DoubleProperty(hit, "_score")));
        }

        return new EngineSearchResult(Name, candidates, watch.ElapsedMilliseconds, false, null, diagnostic);
    }

    public async Task<IReadOnlyList<string>> SuggestAsync(
        string query,
        int size,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<string>();
        }

        var body = new JsonObject
        {
            ["size"] = size,
            ["_source"] = new JsonArray("title"),
            ["query"] = new JsonObject
            {
                ["match_phrase_prefix"] = new JsonObject { ["title"] = query.Trim() }
            }
        };
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            $"{Uri.EscapeDataString(_options.IndexAlias)}/_search",
            body,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return json.RootElement.GetProperty("hits").GetProperty("hits")
            .EnumerateArray()
            .Select(x => JsonSearchParsing.StringProperty(x.GetProperty("_source"), "title"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .Take(size)
            .ToArray();
    }

    public string RenderQuery(SearchQuery query, int topK)
    {
        return BuildSearchBody(query, topK).ToJsonString();
    }

    public async Task<IndexApplyResult> ApplyAsync(
        DesiredDocumentWrite write,
        CancellationToken cancellationToken)
    {
        NewsSearchDocument document = write.Document;
        string id = Uri.EscapeDataString(document.NewsId);
        string path = $"{Uri.EscapeDataString(_options.IndexAlias)}/_doc/{id}" +
            $"?version={document.IndexVersion}&version_type=external";
        HttpResponseMessage response;
        if (write.Operation == DesiredOperation.Delete)
        {
            response = await _client.DeleteAsync(path, cancellationToken);
        }
        else
        {
            var payload = new
            {
                news_id = document.NewsId,
                source_id = document.SourceId,
                source_type = document.SourceType.ToString().ToLowerInvariant(),
                title = document.Title,
                content_text = document.ContentText,
                publisher = document.Publisher,
                author = document.Author,
                publish_time = document.PublishTime.UtcDateTime,
                content_hash = document.ContentHash,
                index_version = document.IndexVersion
            };
            response = await _client.PutAsJsonAsync(path, payload, cancellationToken);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                return new IndexApplyResult(IndexApplyStatus.Applied);
            }

            string error = $"Elasticsearch HTTP {(int)response.StatusCode}";
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return new IndexApplyResult(IndexApplyStatus.Stale, error);
            }

            return new IndexApplyResult(
                (int)response.StatusCode >= 500 || response.StatusCode == (HttpStatusCode)429
                    ? IndexApplyStatus.TransientFailure
                    : IndexApplyStatus.PermanentFailure,
                error);
        }
    }

    public async Task<EngineHealth> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _client.GetAsync(string.Empty, cancellationToken);
            string? version = null;
            if (response.IsSuccessStatusCode)
            {
                using JsonDocument json = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    cancellationToken: cancellationToken);
                if (json.RootElement.TryGetProperty("version", out JsonElement versionNode))
                {
                    version = JsonSearchParsing.StringProperty(versionNode, "number");
                }
            }

            bool compatible = response.IsSuccessStatusCode
                && version is not null
                && version.StartsWith("7.", StringComparison.Ordinal);
            return new EngineHealth(
                Name,
                compatible,
                version,
                compatible ? null : "Elasticsearch must be reachable and major version 7.",
                _clock.UtcNow);
        }
        catch (Exception exception)
        {
            return new EngineHealth(Name, false, null, exception.Message, _clock.UtcNow);
        }
    }

    public async Task<long> CountAsync(string? sourceType, CancellationToken cancellationToken)
    {
        JsonObject query = string.IsNullOrWhiteSpace(sourceType)
            ? new JsonObject { ["match_all"] = new JsonObject() }
            : new JsonObject
            {
                ["term"] = new JsonObject { ["source_type"] = sourceType }
            };
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            $"{Uri.EscapeDataString(_options.IndexAlias)}/_count",
            new JsonObject { ["query"] = query },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return json.RootElement.GetProperty("count").GetInt64();
    }

    public async Task<(string ContentHash, long IndexVersion)?> GetVersionHashAsync(
        string newsId,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _client.GetAsync(
            $"{Uri.EscapeDataString(_options.IndexAlias)}/_doc/{Uri.EscapeDataString(newsId)}" +
            "?_source_includes=content_hash,index_version",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        using JsonDocument json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        JsonElement source = json.RootElement.GetProperty("_source");
        return (
            JsonSearchParsing.StringProperty(source, "content_hash") ?? string.Empty,
            source.GetProperty("index_version").GetInt64());
    }

    internal static JsonObject BuildSearchBody(SearchQuery query, int topK)
    {
        var filters = new JsonArray();
        if (query.SourceTypes.Count > 0)
        {
            filters.Add(new JsonObject
            {
                ["terms"] = new JsonObject
                {
                    ["source_type"] = new JsonArray(
                        query.SourceTypes.Select(x => JsonValue.Create(x.ToString().ToLowerInvariant())).ToArray())
                }
            });
        }

        if (query.PublishTimeFrom.HasValue || query.PublishTimeTo.HasValue)
        {
            var range = new JsonObject();
            if (query.PublishTimeFrom.HasValue)
            {
                range["gte"] = query.PublishTimeFrom.Value.UtcDateTime;
            }
            if (query.PublishTimeTo.HasValue)
            {
                range["lte"] = query.PublishTimeTo.Value.UtcDateTime;
            }
            filters.Add(new JsonObject { ["range"] = new JsonObject { ["publish_time"] = range } });
        }

        AddTermFilter(filters, "publisher", query.Publisher);
        AddTermFilter(filters, "author", query.Author);

        JsonObject textQuery = string.IsNullOrWhiteSpace(query.Query)
            ? new JsonObject { ["match_all"] = new JsonObject() }
            : new JsonObject
            {
                ["multi_match"] = new JsonObject
                {
                    ["query"] = query.Query,
                    ["fields"] = new JsonArray("title^3", "content_text"),
                    ["type"] = "best_fields"
                }
            };

        var body = new JsonObject
        {
            ["size"] = topK,
            ["track_total_hits"] = true,
            ["_source"] = new JsonArray(
                "news_id", "title", "publisher", "author", "source_type", "publish_time"),
            ["query"] = new JsonObject
            {
                ["bool"] = new JsonObject
                {
                    ["must"] = new JsonArray(textQuery),
                    ["filter"] = filters
                }
            },
            ["highlight"] = new JsonObject
            {
                ["fields"] = new JsonObject
                {
                    ["title"] = new JsonObject(),
                    ["content_text"] = new JsonObject
                    {
                        ["fragment_size"] = 160,
                        ["number_of_fragments"] = 1
                    }
                }
            }
        };
        if (string.IsNullOrWhiteSpace(query.Query))
        {
            body["sort"] = new JsonArray(new JsonObject
            {
                ["publish_time"] = new JsonObject { ["order"] = "desc" }
            });
        }
        return body;
    }

    private static void AddTermFilter(JsonArray filters, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            filters.Add(new JsonObject
            {
                ["term"] = new JsonObject { [field] = value }
            });
        }
    }
}
