using System.Text.Json;
using LocalWeb.Mcp.Cache;
using LocalWeb.Mcp.Options;
using LocalWeb.Mcp.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalWeb.Mcp.Fetch;

/// <summary>
/// Orchestrates the fetch pipeline shared by the <c>web_fetch</c> and
/// <c>web_render</c> tools: validate, consult the cache, try HTTP, fall back to
/// the browser, then cache the result.
/// </summary>
public sealed class PageFetcher
{
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
                var entry = JsonSerializer.Deserialize<CachedFetch>(cached, JsonOptions);
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

            await _cache.SetAsync(
                    cacheKey,
                    JsonSerializer.Serialize(new CachedFetch(false, null, outcome), JsonOptions),
                    TimeSpan.FromMinutes(_options.FetchTtlMinutes),
                    cancellationToken)
                .ConfigureAwait(false);

            return outcome;
        }
        catch (LocalWebException ex)
        {
            await _cache.SetAsync(
                    cacheKey,
                    JsonSerializer.Serialize(new CachedFetch(true, ex.Message, null), JsonOptions),
                    TimeSpan.FromMinutes(_options.ErrorTtlMinutes),
                    CancellationToken.None)
                .ConfigureAwait(false);
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
                var entry = JsonSerializer.Deserialize<CachedRaw>(cached, JsonOptions);
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

            await _cache.SetAsync(
                    cacheKey,
                    JsonSerializer.Serialize(new CachedRaw(false, null, page), JsonOptions),
                    TimeSpan.FromMinutes(_options.FetchTtlMinutes),
                    cancellationToken)
                .ConfigureAwait(false);

            return page;
        }
        catch (LocalWebException ex)
        {
            await _cache.SetAsync(
                    cacheKey,
                    JsonSerializer.Serialize(new CachedRaw(true, ex.Message, null), JsonOptions),
                    TimeSpan.FromMinutes(_options.ErrorTtlMinutes),
                    CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task<RawPage> FetchRawInternalAsync(Uri url, CancellationToken cancellationToken)
    {
        var response = await _http.FetchAsync(url, cancellationToken).ConfigureAwait(false);
        var finalUrl = response.FinalUri.ToString();

        if (response.ContentType == "text/plain")
        {
            return new RawPage(string.Empty, finalUrl, response.Body, FetchSource.Http);
        }

        var extracted = _extractor.Extract(finalUrl, response.Body);
        if (extracted.TextLength == 0 || _extractor.NeedsBrowser(response.Body, extracted))
        {
            _logger.LogDebug("HTTP fetch of {Url} was not sufficient; rendering in the browser.", url);
            return await RenderRawAsync(response.FinalUri, cancellationToken).ConfigureAwait(false);
        }

        return new RawPage(extracted.Title, finalUrl, response.Body, FetchSource.Http);
    }

    private async Task<RawPage> RenderRawAsync(Uri url, CancellationToken cancellationToken)
    {
        var page = await _browser.RenderAsync(url, cancellationToken).ConfigureAwait(false);
        var extracted = _extractor.Extract(page.FinalUrl, page.Html);
        return new RawPage(extracted.Title, page.FinalUrl, page.Html, FetchSource.Browser);
    }

    private async Task<FetchOutcome> FetchAutoAsync(Uri url, CancellationToken cancellationToken)
    {
        var response = await _http.FetchAsync(url, cancellationToken).ConfigureAwait(false);
        var finalUrl = response.FinalUri.ToString();

        var extracted = response.ContentType == "text/plain"
            ? _extractor.ExtractPlainText(string.Empty, response.Body)
            : _extractor.Extract(finalUrl, response.Body);

        if (extracted.TextLength == 0 || _extractor.NeedsBrowser(response.Body, extracted))
        {
            _logger.LogDebug("HTTP fetch of {Url} was not sufficient; falling back to browser.", url);
            return await FetchWithBrowserAsync(response.FinalUri, cancellationToken).ConfigureAwait(false);
        }

        return new FetchOutcome(
            extracted.Title,
            finalUrl,
            FetchSource.Http,
            Truncate(extracted.Markdown));
    }

    private async Task<FetchOutcome> FetchWithBrowserAsync(Uri url, CancellationToken cancellationToken)
    {
        var page = await _browser.RenderAsync(url, cancellationToken).ConfigureAwait(false);
        var extracted = _extractor.Extract(page.FinalUrl, page.Html);

        if (string.IsNullOrWhiteSpace(extracted.Markdown))
        {
            throw new LocalWebException($"The browser could not extract any readable content from {url}.");
        }

        return new FetchOutcome(
            extracted.Title,
            page.FinalUrl,
            FetchSource.Browser,
            Truncate(extracted.Markdown));
    }

    private string Truncate(string markdown)
    {
        if (markdown.Length <= _options.MaxOutputChars)
        {
            return markdown;
        }

        return markdown[.._options.MaxOutputChars] + "\n\n[content truncated]";
    }

    private sealed record CachedFetch(bool IsError, string? Error, FetchOutcome? Outcome);

    private sealed record CachedRaw(bool IsError, string? Error, RawPage? Page);
}
