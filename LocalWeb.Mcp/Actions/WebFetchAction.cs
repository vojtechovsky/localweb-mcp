using System.ComponentModel;
using LocalWeb.Mcp.Fetch;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LocalWeb.Mcp.Actions;

/// <summary>
/// MCP tool: fetch a URL and return the main content as Markdown. Uses plain
/// HTTP first and automatically falls back to a headless browser when the page
/// needs JavaScript.
/// </summary>
[McpServerToolType]
public sealed class WebFetchAction
{
    private readonly PageFetcher _fetcher;
    private readonly ILogger<WebFetchAction> _logger;

    public WebFetchAction(PageFetcher fetcher, ILogger<WebFetchAction> logger)
    {
        _fetcher = fetcher;
        _logger = logger;
    }

    [McpServerTool(Name = "web_fetch", Title = "Fetch page", ReadOnly = true, OpenWorld = true)]
    [Description("Fetch a URL and return its main content as Markdown. Uses HTTP first, with a browser fallback for JavaScript-heavy pages.")]
    public async Task<string> WebFetchAsync(
        [Description("Absolute URL to fetch.")] string url,
        [Description("Skip the cache and fetch the page again.")] bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new McpException($"'{url}' is not an absolute URL.");
        }

        try
        {
            var outcome = await _fetcher.FetchAsync(uri, forceBrowser: false, bypassCache, cancellationToken)
                .ConfigureAwait(false);
            return OutputFormatter.FormatFetch(outcome);
        }
        catch (LocalWebException ex)
        {
            _logger.LogWarning(ex, "web_fetch failed for {Url}.", url);
            throw new McpException(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "web_fetch failed unexpectedly for {Url}.", url);
            throw new McpException($"Failed to fetch '{url}'.");
        }
    }
}
