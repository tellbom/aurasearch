using System.Net.Http.Json;
using System.Text.Json;

if (args.Length < 2)
{
    Console.Error.WriteLine(
        "Usage: dotnet run -- <documents.jsonl> <api-base-url> [checkpoint-file] [concurrency]");
    return 2;
}

string inputPath = args[0];
var baseUri = new Uri(args[1].TrimEnd('/') + "/", UriKind.Absolute);
string checkpointPath = args.Length > 2 ? args[2] : $"{inputPath}.checkpoint";
int concurrency = args.Length > 3 && int.TryParse(args[3], out int parsed) ? parsed : 4;
if (concurrency is < 1 or > 32)
{
    throw new ArgumentOutOfRangeException(nameof(concurrency), "Concurrency must be 1 to 32.");
}

string? checkpoint = File.Exists(checkpointPath)
    ? (await File.ReadAllTextAsync(checkpointPath)).Trim()
    : null;
bool resumeReached = string.IsNullOrWhiteSpace(checkpoint);
using var client = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };
using var gate = new SemaphoreSlim(concurrency);
var pending = new List<Task<BackfillResult>>();
var completedBySequence = new SortedDictionary<long, BackfillResult>();
long sequence = 0;
long nextCheckpointSequence = 1;
long read = 0;
long succeeded = 0;
long failed = 0;
var started = System.Diagnostics.Stopwatch.StartNew();

foreach (string line in File.ReadLines(inputPath))
{
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }
    BackfillDocument document = JsonSerializer.Deserialize<BackfillDocument>(
        line,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("Invalid JSONL document.");
    if (!resumeReached)
    {
        resumeReached = document.NewsId == checkpoint;
        continue;
    }

    read++;
    sequence++;
    await gate.WaitAsync();
    pending.Add(SendAsync(sequence, document, client, gate));
    if (pending.Count >= concurrency * 4)
    {
        Task<BackfillResult> completed = await Task.WhenAny(pending);
        pending.Remove(completed);
        BackfillResult result = await completed;
        await RecordAsync(result);
    }
}

foreach (BackfillResult result in await Task.WhenAll(pending))
{
    await RecordAsync(result);
}

Console.WriteLine(
    $"read={read} succeeded={succeeded} failed={failed} " +
    $"throughput={succeeded / Math.Max(0.001, started.Elapsed.TotalSeconds):F2}/s");
return failed == 0 ? 0 : 1;

async Task RecordAsync(BackfillResult result)
{
    completedBySequence[result.Sequence] = result;
    if (result.Success)
    {
        succeeded++;
    }
    else
    {
        failed++;
        await File.AppendAllTextAsync(
            $"{checkpointPath}.errors.jsonl",
            JsonSerializer.Serialize(result) + Environment.NewLine);
    }

    while (completedBySequence.TryGetValue(nextCheckpointSequence, out BackfillResult? contiguous)
        && contiguous.Success)
    {
        await File.WriteAllTextAsync(checkpointPath, contiguous.NewsId);
        completedBySequence.Remove(nextCheckpointSequence);
        nextCheckpointSequence++;
    }
}

static async Task<BackfillResult> SendAsync(
    long sequence,
    BackfillDocument document,
    HttpClient client,
    SemaphoreSlim gate)
{
    try
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"api/v1/index/documents/{Uri.EscapeDataString(document.NewsId)}",
            document.Document);
        return new BackfillResult(
            sequence,
            document.NewsId,
            response.IsSuccessStatusCode,
            (int)response.StatusCode,
            response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync());
    }
    catch (Exception exception)
    {
        return new BackfillResult(sequence, document.NewsId, false, 0, exception.Message);
    }
    finally
    {
        gate.Release();
    }
}

public sealed record BackfillDocument(string NewsId, JsonElement Document);
public sealed record BackfillResult(
    long Sequence,
    string NewsId,
    bool Success,
    int StatusCode,
    string? Error);
