using System.Text.Json;
using DualNewsSearch.Domain;

namespace DualNewsSearch.Infrastructure.Search;

internal static class JsonSearchParsing
{
    public static SourceType ParseSourceType(string? value)
    {
        return Enum.TryParse(value, true, out SourceType sourceType)
            ? sourceType
            : SourceType.News;
    }

    public static string? StringProperty(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    public static DateTimeOffset DateProperty(JsonElement element, string name)
    {
        return DateTimeOffset.TryParse(StringProperty(element, name), out DateTimeOffset parsed)
            ? parsed
            : DateTimeOffset.UnixEpoch;
    }
}

