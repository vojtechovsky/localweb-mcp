using LocalWeb.Mcp.Cache;
using LocalWeb.Mcp.Composition;
using LocalWeb.Mcp.Fetch;
using LocalWeb.Mcp.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalWeb.Mcp.Tests;

/// <summary>
/// Integration tests that fetch real, well-known pages over the internet and
/// assert on information that is known to be there.
///
/// They are skipped unless <c>LOCALWEB_INTEGRATION=1</c> is set, because they
/// need internet access and an installed Playwright Chromium. They are meant to
/// be run explicitly, not on every build.
/// </summary>
public sealed class LiveWebTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("LOCALWEB_INTEGRATION") == "1";

    private const string SkipReason =
        "Live web tests require LOCALWEB_INTEGRATION=1, internet access and a Playwright Chromium install.";

    // --- Static pages: the easy case, served as plain HTML. ---

    [SkippableFact]
    public async Task Static_page_is_fetched_over_http_with_known_content()
    {
        Skip.IfNot(Enabled, SkipReason);

        await using var provider = BuildProvider();
        var fetcher = provider.GetRequiredService<PageFetcher>();

        var outcome = await fetcher.FetchAsync(
            new Uri("https://www.iana.org/help/example-domains"), forceBrowser: false, bypassCache: false, CancellationToken.None);

        // A static page should not need the browser.
        Assert.Equal(FetchSource.Http, outcome.Source);

        // Known facts: the page is about the reserved example domains (RFC 2606).
        Assert.Contains("example", outcome.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reserved", outcome.Markdown, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Static_page_links_include_the_rfc2606_reference()
    {
        Skip.IfNot(Enabled, SkipReason);

        await using var provider = BuildProvider();
        var fetcher = provider.GetRequiredService<PageFetcher>();
        var extractor = provider.GetRequiredService<LinkExtractor>();

        var page = await fetcher.FetchRawAsync(
            new Uri("https://www.iana.org/help/example-domains"), bypassCache: false, CancellationToken.None);

        var links = extractor.Extract(page.Html, page.Url, maxLinks: 200);

        // The page documents the example domains specified in RFC 2606.
        Assert.Contains(links, l => l.Url == "https://www.iana.org/go/rfc2606");
    }

    // --- Dynamic pages: content is rendered by JavaScript. ---

    [SkippableFact]
    public async Task Dynamic_page_falls_back_to_the_browser_for_rendered_content()
    {
        Skip.IfNot(Enabled, SkipReason);

        await using var provider = BuildProvider();
        var fetcher = provider.GetRequiredService<PageFetcher>();

        // Playwright's TodoMVC is a client-rendered React app: the static HTML
        // only ships an empty <section> and the app title is added by JavaScript.
        var outcome = await fetcher.FetchAsync(
            new Uri("https://demo.playwright.dev/todomvc/"), forceBrowser: false, bypassCache: true, CancellationToken.None);

        Assert.Equal(FetchSource.Browser, outcome.Source);

        // "todos" is the <h1> the React app renders; it is absent from the raw HTML.
        Assert.Contains("todos", outcome.Markdown);
    }

    [SkippableFact]
    public async Task WebRender_forces_the_browser_on_a_dynamic_page()
    {
        Skip.IfNot(Enabled, SkipReason);

        await using var provider = BuildProvider();
        var fetcher = provider.GetRequiredService<PageFetcher>();

        var outcome = await fetcher.FetchAsync(
            new Uri("https://demo.playwright.dev/todomvc/"), forceBrowser: true, bypassCache: true, CancellationToken.None);

        Assert.Equal(FetchSource.Browser, outcome.Source);
        Assert.Contains("todos", outcome.Markdown);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        // Default options: only http/https on ports 80/443, no loopback.
        services.AddSingleton(TestOptions.Create());
        services.AddLocalWebServices();

        return services.BuildServiceProvider();
    }
}
