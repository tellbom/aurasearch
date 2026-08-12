using System.Text;
using System.Text.RegularExpressions;
using DualNewsSearch.Application.Contracts;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;

namespace DualNewsSearch.Infrastructure.Persistence;

public sealed class SearchResultContentStore : ISearchResultContentStore
{
    internal const int NewsSummaryLength = 180;
    private readonly IDbContextFactory<SearchDbContext> _dbFactory;

    public SearchResultContentStore(IDbContextFactory<SearchDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyDictionary<string, SearchResultContent>> GetAsync(
        IReadOnlyCollection<string> newsIds,
        string query,
        CancellationToken cancellationToken)
    {
        if (newsIds.Count == 0)
        {
            return new Dictionary<string, SearchResultContent>(StringComparer.Ordinal);
        }

        string[] ids = newsIds.Distinct(StringComparer.Ordinal).ToArray();
        await using SearchDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        DesiredDocumentEntity[] documents = await db.DesiredDocuments
            .AsNoTracking()
            .Where(x => ids.Contains(x.NewsId) && x.DesiredOperation == "Upsert")
            .ToArrayAsync(cancellationToken);

        return documents.ToDictionary(
            x => x.NewsId,
            x => x.SourceType.Equals("Announcement", StringComparison.OrdinalIgnoreCase)
                ? new SearchResultContent(
                    x.NewsId,
                    null,
                    AnnouncementHtmlFormatter.SanitizeAndHighlight(x.ContentHtml, query),
                    x.Cover)
                : new SearchResultContent(
                    x.NewsId,
                    CreateSummary(x.ContentText),
                    null,
                    x.Cover),
            StringComparer.Ordinal);
    }

    internal static string CreateSummary(string text)
    {
        if (text.Length <= NewsSummaryLength)
        {
            return text;
        }

        int length = NewsSummaryLength - 1;
        if (char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length]))
        {
            length--;
        }
        return text[..length].TrimEnd() + "…";
    }
}

internal static class AnnouncementHtmlFormatter
{
    private static readonly HashSet<string> RemovedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "iframe", "object", "embed", "form"
    };

    public static string SanitizeAndHighlight(string html, string query)
    {
        var document = new HtmlDocument
        {
            OptionFixNestedTags = true,
            OptionAutoCloseOnEnd = true
        };
        document.LoadHtml(html ?? string.Empty);

        foreach (HtmlNode node in document.DocumentNode.Descendants().ToArray())
        {
            if (RemovedElements.Contains(node.Name) || node.NodeType == HtmlNodeType.Comment)
            {
                node.Remove();
                continue;
            }

            if (node.NodeType == HtmlNodeType.Element)
            {
                SanitizeAttributes(node);
            }
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            HighlightTextNodes(document, query.Trim());
        }
        return document.DocumentNode.InnerHtml;
    }

    private static void HighlightTextNodes(HtmlDocument document, string query)
    {
        var matcher = new Regex(
            Regex.Escape(query),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        HtmlTextNode[] textNodes = document.DocumentNode
            .DescendantsAndSelf()
            .OfType<HtmlTextNode>()
            .Where(x => x.ParentNode is not null
                && !x.ParentNode.Name.Equals("mark", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (HtmlTextNode textNode in textNodes)
        {
            string text = HtmlEntity.DeEntitize(textNode.Text);
            MatchCollection matches = matcher.Matches(text);
            if (matches.Count == 0)
            {
                continue;
            }

            int position = 0;
            foreach (Match match in matches)
            {
                InsertText(document, textNode, text[position..match.Index]);
                HtmlNode mark = document.CreateElement("mark");
                mark.SetAttributeValue("class", "search-hit");
                mark.AppendChild(document.CreateTextNode(HtmlEntity.Entitize(match.Value)));
                textNode.ParentNode.InsertBefore(mark, textNode);
                position = match.Index + match.Length;
            }
            InsertText(document, textNode, text[position..]);
            textNode.Remove();
        }
    }

    private static void InsertText(HtmlDocument document, HtmlTextNode anchor, string text)
    {
        if (text.Length > 0)
        {
            anchor.ParentNode.InsertBefore(
                document.CreateTextNode(HtmlEntity.Entitize(text)),
                anchor);
        }
    }

    private static void SanitizeAttributes(HtmlNode node)
    {
        foreach (HtmlAttribute attribute in node.Attributes.ToArray())
        {
            bool eventHandler = attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase);
            bool unsafeStyle = attribute.Name.Equals("style", StringComparison.OrdinalIgnoreCase)
                || attribute.Name.Equals("srcdoc", StringComparison.OrdinalIgnoreCase);
            bool unsafeUri = (attribute.Name.Equals("href", StringComparison.OrdinalIgnoreCase)
                    || attribute.Name.Equals("src", StringComparison.OrdinalIgnoreCase))
                && !IsSafeUri(attribute.Value);
            if (eventHandler || unsafeStyle || unsafeUri)
            {
                node.Attributes.Remove(attribute);
            }
        }
    }

    private static bool IsSafeUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.StartsWith("/", StringComparison.Ordinal)
            || value.StartsWith("#", StringComparison.Ordinal))
        {
            return true;
        }
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && uri.Scheme is "http" or "https" or "mailto";
    }
}
