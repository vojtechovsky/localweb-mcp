using LocalWeb.Mcp.Cache;
using LocalWeb.Mcp.Fetch;
using LocalWeb.Mcp.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalWeb.Mcp.Tests;

/// <summary>
/// End-to-end tests for the fetch pipeline. They start a local Kestrel server
/// and need an installed Playwright Chromium, so they are skipped unless
/// <c>LOCALWEB_INTEGRATION=1</c> is set.
/// </summary>
public sealed class IntegrationTests
{
    private static bool IntegrationEnabled =>
        Environment.GetEnvironmentVariable("LOCALWEB_INTEGRATION") == "1";

    private const string SkipReason =
        "Integration tests require LOCALWEB_INTEGRATION=1 and an installed Playwright browser.";

    [SkippableFact]
    public async Task WebFetch_static_page_uses_http_then_cache()
    {
        Skip.IfNot(IntegrationEnabled, SkipReason);

        await using var server = await TestWebServer.StartAsync();
        await using var provider = BuildProvider(server.BaseUri, allowLoopback: true);
        var fetcher = provider.GetRequiredService<PageFetcher>();

        var first = await fetcher.FetchAsync(server.BaseUri, forceBrowser: false, bypassCache: false, CancellationToken.None);
        Assert.Equal(FetchSource.Http, first.Source);
        Assert.Contains("static article", first.Markdown, StringComparison.OrdinalIgnoreCase);

        var second = await fetcher.FetchAsync(server.BaseUri, forceBrowser: false, bypassCache: false, CancellationToken.None);
        Assert.Equal(FetchSource.Cache, second.Source);
    }

    [SkippableFact]
    public async Task WebFetch_falls_back_to_browser_for_javascript_page()
    {
        Skip.IfNot(IntegrationEnabled, SkipReason);

        await using var server = await TestWebServer.StartAsync();
        await using var provider = BuildProvider(server.BaseUri, allowLoopback: true);
        var fetcher = provider.GetRequiredService<PageFetcher>();

        var outcome = await fetcher.FetchAsync(
            new Uri(server.BaseUri, "spa"), forceBrowser: false, bypassCache: true, CancellationToken.None);

        Assert.Equal(FetchSource.Browser, outcome.Source);
        Assert.Contains("Rendered App", outcome.Markdown);
    }

    [SkippableFact]
    public async Task WebRender_rejects_loopback_when_test_flag_is_off()
    {
        Skip.IfNot(IntegrationEnabled, SkipReason);

        await using var server = await TestWebServer.StartAsync();
        await using var provider = BuildProvider(server.BaseUri, allowLoopback: false);
        var fetcher = provider.GetRequiredService<PageFetcher>();

        await Assert.ThrowsAsync<UrlGuardException>(
            () => fetcher.FetchAsync(server.BaseUri, forceBrowser: true, bypassCache: true, CancellationToken.None));
    }

    [SkippableFact]
    public async Task Concurrent_renders_respect_the_page_limit()
    {
        Skip.IfNot(IntegrationEnabled, SkipReason);

        await using var server = await TestWebServer.StartAsync();
        await using var provider = BuildProvider(server.BaseUri, allowLoopback: true, maxBrowserPages: 1);
        var fetcher = provider.GetRequiredService<PageFetcher>();

        var tasks = Enumerable.Range(0, 3)
            .Select(_ => fetcher.FetchAsync(
                new Uri(server.BaseUri, "spa"), forceBrowser: true, bypassCache: true, CancellationToken.None));

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(FetchSource.Browser, r.Source));
    }

    private static ServiceProvider BuildProvider(
        Uri baseUri,
        bool allowLoopback,
        int maxBrowserPages = 2)
    {
        var options = TestOptions.Create(o =>
        {
            o.AllowLoopbackForTests = allowLoopback;
            o.AllowedPorts = [baseUri.Port];
            o.MaxConcurrentBrowserPages = maxBrowserPages;
        });

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(options);
        services.AddSingleton<UrlGuard>();
        services.AddSingleton<SqliteCache>();
        services.AddSingleton<HttpFetcher>();
        services.AddSingleton<PlaywrightFetcher>();
        services.AddSingleton<ContentExtractor>();
        services.AddSingleton<PageFetcher>();
        return services.BuildServiceProvider();
    }

    private sealed class TestWebServer : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private TestWebServer(WebApplication app, Uri baseUri)
        {
            _app = app;
            BaseUri = baseUri;
        }

        public Uri BaseUri { get; }

        public static async Task<TestWebServer> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();

            app.MapGet("/", () => Results.Content(StaticHtml(), "text/html; charset=utf-8"));
            app.MapGet("/spa", () => Results.Content(SpaHtml(), "text/html; charset=utf-8"));

            await app.StartAsync();
            var address = app.Urls.First();
            return new TestWebServer(app, new Uri(address));
        }

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();

        private static string StaticHtml()
        {
            var paragraphs = string.Concat(Enumerable.Repeat(
                "<p>The static article contains several sentences of real content so the extractor keeps it and does not fall back to the browser.</p>",
                6));
            return $"<html><head><title>Static Page</title></head><body><article><h1>Static article</h1>{paragraphs}</article></body></html>";
        }

        private static string SpaHtml() => """
            <html><head><title>SPA</title></head><body>
            <div id="root"></div>
            <script>
              const root = document.getElementById('root');
              let html = '<h1>Rendered App</h1>';
              for (let i = 0; i < 20; i++) {
                html += '<p>Rendered paragraph number ' + i + ' with enough text to count as content.</p>';
              }
              root.innerHTML = html;
            </script>
            </body></html>
            """;
    }
}
