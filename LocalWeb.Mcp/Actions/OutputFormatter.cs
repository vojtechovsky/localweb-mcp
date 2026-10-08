using System.Text;
using LocalWeb.Mcp.Fetch;
using LocalWeb.Mcp.Search;

namespace LocalWeb.Mcp.Actions;

/// <summary>Renders tool results as plain Markdown text for the MCP client.</summary>
public static class OutputFormatter
{
    public static string FormatSearch(string query, SearchResponse response)
    {
        if (response.Results.Count == 0)
        {
            return $"No results for \"{query}\".";
        }

        var builder = new StringBuilder();
        builder.Append("Results for \"").Append(query).Append("\": ").Append(response.Results.Count);

        if (response.NumberOfResults > response.Results.Count)
        {
            builder.Append(" (of about ").Append(response.NumberOfResults).Append(')');
        }

        builder.AppendLine().AppendLine();

        for (var i = 0; i < response.Results.Count; i++)
        {
            var result = response.Results[i];
            builder.Append(i + 1).Append(". **").Append(result.Title).AppendLine("**");
            builder.Append("   ").AppendLine(result.Url);

            if (!string.IsNullOrWhiteSpace(result.Content))
            {
                builder.Append("   ").AppendLine(result.Content.Trim());
            }

            if (!string.IsNullOrWhiteSpace(result.Engine))
            {
                builder.Append("   _source: ").Append(result.Engine).AppendLine("_");
            }

            builder.AppendLine();
        }

        if (response.Suggestions.Count > 0)
        {
            builder.Append("Suggestions: ").AppendLine(string.Join(", ", response.Suggestions));
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string FormatFetch(FetchOutcome outcome)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(outcome.Title))
        {
            builder.Append("# ").AppendLine(outcome.Title.Trim());
        }

        builder.Append("URL: ").AppendLine(outcome.Url);
        builder.Append("Source: ").AppendLine(outcome.Source.ToString().ToLowerInvariant());
        builder.AppendLine();
        builder.Append(outcome.Markdown);

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string FormatError(string message) => $"Error: {message}";

    public static string FormatLinks(string title, string url, FetchSource source, IReadOnlyList<LinkItem> links)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.Append("# ").AppendLine(title.Trim());
        }

        builder.Append("URL: ").AppendLine(url);
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
            var text = string.IsNullOrWhiteSpace(link.Text) ? link.Url : EscapeLinkText(link.Text);
            builder.Append(i + 1).Append(". [").Append(text).Append("](").Append(link.Url).AppendLine(")");
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string EscapeLinkText(string text) =>
        text.Replace("[", "\\[").Replace("]", "\\]");
}
