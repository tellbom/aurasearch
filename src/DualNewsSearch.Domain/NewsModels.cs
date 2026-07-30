using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DualNewsSearch.Domain;

public enum SourceType
{
    News,
    Announcement,
    Portal
}

public enum DesiredOperation
{
    Upsert,
    Delete
}

public enum DesiredWriteStatus
{
    Accepted,
    NoOp,
    Stale
}

public sealed record NewsSearchDocument(
    string NewsId,
    string SourceId,
    SourceType SourceType,
    string Title,
    string ContentText,
    string Publisher,
    string Author,
    DateTimeOffset PublishTime,
    long IndexVersion,
    string ContentHash,
    DateTimeOffset UpdatedAt);

public static class ContentHash
{
    private const char UnitSeparator = '\u001f';

    public static string Compute(
        string title,
        string contentText,
        string publisher,
        string author,
        DateTimeOffset publishTime,
        SourceType sourceType)
    {
        string canonical = string.Join(
            UnitSeparator,
            Normalize(title),
            Normalize(contentText),
            Normalize(publisher),
            Normalize(author),
            publishTime.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            sourceType.ToString().ToLowerInvariant());

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static string Normalize(string? value) => value?.Normalize(NormalizationForm.FormC) ?? string.Empty;
}

