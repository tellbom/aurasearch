using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DualNewsSearch.Application.Abstractions;
using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Contracts;
using DualNewsSearch.Domain;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.Infrastructure.Search;

public sealed class VespaAdapter :
    ISearchEngineAdapter,
    IIndexSink,
    IEngineDiagnostics,
    IEngineConsistencyProbe,
    IQueryDiagnosticsRenderer
{
    private readonly HttpClient _client;
    private readonly VespaOptions _options;
    private readonly IClock _clock;

    public VespaAdapter(HttpClient client, IOptions<VespaOptions> options, IClock clock)
    {
        _client = client;
        _options = options.Value;
        _clock = clock;
    }

    public string Name => "vespa";

    public async Task<EngineSearchResult> SearchAsync(
        SearchQuery query,
        int topK,
        Guid searchTraceId,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> parameters = BuildQueryParameters(query, topK, _options);
        string path = "search/?" + string.Join(
            "&",
            parameters.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        var watch = Stopwatch.StartNew();
        using HttpResponseMessage response = await _client.GetAsync(path, cancellationToken);
        watch.Stop();
        if (!response.IsSuccessStatusCode)
        {
            return new EngineSearchResult(
                Name,
                Array.Empty<SearchCandidate>(),
                watch.ElapsedMilliseconds,
                false,
                $"HTTP {(int)response.StatusCode}",
                path);
        }

        using JsonDocument json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var candidates = new List<SearchCandidate>();
        if (json.RootElement.GetProperty("root").TryGetProperty("children", out JsonElement children))
        {
            int rank = 1;
            foreach (JsonElement child in children.EnumerateArray())
            {
                JsonElement fields = child.GetProperty("fields");
                string newsId = JsonSearchParsing.StringProperty(fields, "news_id") ?? string.Empty;
                candidates.Add(new SearchCandidate(
                    newsId,
                    JsonSearchParsing.StringProperty(fields, "title") ?? string.Empty,
                    JsonSearchParsing.StringProperty(fields, "documentid"),
                    JsonSearchParsing.StringProperty(fields, "publisher") ?? string.Empty,
                    JsonSearchParsing.StringProperty(fields, "author") ?? string.Empty,
                    JsonSearchParsing.ParseSourceType(JsonSearchParsing.StringProperty(fields, "source_type")),
                    ParseVespaTime(fields),
                    rank++,
                    child.TryGetProperty("relevance", out JsonElement relevance)
                        ? relevance.GetDouble()
                        : 0));
            }
        }

        return new EngineSearchResult(Name, candidates, watch.ElapsedMilliseconds, false, null, path);
    }

    public async Task<IndexApplyResult> ApplyAsync(
        DesiredDocumentWrite write,
        CancellationToken cancellationToken)
    {
        NewsSearchDocument document = write.Document;
        string basePath = $"document/v1/{Uri.EscapeDataString(_options.Namespace)}/" +
            $"{Uri.EscapeDataString(_options.DocumentType)}/docid/{Uri.EscapeDataString(document.NewsId)}";
        string condition = $"{_options.DocumentType}.index_version < {document.IndexVersion}";
        HttpResponseMessage response;
        if (write.Operation == DesiredOperation.Delete)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, basePath)
            {
                Content = JsonContent.Create(new { condition })
            };
            response = await _client.SendAsync(request, cancellationToken);
        }
        else
        {
            var payload = new
            {
                condition,
                create = true,
                fields = new
                {
                    news_id = document.NewsId,
                    source_id = document.SourceId,
                    source_type = document.SourceType.ToString().ToLowerInvariant(),
                    index_version = document.IndexVersion,
                    content_hash = document.ContentHash,
                    title = document.Title,
                    content = document.ContentText,
                    publisher = document.Publisher,
                    author = document.Author,
                    publish_time = document.PublishTime.ToUnixTimeSeconds()
                }
            };
            response = await _client.PostAsJsonAsync(basePath, payload, cancellationToken);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                return new IndexApplyResult(IndexApplyStatus.Applied);
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            string error = $"Vespa HTTP {(int)response.StatusCode}";
            if (response.StatusCode == HttpStatusCode.PreconditionFailed
                || body.Contains("condition", StringComparison.OrdinalIgnoreCase))
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

    public string RenderQuery(SearchQuery query, int topK)
    {
        IReadOnlyDictionary<string, string> parameters =
            BuildQueryParameters(query, topK, _options);
        return "search/?" + string.Join(
            "&",
            parameters.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    public async Task<EngineHealth> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _client.GetAsync(
                "ApplicationStatus",
                cancellationToken);
            return new EngineHealth(
                Name,
                response.IsSuccessStatusCode,
                null,
                response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}",
                _clock.UtcNow);
        }
        catch (Exception exception)
        {
            return new EngineHealth(Name, false, null, exception.Message, _clock.UtcNow);
        }
    }

    public async Task<long> CountAsync(string? sourceType, CancellationToken cancellationToken)
    {
        string yql = string.IsNullOrWhiteSpace(sourceType)
            ? $"select news_id from {_options.DocumentType} where true"
            : $"select news_id from {_options.DocumentType} where source_type contains @sourceType";
        var parameters = new Dictionary<string, string>
        {
            ["yql"] = yql,
            ["hits"] = "0"
        };
        if (!string.IsNullOrWhiteSpace(sourceType))
        {
            parameters["sourceType"] = sourceType;
        }
        string path = "search/?" + string.Join(
            "&",
            parameters.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        using HttpResponseMessage response = await _client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return json.RootElement.GetProperty("root").GetProperty("fields")
            .GetProperty("totalCount").GetInt64();
    }

    public async Task<(string ContentHash, long IndexVersion)?> GetVersionHashAsync(
        string newsId,
        CancellationToken cancellationToken)
    {
        string path = $"document/v1/{Uri.EscapeDataString(_options.Namespace)}/" +
            $"{Uri.EscapeDataString(_options.DocumentType)}/docid/{Uri.EscapeDataString(newsId)}";
        using HttpResponseMessage response = await _client.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        using JsonDocument json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        JsonElement fields = json.RootElement.GetProperty("fields");
        return (
            JsonSearchParsing.StringProperty(fields, "content_hash") ?? string.Empty,
            fields.GetProperty("index_version").GetInt64());
    }

    internal static IReadOnlyDictionary<string, string> BuildQueryParameters(
        SearchQuery query,
        int topK,
        VespaOptions options)
    {
        var clauses = new List<string> { "userQuery()" };
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["query"] = query.Query,
            ["type"] = "all",
            ["ranking"] = options.RankProfile,
            ["hits"] = topK.ToString(CultureInfo.InvariantCulture),
            ["timeout"] = $"{options.TimeoutMs}ms",
            ["presentation.summary"] = "short"
        };

        if (query.SourceTypes.Count > 0)
        {
            var sourceClauses = new List<string>();
            for (int i = 0; i < query.SourceTypes.Count; i++)
            {
                string key = $"sourceType{i}";
                sourceClauses.Add($"source_type contains @{key}");
                values[key] = query.SourceTypes[i].ToString().ToLowerInvariant();
            }
            clauses.Add($"({string.Join(" or ", sourceClauses)})");
        }

        AddContainsClause(clauses, values, "publisher", query.Publisher);
        AddContainsClause(clauses, values, "author", query.Author);
        if (query.PublishTimeFrom.HasValue)
        {
            clauses.Add("publish_time >= @publishTimeFrom");
            values["publishTimeFrom"] = query.PublishTimeFrom.Value
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);
        }
        if (query.PublishTimeTo.HasValue)
        {
            clauses.Add("publish_time <= @publishTimeTo");
            values["publishTimeTo"] = query.PublishTimeTo.Value
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);
        }

        values["yql"] = $"select news_id,title,publisher,author,source_type,publish_time " +
            $"from {_optionsDocument(options)} where {string.Join(" and ", clauses)}";
        return values;
    }

    private static string _optionsDocument(VespaOptions options)
    {
        return options.DocumentType.All(char.IsLetterOrDigit)
            ? options.DocumentType
            : throw new InvalidOperationException("Vespa DocumentType must be alphanumeric.");
    }

    private static void AddContainsClause(
        ICollection<string> clauses,
        IDictionary<string, string> values,
        string field,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            clauses.Add($"{field} contains @{field}");
            values[field] = value;
        }
    }

    private static DateTimeOffset ParseVespaTime(JsonElement fields)
    {
        if (fields.TryGetProperty("publish_time", out JsonElement value)
            && value.TryGetInt64(out long seconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }

        return DateTimeOffset.UnixEpoch;
    }
}
