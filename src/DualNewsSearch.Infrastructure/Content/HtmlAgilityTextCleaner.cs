using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DualNewsSearch.Application.Configuration;
using DualNewsSearch.Application.Contracts;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;

namespace DualNewsSearch.Infrastructure.Content;

public sealed class HtmlAgilityTextCleaner : IHtmlTextCleaner
{
    private const char TableSeparator = '\u001e';
    private static readonly HashSet<string> RemovedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "iframe", "object", "embed"
    };

    private static readonly HashSet<string> LineElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "br", "h1", "h2", "h3", "h4", "h5", "h6", "li", "tr"
    };

    private static readonly Regex HorizontalWhitespace =
        new(@"[\p{Zs}\t\f\v]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExcessNewlines =
        new(@"\n{3,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly int _maxLength;

    public HtmlAgilityTextCleaner(IOptions<IndexingOptions> options)
    {
        _maxLength = options.Value.HtmlMaxLength;
    }

    public HtmlCleanResult Clean(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return new HtmlCleanResult(string.Empty, false);
        }

        try
        {
            var document = new HtmlDocument
            {
                OptionFixNestedTags = true,
                OptionAutoCloseOnEnd = true
            };
            document.LoadHtml(html);

            foreach (HtmlNode node in document.DocumentNode
                         .Descendants()
                         .Where(x => RemovedElements.Contains(x.Name)
                             || x.NodeType == HtmlNodeType.Comment
                             || IsHidden(x))
                         .ToArray())
            {
                node.Remove();
            }

            var builder = new StringBuilder(Math.Min(html.Length, _maxLength + 1));
            AppendNode(document.DocumentNode, builder);
            string text = DecodeEntitiesTwice(builder.ToString());
            text = NormalizeWhitespace(text);
            bool truncated = text.Length > _maxLength;
            if (truncated)
            {
                int length = _maxLength;
                if (length > 0
                    && length < text.Length
                    && char.IsHighSurrogate(text[length - 1])
                    && char.IsLowSurrogate(text[length]))
                {
                    length--;
                }

                text = text[..length].TrimEnd();
            }

            return new HtmlCleanResult(text, truncated);
        }
        catch
        {
            string fallback = NormalizeWhitespace(DecodeEntitiesTwice(html));
            int length = Math.Min(fallback.Length, _maxLength);
            if (length > 0
                && length < fallback.Length
                && char.IsHighSurrogate(fallback[length - 1])
                && char.IsLowSurrogate(fallback[length]))
            {
                length--;
            }

            return new HtmlCleanResult(fallback[..length], fallback.Length > length);
        }
    }

    private static void AppendNode(HtmlNode node, StringBuilder builder)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            string text = ((HtmlTextNode)node).Text;
            if (string.IsNullOrWhiteSpace(text)
                && (text.Contains('\r', StringComparison.Ordinal)
                    || text.Contains('\n', StringComparison.Ordinal)))
            {
                return;
            }

            builder.Append(text);
            return;
        }

        if (node.Name.Equals("br", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append('\n');
            return;
        }

        bool lineElement = LineElements.Contains(node.Name);
        bool tableCell = node.Name.Equals("td", StringComparison.OrdinalIgnoreCase)
            || node.Name.Equals("th", StringComparison.OrdinalIgnoreCase);

        if (lineElement)
        {
            AppendSeparator(builder, '\n');
        }

        foreach (HtmlNode child in node.ChildNodes)
        {
            AppendNode(child, builder);
        }

        if (tableCell)
        {
            AppendSeparator(builder, TableSeparator);
        }
        else if (lineElement)
        {
            AppendSeparator(builder, '\n');
        }
    }

    private static void AppendSeparator(StringBuilder builder, char separator)
    {
        if (builder.Length == 0 || builder[^1] == separator)
        {
            return;
        }

        builder.Append(separator);
    }

    private static string DecodeEntitiesTwice(string value)
    {
        string once = WebUtility.HtmlDecode(value);
        string twice = WebUtility.HtmlDecode(once);
        return twice;
    }

    private static string NormalizeWhitespace(string value)
    {
        string normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\u00a0', ' ');
        normalized = HorizontalWhitespace.Replace(normalized, " ");
        normalized = normalized.Replace(TableSeparator, '\t');
        string[] lines = normalized
            .Split('\n')
            .Select(x => x.Trim())
            .ToArray();
        normalized = string.Join('\n', lines).Trim();
        return ExcessNewlines.Replace(normalized, "\n\n");
    }

    private static bool IsHidden(HtmlNode node)
    {
        string style = node.GetAttributeValue("style", string.Empty);
        string hidden = node.GetAttributeValue("hidden", string.Empty);
        string ariaHidden = node.GetAttributeValue("aria-hidden", string.Empty);
        return node.Attributes["hidden"] is not null
            || hidden.Equals("hidden", StringComparison.OrdinalIgnoreCase)
            || ariaHidden.Equals("true", StringComparison.OrdinalIgnoreCase)
            || style.Contains("display:none", StringComparison.OrdinalIgnoreCase)
            || style.Contains("display: none", StringComparison.OrdinalIgnoreCase)
            || style.Contains("visibility:hidden", StringComparison.OrdinalIgnoreCase)
            || style.Contains("visibility: hidden", StringComparison.OrdinalIgnoreCase);
    }
}
