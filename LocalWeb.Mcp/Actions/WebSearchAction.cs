using System.ComponentModel;
using System.Text.Json;
using LocalWeb.Mcp.Cache;
using LocalWeb.Mcp.Options;
using LocalWeb.Mcp.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
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

    /// <summary>Initializes the action.</summary>
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

    /// <summary>Search handler exposed as the <c>web_search</c> MCP tool.</summary>
    [McpServerTool(Name = "web_search", Title = "Web search", ReadOnly = true, OpenWorld = true)]
    [Description("Search the web via the local SearXNG instance. Returns titles, URLs and snippets.")]
    public async Task<string> WebSearchAsync(
        [Description("Search query.")] string query,
        [Description("Maximum number of results; 0 uses the configured default.")] int maxResults = 0,
        [Description("Language code such as cs or en, or 'auto'.")] string language = "auto",
        [Description("Page number, 1-based.")] int page = 1,
        [Description("Skip the cache and query SearXNG again.")] bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new McpException("The search query must not be empty.");
        }

        if (maxResults <= 0)
        {
            maxResults = _options.DefaultMaxResults;
        }

        maxResults = Math.Clamp(maxResults, 1, _options.MaxMaxResults);
        if (page < 1)
        {
            page = 1;
        }

        // The cache key deliberately excludes maxResults so one entry serves every
        // result count; the full set is cached and sliced for each caller.
        var cacheKey = CacheKeys.Search(query, language, page);

        SearchResponse? response = null;

        if (!bypassCache)
        {
            var cached = await _cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                var entry = TryReadCache(cached);
                if (entry is { IsError: true })
                {
                    throw new McpException(entry.Error ?? "Cached error.");
                }

                response = entry?.Response;
            }
        }

        if (response is null)
        {
            try
            {
                response = await _searxng.SearchAsync(query, _options.MaxMaxResults, language, page, cancellationToken)
                    .ConfigureAwait(false);

                await TryCacheAsync(cacheKey, new CachedSearch(false, null, response),
                    TimeSpan.FromMinutes(_options.SearchTtlMinutes), cancellationToken).ConfigureAwait(false);
            }
            catch (LocalWebException ex)
            {
                _logger.LogWarning(ex, "web_search failed for {Query}.", query);
                await TryCacheAsync(cacheKey, new CachedSearch(true, ex.Message, null),
                    TimeSpan.FromMinutes(_options.ErrorTtlMinutes), CancellationToken.None).ConfigureAwait(false);
                throw new McpException(ex.Message);
            }
        }

        return OutputFormatter.FormatSearch(query, Limit(response, maxResults));
    }

    private static SearchResponse Limit(SearchResponse response, int maxResults)
    {
        if (response.Results.Count <= maxResults)
        {
            return response;
        }

        return new SearchResponse
        {
            Query = response.Query,
            NumberOfResults = response.NumberOfResults,
            Results = response.Results.Take(maxResults).ToList(),
            Suggestions = response.Suggestions,
        };
    }

    private CachedSearch? TryReadCache(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<CachedSearch>(value);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Ignoring a corrupt search cache entry.");
            return null;
        }
    }

    private async Task TryCacheAsync(string key, CachedSearch entry, TimeSpan ttl, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.SetAsync(key, JsonSerializer.Serialize(entry), ttl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to write a search cache entry.");
        }
    }

    private sealed record CachedSearch(bool IsError, string? Error, SearchResponse? Response);
}
