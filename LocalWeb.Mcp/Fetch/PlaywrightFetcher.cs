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
/// Requests are intercepted at the context level so popups are covered, service
/// workers and WebSockets are blocked, and images/media/fonts are dropped for
/// speed. The page URL is re-validated after navigation because a redirect can
/// move the page to an address the route handler never saw.
///
/// Known limitation: unlike the HTTP path, the browser cannot be pinned to the
/// exact validated IP — Chromium resolves the host again when it connects.
/// <see cref="UrlGuard"/> therefore acts as a check, not a hard pin, for the
/// browser path.
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

    /// <summary>Initializes the browser fetcher; Chromium is launched lazily.</summary>
    public PlaywrightFetcher(IOptions<LocalWebOptions> options, UrlGuard guard, ILogger<PlaywrightFetcher> logger)
    {
        _options = options.Value;
        _guard = guard;
        _logger = logger;
        _pageLimit = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentBrowserPages));
    }

    /// <summary>Renders a URL in a fresh browser context and returns the HTML.</summary>
    /// <param name="url">Validated absolute URL.</param>
    /// <param name="cancellationToken">Caller cancellation token; closes the context on cancel.</param>
    public async Task<RenderedPage> RenderAsync(Uri url, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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
                AcceptDownloads = false,
                // Service workers can issue requests that route handlers never see.
                ServiceWorkers = ServiceWorkerPolicy.Block,
            }).ConfigureAwait(false);

            // Route at the context level (covers popups); WebSockets bypass routes
            // entirely, so block them outright — they are not needed for content.
            await context.RouteAsync("**/*", HandleRouteAsync).ConfigureAwait(false);
            await context.RouteWebSocketAsync("**/*", route => { _ = route.CloseAsync(); }).ConfigureAwait(false);

            var page = await context.NewPageAsync().ConfigureAwait(false);

            // Playwright APIs take no CancellationToken; close the context if the
            // caller cancels so an in-flight render does not outlive the request.
            await using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    _ = context.CloseAsync();
                }
                catch (PlaywrightException)
                {
                    // Context already gone.
                }
            });

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

            cancellationToken.ThrowIfCancellationRequested();

            // A redirect is not seen by the route handler; re-validate the final URL.
            if (Uri.TryCreate(page.Url, UriKind.Absolute, out var finalUri))
            {
                await _guard.ValidateAsync(finalUri, cancellationToken).ConfigureAwait(false);
            }

            var html = await page.ContentAsync().ConfigureAwait(false);
            if (html.Length > _options.MaxResponseBytes)
            {
                _logger.LogWarning(
                    "Rendered HTML for {Url} exceeded MaxResponseBytes and was truncated.", url);
                html = html[.._options.MaxResponseBytes];
            }

            return new RenderedPage(page.Url, html);
        }
        catch (PlaywrightException ex)
        {
            throw new LocalWebException(
                $"The headless browser could not render {url}: {ex.Message} " +
                "Make sure Chromium is installed (run the server with --install-browser).", ex);
        }
        finally
        {
            try
            {
                if (context is not null)
                {
                    await context.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (PlaywrightException ex)
            {
                _logger.LogDebug(ex, "Failed to dispose the browser context for {Url}.", url);
            }
            finally
            {
                // Release unconditionally, even if context disposal threw.
                _pageLimit.Release();
            }
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
        ObjectDisposedException.ThrowIf(_disposed, this);

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

            if (_browser is not null)
            {
                // The previous browser disconnected; dispose it before replacing it.
                try
                {
                    await _browser.DisposeAsync().ConfigureAwait(false);
                }
                catch (PlaywrightException ex)
                {
                    _logger.LogDebug(ex, "Failed to dispose a disconnected browser.");
                }

                _browser = null;
            }

            _playwright ??= await Playwright.CreateAsync().ConfigureAwait(false);
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                // --no-sandbox is required in many container/CI environments where
                // the Chromium sandbox is unavailable. The process runs as a
                // non-privileged user and only renders untrusted pages.
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

    /// <summary>Drains in-flight renders and shuts the browser down.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Wait for in-flight renders to release their page slots before shutting
        // the browser down, rather than tearing resources down underneath them.
        var permits = Math.Max(1, _options.MaxConcurrentBrowserPages);
        for (var i = 0; i < permits; i++)
        {
            try
            {
                await _pageLimit.WaitAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }

        if (_browser is not null)
        {
            try
            {
                await _browser.DisposeAsync().ConfigureAwait(false);
            }
            catch (PlaywrightException ex)
            {
                _logger.LogDebug(ex, "Failed to dispose the browser during shutdown.");
            }
        }

        _playwright?.Dispose();

        // The semaphores are deliberately left undisposed: the process is exiting
        // and disposing them could throw inside late request interceptors.
    }
}
