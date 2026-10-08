using System.ComponentModel;
using LocalWeb.Mcp.Fetch;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace LocalWeb.Mcp.Actions;

/// <summary>
/// MCP tool: render a URL in a headless browser and return the main content as
/// Markdown. Skips the HTTP path entirely; use it for JavaScript-heavy pages.
/// </summary>
[McpServerToolType]
public sealed class WebRenderAction
{
    private readonly PageFetcher _fetcher;
    private readonly ILogger<WebRenderAction> _logger;

    public WebRenderAction(PageFetcher fetcher, ILogger<WebRenderAction> logger)
    {
        _fetcher = fetcher;
        _logger = logger;
    }

    [McpServerTool(Name = "web_render", Title = "Render page", ReadOnly = true, OpenWorld = true)]
    [Description("Render a URL in a headless browser and return the main content as Markdown. Use for JavaScript-heavy pages.")]
    public async Task<string> WebRenderAsync(
        [Description("Absolute URL to render.")] string url,
        [Description("Skip the cache and render the page again.")] bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return OutputFormatter.FormatError($"'{url}' is not an absolute URL.");
        }

        try
        {
            var outcome = await _fetcher.FetchAsync(uri, forceBrowser: true, bypassCache, cancellationToken)
                .ConfigureAwait(false);
            return OutputFormatter.FormatFetch(outcome);
        }
        catch (LocalWebException ex)
        {
            _logger.LogWarning(ex, "web_render failed for {Url}.", url);
            return OutputFormatter.FormatError(ex.Message);
        }
    }
}
