using System.Text.Json;
using LocalWeb.Mcp.Cache;
using LocalWeb.Mcp.Options;
using LocalWeb.Mcp.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalWeb.Mcp.Fetch;

/// <summary>
/// Orchestrates the fetch pipeline shared by <c>web_fetch</c>, <c>web_render</c>
/// and <c>web_extract_links</c>: validate, consult the cache, try HTTP, fall back
/// to the browser, then cache the result. A corrupt cache entry is treated as a
/// miss, and a failed cache write never masks the real result.
/// </summary>
public sealed class PageFetcher
{
    private const string PlainTextMediaType = "text/plain";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpFetcher _http;
    private readonly PlaywrightFetcher _browser;
    private readonly ContentExtractor _extractor;
    private readonly SqliteCache _cache;
    private readonly UrlGuard _guard;
    private readonly LocalWebOptions _options;
    private readonly ILogger<PageFetcher> _logger;

    /// <summary>Initializes the fetch pipeline.</summary>
    public PageFetcher(
        HttpFetcher http,
        PlaywrightFetcher browser,
        ContentExtractor extractor,
        SqliteCache cache,
        UrlGuard guard,
        IOptions<LocalWebOptions> options,
        ILogger<PageFetcher> logger)
    {
        _http = http;
        _browser = browser;
        _extractor = extractor;
        _cache = cache;
        _guard = guard;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Fetches a page and returns its extracted content as Markdown.</summary>
    /// <param name="url">Validated absolute URL.</param>
    /// <param name="forceBrowser">Skip the HTTP path and render in the browser.</param>
    /// <param name="bypassCache">Ignore any cached result.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    public async Task<FetchOutcome> FetchAsync(
        Uri url,
        bool forceBrowser,
        bool bypassCache,
        CancellationToken cancellationToken)
    {
        // Step 1: validate before touching the cache, so a bad URL always fails.
        await _guard.ValidateAsync(url, cancellationToken).ConfigureAwait(false);

        var mode = forceBrowser ? "browser" : "auto";
        var cacheKey = CacheKeys.Fetch(url.ToString(), mode);

        if (!bypassCache)
        {
            var cached = await _cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                var entry = TryReadCache<CachedFetch>(cached);
                if (entry is { IsError: true })
                {
                    throw new LocalWebException(entry.Error ?? "Cached error.");
                }

                if (entry?.Outcome is not null)
                {
                    return entry.Outcome with { Source = FetchSource.Cache };
                }
            }
        }

        try
        {
            var outcome = forceBrowser
                ? await FetchWithBrowserAsync(url, cancellationToken).ConfigureAwait(false)
                : await FetchAutoAsync(url, cancellationToken).ConfigureAwait(false);

            await TryCacheAsync(cacheKey, new CachedFetch(false, null, outcome),
                TimeSpan.FromMinutes(_options.FetchTtlMinutes), cancellationToken).ConfigureAwait(false);

            return outcome;
        }
        catch (LocalWebException ex)
        {
            await TryCacheAsync(cacheKey, new CachedFetch(true, ex.Message, null),
                TimeSpan.FromMinutes(_options.ErrorTtlMinutes), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Fetch a page and return its raw HTML. Used by tools that need the markup
    /// rather than the extracted article (for example link extraction).
    /// </summary>
    public async Task<RawPage> FetchRawAsync(Uri url, bool bypassCache, CancellationToken cancellationToken)
    {
        await _guard.ValidateAsync(url, cancellationToken).ConfigureAwait(false);

        var cacheKey = CacheKeys.Fetch(url.ToString(), "html");

        if (!bypassCache)
        {
            var cached = await _cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                var entry = TryReadCache<CachedRaw>(cached);
                if (entry is { IsError: true })
                {
                    throw new LocalWebException(entry.Error ?? "Cached error.");
                }

                if (entry?.Page is not null)
                {
                    return entry.Page with { Source = FetchSource.Cache };
                }
            }
        }

        try
        {
            var page = await FetchRawInternalAsync(url, cancellationToken).ConfigureAwait(false);

            await TryCacheAsync(cacheKey, new CachedRaw(false, null, page),
                TimeSpan.FromMinutes(_options.FetchTtlMinutes), cancellationToken).ConfigureAwait(false);

            return page;
        }
        catch (LocalWebException ex)
        {
            await TryCacheAsync(cacheKey, new CachedRaw(true, ex.Message, null),
                TimeSpan.FromMinutes(_options.ErrorTtlMinutes), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<RawPage> FetchRawInternalAsync(Uri url, CancellationToken cancellationToken)
    {
        var response = await _http.FetchAsync(url, cancellationToken).ConfigureAwait(false);
        var finalUrl = response.FinalUri.ToString();

        if (response.Truncated)
        {
            _logger.LogWarning("The response from {Url} exceeded MaxResponseBytes and was truncated.", url);
        }

        if (response.ContentType == PlainTextMediaType)
        {
            // Plain text is already usable; never invoke the browser for it.
            return new RawPage(string.Empty, finalUrl, response.Body, FetchSource.Http);
        }

        var extracted = _extractor.Extract(finalUrl, response.Body);
        if (_extractor.NeedsBrowser(response.Body, extracted))
        {
            _logger.LogDebug("HTTP fetch of {Url} was not sufficient; rendering in the browser.", url);
            try
            {
                return await RenderRawAsync(response.FinalUri, cancellationToken).ConfigureAwait(false);
            }
            catch (LocalWebException ex)
            {
                _logger.LogWarning(ex, "Browser render failed for {Url}; returning the HTTP HTML.", url);
            }
        }

        return new RawPage(extracted.Title, finalUrl, response.Body, FetchSource.Http);
    }

    private async Task<RawPage> RenderRawAsync(Uri url, CancellationToken cancellationToken)
    {
        var resolved = await _http.ResolveRedirectsAsync(url, cancellationToken).ConfigureAwait(false);
        var page = await _browser.RenderAsync(resolved, cancellationToken).ConfigureAwait(false);
        var extracted = _extractor.Extract(page.FinalUrl, page.Html);
        return new RawPage(extracted.Title, page.FinalUrl, page.Html, FetchSource.Browser);
    }

    private async Task<FetchOutcome> FetchAutoAsync(Uri url, CancellationToken cancellationToken)
    {
        var response = await _http.FetchAsync(url, cancellationToken).ConfigureAwait(false);
        var finalUrl = response.FinalUri.ToString();

        if (response.ContentType == PlainTextMediaType)
        {
            var text = _extractor.ExtractPlainText(string.Empty, response.Body);
            return new FetchOutcome(string.Empty, finalUrl, FetchSource.Http, Truncate(text.Markdown, response.Truncated));
        }

        var extracted = _extractor.Extract(finalUrl, response.Body);
        if (_extractor.NeedsBrowser(response.Body, extracted))
        {
            _logger.LogDebug("HTTP fetch of {Url} was not sufficient; falling back to browser.", url);
            try
            {
                return await FetchWithBrowserAsync(response.FinalUri, cancellationToken).ConfigureAwait(false);
            }
            catch (LocalWebException ex)
            {
                // Keep the usable (if partial) HTTP result rather than failing.
                _logger.LogWarning(ex, "Browser fallback failed for {Url}; returning the HTTP result.", url);
            }
        }

        return new FetchOutcome(extracted.Title, finalUrl, FetchSource.Http, Truncate(extracted.Markdown, response.Truncated));
    }

    private async Task<FetchOutcome> FetchWithBrowserAsync(Uri url, CancellationToken cancellationToken)
    {
        // Resolve redirects first (validated, IP-pinned) because the browser's own
        // redirect hops are invisible to the request router.
        var resolved = await _http.ResolveRedirectsAsync(url, cancellationToken).ConfigureAwait(false);
        var page = await _browser.RenderAsync(resolved, cancellationToken).ConfigureAwait(false);
        var extracted = _extractor.Extract(page.FinalUrl, page.Html);

        if (string.IsNullOrWhiteSpace(extracted.Markdown))
        {
            throw new LocalWebException($"The browser could not extract any readable content from {url}.");
        }

        return new FetchOutcome(extracted.Title, page.FinalUrl, FetchSource.Browser, Truncate(extracted.Markdown));
    }

    private T? TryReadCache<T>(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(value, JsonOptions);
        }
        catch (JsonException ex)
        {
            // A corrupt or legacy cache row must not fail the tool; treat it as a miss.
            _logger.LogWarning(ex, "Ignoring a corrupt cache entry.");
            return default;
        }
    }

    private async Task TryCacheAsync<T>(string key, T entry, TimeSpan ttl, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.SetAsync(key, JsonSerializer.Serialize(entry, JsonOptions), ttl, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A cache write failure must never mask the real result.
            _logger.LogWarning(ex, "Failed to write a cache entry.");
        }
    }

    private string Truncate(string markdown, bool sourceTruncated = false)
    {
        var result = markdown;
        if (result.Length > _options.MaxOutputChars)
        {
            result = result[.._options.MaxOutputChars] + "\n\n[content truncated]";
        }

        if (sourceTruncated)
        {
            result += "\n\n[source truncated at MaxResponseBytes]";
        }

        return result;
    }

    private sealed record CachedFetch(bool IsError, string? Error, FetchOutcome? Outcome);

    private sealed record CachedRaw(bool IsError, string? Error, RawPage? Page);
}
