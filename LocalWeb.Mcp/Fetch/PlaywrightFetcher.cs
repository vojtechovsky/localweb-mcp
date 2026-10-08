using LocalWeb.Mcp.Options;
using LocalWeb.Mcp.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace LocalWeb.Mcp.Fetch;

/// <summary>
/// Renders pages in a single shared headless Chromium instance. Each request
/// gets its own browser context (closed afterwards) and the number of
/// concurrently open pages is capped by <c>MaxConcurrentBrowserPages</c>.
///
/// Every subresource request is validated through <see cref="UrlGuard"/> and
/// blocked if it targets a forbidden address. Images, media and fonts are
/// blocked outright for speed; they are not needed for text extraction.
/// </summary>
public sealed class PlaywrightFetcher : IAsyncDisposable
{
    private readonly LocalWebOptions _options;
    private readonly UrlGuard _guard;
    private readonly ILogger<PlaywrightFetcher> _logger;
    private readonly SemaphoreSlim _pageLimit;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private bool _disposed;

    public PlaywrightFetcher(IOptions<LocalWebOptions> options, UrlGuard guard, ILogger<PlaywrightFetcher> logger)
    {
        _options = options.Value;
        _guard = guard;
        _logger = logger;
        _pageLimit = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentBrowserPages));
    }

    public async Task<RenderedPage> RenderAsync(Uri url, CancellationToken cancellationToken)
    {
        await _guard.ValidateAsync(url, cancellationToken).ConfigureAwait(false);
        await _pageLimit.WaitAsync(cancellationToken).ConfigureAwait(false);

        IBrowserContext? context = null;
        try
        {
            var browser = await GetBrowserAsync(cancellationToken).ConfigureAwait(false);
            context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = _options.UserAgent,
                JavaScriptEnabled = true,
            }).ConfigureAwait(false);

            var page = await context.NewPageAsync().ConfigureAwait(false);
            await page.RouteAsync("**/*", HandleRouteAsync).ConfigureAwait(false);

            var timeout = _options.BrowserTimeoutSeconds * 1000;
            try
            {
                await page.GotoAsync(url.ToString(), new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = timeout,
                }).ConfigureAwait(false);
            }
            catch (PlaywrightException ex)
            {
                _logger.LogDebug(ex, "Navigation to {Url} did not reach network idle; using loaded DOM.", url);
            }

            var html = await page.ContentAsync().ConfigureAwait(false);
            return new RenderedPage(page.Url, html);
        }
        finally
        {
            if (context is not null)
            {
                await context.DisposeAsync().ConfigureAwait(false);
            }

            _pageLimit.Release();
        }
    }

    private async Task HandleRouteAsync(IRoute route)
    {
        try
        {
            var request = route.Request;

            if (IsBlockedResourceType(request.ResourceType))
            {
                await route.AbortAsync().ConfigureAwait(false);
                return;
            }

            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
            {
                await route.AbortAsync().ConfigureAwait(false);
                return;
            }

            if (uri.Scheme is "http" or "https")
            {
                try
                {
                    await _guard.ValidateAsync(uri, CancellationToken.None).ConfigureAwait(false);
                }
                catch (UrlGuardException)
                {
                    _logger.LogDebug("Blocked browser request to {Url}.", uri);
                    await route.AbortAsync().ConfigureAwait(false);
                    return;
                }
            }
            else if (uri.Scheme is not ("data" or "blob" or "about"))
            {
                await route.AbortAsync().ConfigureAwait(false);
                return;
            }

            await route.ContinueAsync().ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            // The page or route was torn down while the handler ran; nothing to do.
        }
    }

    private static bool IsBlockedResourceType(string resourceType) => resourceType is
        "image" or "media" or "font";

    private async Task<IBrowser> GetBrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is { IsConnected: true })
        {
            return _browser;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_browser is { IsConnected: true })
            {
                return _browser;
            }

            _playwright ??= await Playwright.CreateAsync().ConfigureAwait(false);
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = ["--no-sandbox", "--disable-dev-shm-usage"],
            }).ConfigureAwait(false);

            _logger.LogInformation("Launched headless Chromium.");
            return _browser;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_browser is not null)
        {
            await _browser.DisposeAsync().ConfigureAwait(false);
        }

        _playwright?.Dispose();
        _pageLimit.Dispose();
        _initLock.Dispose();
    }
}
