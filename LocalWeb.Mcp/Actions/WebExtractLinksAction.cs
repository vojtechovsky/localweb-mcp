using System.ComponentModel;
using LocalWeb.Mcp.Fetch;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace LocalWeb.Mcp.Actions;

/// <summary>
/// MCP tool: fetch a URL and return the http(s) links found on the page. Uses
/// the same HTTP-first / browser-fallback pipeline as <c>web_fetch</c>.
/// </summary>
[McpServerToolType]
public sealed class WebExtractLinksAction
{
    private readonly PageFetcher _fetcher;
    private readonly LinkExtractor _extractor;
    private readonly ILogger<WebExtractLinksAction> _logger;

    public WebExtractLinksAction(
        PageFetcher fetcher,
        LinkExtractor extractor,
        ILogger<WebExtractLinksAction> logger)
    {
        _fetcher = fetcher;
        _extractor = extractor;
        _logger = logger;
    }

    [McpServerTool(Name = "web_extract_links", Title = "Extract links", ReadOnly = true, OpenWorld = true)]
    [Description("Fetch a URL and return the http(s) links found on the page as Markdown. Uses HTTP first, with a browser fallback.")]
    public async Task<string> ExtractLinksAsync(
        [Description("Absolute URL to inspect.")] string url,
        [Description("Maximum number of links to return, 1-200.")] int maxLinks = 50,
        [Description("Skip the cache and fetch the page again.")] bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return OutputFormatter.FormatError($"'{url}' is not an absolute URL.");
        }

        maxLinks = Math.Clamp(maxLinks, 1, 200);

        try
        {
            var page = await _fetcher.FetchRawAsync(uri, bypassCache, cancellationToken).ConfigureAwait(false);
            var links = _extractor.Extract(page.Html, page.Url, maxLinks);
            return OutputFormatter.FormatLinks(page.Title, page.Url, page.Source, links);
        }
        catch (LocalWebException ex)
        {
            _logger.LogWarning(ex, "web_extract_links failed for {Url}.", url);
            return OutputFormatter.FormatError(ex.Message);
        }
    }
}
