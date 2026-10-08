using System.ComponentModel;
using System.Text.Json;
using LocalWeb.Mcp.Cache;
using LocalWeb.Mcp.Options;
using LocalWeb.Mcp.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace LocalWeb.Mcp.Actions;

/// <summary>MCP tool: search the web through the local SearXNG instance.</summary>
[McpServerToolType]
public sealed class WebSearchAction
{
    private readonly SearxngClient _searxng;
    private readonly SqliteCache _cache;
    private readonly LocalWebOptions _options;
    private readonly ILogger<WebSearchAction> _logger;

    public WebSearchAction(
        SearxngClient searxng,
        SqliteCache cache,
        IOptions<LocalWebOptions> options,
        ILogger<WebSearchAction> logger)
    {
        _searxng = searxng;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    [McpServerTool(Name = "web_search", Title = "Web search", ReadOnly = true, OpenWorld = true)]
    [Description("Search the web via the local SearXNG instance. Returns titles, URLs and snippets.")]
    public async Task<string> WebSearchAsync(
        [Description("Search query.")] string query,
        [Description("Maximum number of results, 1-20.")] int maxResults = 8,
        [Description("Language code such as cs or en, or 'auto'.")] string language = "auto",
        [Description("Page number, 1-based.")] int page = 1,
        [Description("Skip the cache and query SearXNG again.")] bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return OutputFormatter.FormatError("the search query must not be empty.");
        }

        maxResults = Math.Clamp(maxResults, 1, _options.MaxMaxResults);
        if (page < 1)
        {
            page = 1;
        }

        var cacheKey = CacheKeys.Search(query, language, page, maxResults);

        if (!bypassCache)
        {
            var cached = await _cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                var entry = JsonSerializer.Deserialize<CachedSearch>(cached);
                if (entry is { IsError: true })
                {
                    return entry.Error!;
                }

                if (entry?.Text is not null)
                {
                    return entry.Text;
                }
            }
        }

        try
        {
            var response = await _searxng.SearchAsync(query, maxResults, language, page, cancellationToken)
                .ConfigureAwait(false);
            var text = OutputFormatter.FormatSearch(query, response);

            await _cache.SetAsync(
                    cacheKey,
                    JsonSerializer.Serialize(new CachedSearch(false, null, text)),
                    TimeSpan.FromMinutes(_options.SearchTtlMinutes),
                    cancellationToken)
                .ConfigureAwait(false);

            return text;
        }
        catch (LocalWebException ex)
        {
            _logger.LogWarning(ex, "web_search failed for {Query}.", query);
            var text = OutputFormatter.FormatError(ex.Message);

            await _cache.SetAsync(
                    cacheKey,
                    JsonSerializer.Serialize(new CachedSearch(true, text, null)),
                    TimeSpan.FromMinutes(_options.ErrorTtlMinutes),
                    CancellationToken.None)
                .ConfigureAwait(false);

            return text;
        }
    }

    private sealed record CachedSearch(bool IsError, string? Error, string? Text);
}
