using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using LocalWeb.Mcp.Cache;
using Microsoft.Extensions.Logging;

namespace LocalWeb.Mcp.Fetch;

/// <summary>
/// Extracts http(s) links from HTML. Relative URLs are resolved against the
/// page URL (honouring a <c>&lt;base href&gt;</c> when present) and duplicate
/// targets are removed while keeping the first anchor text seen.
/// </summary>
public sealed class LinkExtractor
{
    private readonly ILogger<LinkExtractor> _logger;
    private readonly HtmlParser _parser = new();

    /// <summary>Initializes a new link extractor.</summary>
    public LinkExtractor(ILogger<LinkExtractor> logger)
    {
        _logger = logger;
    }

    /// <summary>Extracts unique http(s) links from HTML.</summary>
    /// <param name="html">Raw HTML.</param>
    /// <param name="pageUrl">The page URL, used to resolve relative links.</param>
    /// <param name="maxLinks">Maximum number of links to return.</param>
    public IReadOnlyList<LinkItem> Extract(string html, string pageUrl, int maxLinks)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        IDocument document;
        try
        {
            document = _parser.ParseDocument(html);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse HTML for link extraction.");
            return [];
        }

        var baseUri = ResolveBaseUri(document, pageUrl);
        // Scheme/host are case-insensitive but the path and query are not, so
        // normalize the former (NormalizeUrl) and compare case-sensitively.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var links = new List<LinkItem>();

        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#'))
            {
                continue;
            }

            if (!Uri.TryCreate(baseUri, href, out var absolute) ||
                absolute.Scheme is not ("http" or "https"))
            {
                continue;
            }

            var target = absolute.ToString();
            if (!seen.Add(CacheKeys.NormalizeUrl(target)))
            {
                continue;
            }

            links.Add(new LinkItem(Normalize(anchor.TextContent), target));
            if (links.Count >= maxLinks)
            {
                break;
            }
        }

        return links;
    }

    private static Uri? ResolveBaseUri(IDocument document, string pageUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var page))
        {
            return null;
        }

        var baseHref = document.QuerySelector("base[href]")?.GetAttribute("href");
        if (!string.IsNullOrWhiteSpace(baseHref) &&
            Uri.TryCreate(page, baseHref, out var resolved))
        {
            return resolved;
        }

        return page;
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
