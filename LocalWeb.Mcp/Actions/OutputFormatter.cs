using System.Text;
using LocalWeb.Mcp.Fetch;
using LocalWeb.Mcp.Search;

namespace LocalWeb.Mcp.Actions;

/// <summary>
/// Renders tool results as plain Markdown text for the MCP client. All content
/// that originates from the web (titles, snippets, engines, link text and URLs)
/// is attacker-controlled, so it is escaped before being embedded to stop it
/// from injecting Markdown structure or HTML into the model's context.
/// </summary>
public static class OutputFormatter
{
    /// <summary>Formats search results as Markdown.</summary>
    /// <param name="query">The original query.</param>
    /// <param name="response">The SearXNG response.</param>
    public static string FormatSearch(string query, SearchResponse response)
    {
        if (response.Results.Count == 0)
        {
            return $"No results for \"{EscapeInline(query)}\".";
        }

        var builder = new StringBuilder();
        builder.Append("Results for \"").Append(EscapeInline(query)).Append("\": ").Append(response.Results.Count);

        if (response.NumberOfResults > response.Results.Count)
        {
            builder.Append(" (of about ").Append(response.NumberOfResults).Append(')');
        }

        builder.AppendLine().AppendLine();

        for (var i = 0; i < response.Results.Count; i++)
        {
            var result = response.Results[i];
            builder.Append(i + 1).Append(". **").Append(EscapeInline(result.Title)).AppendLine("**");
            builder.Append("   ").AppendLine(SanitizeUrl(result.Url));

            if (!string.IsNullOrWhiteSpace(result.Content))
            {
                builder.Append("   ").AppendLine(EscapeInline(result.Content));
            }

            if (!string.IsNullOrWhiteSpace(result.Engine))
            {
                builder.Append("   _source: ").Append(EscapeInline(result.Engine)).AppendLine("_");
            }

            builder.AppendLine();
        }

        if (response.Suggestions.Count > 0)
        {
            builder.Append("Suggestions: ")
                .AppendLine(string.Join(", ", response.Suggestions.Select(EscapeInline)));
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>Formats a fetched page as Markdown.</summary>
    /// <param name="outcome">The fetch outcome.</param>
    public static string FormatFetch(FetchOutcome outcome)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(outcome.Title))
        {
            builder.Append("# ").AppendLine(EscapeInline(outcome.Title));
        }

        builder.Append("URL: ").AppendLine(SanitizeUrl(outcome.Url));
        builder.Append("Source: ").AppendLine(outcome.Source.ToString().ToLowerInvariant());
        builder.AppendLine();
        builder.Append(outcome.Markdown);

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>Formats extracted links as Markdown.</summary>
    /// <param name="title">Page title, if any.</param>
    /// <param name="url">The page URL.</param>
    /// <param name="source">Where the HTML came from.</param>
    /// <param name="links">The extracted links.</param>
    public static string FormatLinks(string title, string url, FetchSource source, IReadOnlyList<LinkItem> links)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.Append("# ").AppendLine(EscapeInline(title));
        }

        builder.Append("URL: ").AppendLine(SanitizeUrl(url));
        builder.Append("Source: ").AppendLine(source.ToString().ToLowerInvariant());
        builder.Append("Links: ").AppendLine(links.Count.ToString());
        builder.AppendLine();

        if (links.Count == 0)
        {
            builder.AppendLine("No http(s) links found.");
            return builder.ToString().TrimEnd() + Environment.NewLine;
        }

        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            var text = string.IsNullOrWhiteSpace(link.Text) ? SanitizeUrl(link.Url) : EscapeInline(link.Text);
            builder.Append(i + 1).Append(". [").Append(text).Append("](").Append(SanitizeUrl(link.Url)).AppendLine(")");
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>Escapes inline Markdown/HTML metacharacters and collapses newlines.</summary>
    private static string EscapeInline(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\': builder.Append("\\\\"); break;
                case '`': builder.Append("\\`"); break;
                case '*': builder.Append("\\*"); break;
                case '_': builder.Append("\\_"); break;
                case '[': builder.Append("\\["); break;
                case ']': builder.Append("\\]"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '|': builder.Append("\\|"); break;
                case '\r':
                case '\n': builder.Append(' '); break;
                default: builder.Append(ch); break;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Wraps a URL as a Markdown link destination. Angle brackets let the
    /// destination contain parentheses, but not <c>&lt;</c>, <c>&gt;</c> or
    /// newlines, which are removed/encoded.
    /// </summary>
    private static string SanitizeUrl(string url)
    {
        var cleaned = url
            .Replace("<", "%3C")
            .Replace(">", "%3E")
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty);

        return "<" + cleaned + ">";
    }
}
