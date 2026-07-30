using System.Globalization;
using System.Text;
using System.Text.Json;
using DualNewsSearch.Domain;

Console.OutputEncoding = Encoding.UTF8;
if (args.Length < 3 || args[0] is not ("pool" or "score"))
{
    Console.Error.WriteLine(
        "Usage:\n" +
        "  dotnet run -- pool <results.jsonl> <blind-judgements.tsv>\n" +
        "  dotnet run -- score <results.jsonl> <judgements.tsv>");
    return 2;
}

string command = args[0];
string resultsPath = args[1];
string outputOrJudgementsPath = args[2];
List<QueryResults> queries = File.ReadLines(resultsPath)
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Select(x => JsonSerializer.Deserialize<QueryResults>(
        x,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("Invalid JSONL row."))
    .ToList();

if (command == "pool")
{
    using var writer = new StreamWriter(outputOrJudgementsPath, false, new UTF8Encoding(false));
    await writer.WriteLineAsync("query_id\tquery\tpool_position\tnews_id\ttitle\trelevance_0_to_3\tjudge");
    foreach (QueryResults query in queries.OrderBy(x => x.QueryId, StringComparer.Ordinal))
    {
        SearchRow[] pool = query.Results
            .GroupBy(x => x.NewsId, StringComparer.Ordinal)
            .Select(x => x.First())
            .OrderBy(x => StableBlindKey(query.QueryId, x.NewsId), StringComparer.Ordinal)
            .ToArray();
        for (int i = 0; i < pool.Length; i++)
        {
            SearchRow row = pool[i];
            await writer.WriteLineAsync(string.Join(
                '\t',
                Escape(query.QueryId),
                Escape(query.Query),
                (i + 1).ToString(CultureInfo.InvariantCulture),
                Escape(row.NewsId),
                Escape(row.Title),
                string.Empty,
                string.Empty));
        }
    }
    return 0;
}

Dictionary<(string QueryId, string NewsId), int> judgements = File.ReadLines(outputOrJudgementsPath)
    .Skip(1)
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Select(ParseJudgement)
    .ToDictionary(x => (x.QueryId, x.NewsId), x => x.Relevance);

var report = new List<object>();
foreach (IGrouping<string, QueryResults> queryGroup in queries.GroupBy(x => x.QueryId))
{
    foreach (IGrouping<string, SearchRow> engine in queryGroup
                 .SelectMany(x => x.Results)
                 .GroupBy(x => x.Engine, StringComparer.OrdinalIgnoreCase))
    {
        SearchRow[] ranked = engine.OrderBy(x => x.Rank).ToArray();
        int[] relevance = ranked
            .Select(x => judgements.GetValueOrDefault((queryGroup.Key, x.NewsId), 0))
            .ToArray();
        HashSet<string> relevantIds = judgements
            .Where(x => x.Key.QueryId == queryGroup.Key && x.Value > 0)
            .Select(x => x.Key.NewsId)
            .ToHashSet(StringComparer.Ordinal);
        report.Add(new
        {
            queryId = queryGroup.Key,
            engine = engine.Key,
            ndcg10 = EvaluationMetrics.NormalizedDiscountedCumulativeGain(relevance, 10),
            mrr10 = EvaluationMetrics.MeanReciprocalRank(relevance, 10),
            recall20 = EvaluationMetrics.RecallAtK(
                ranked.Select(x => x.NewsId).ToArray(),
                relevantIds,
                20)
        });
    }
}

Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
return 0;

static string StableBlindKey(string queryId, string newsId)
{
    byte[] bytes = System.Security.Cryptography.SHA256.HashData(
        Encoding.UTF8.GetBytes($"{queryId}\u001f{newsId}"));
    return Convert.ToHexString(bytes);
}

static string Escape(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

static Judgement ParseJudgement(string line)
{
    string[] fields = line.Split('\t');
    if (fields.Length < 7
        || !int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int relevance)
        || relevance is < 0 or > 3)
    {
        throw new InvalidDataException($"Invalid judgement row: {line}");
    }
    return new Judgement(fields[0], fields[3], relevance);
}

public sealed record QueryResults(string QueryId, string Query, IReadOnlyList<SearchRow> Results);
public sealed record SearchRow(string Engine, int Rank, string NewsId, string Title, double LatencyMs);
public sealed record Judgement(string QueryId, string NewsId, int Relevance);

