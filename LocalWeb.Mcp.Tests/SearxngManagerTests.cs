using System.Net;
using System.Text;
using LocalWeb.Mcp.Search;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalWeb.Mcp.Tests;

public sealed class SearxngManagerTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "localweb-searxng-tests", Guid.NewGuid().ToString("N"));

    public SearxngManagerTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            Path.Combine(_dir, ".env.example"),
            "SEARXNG_HOST=127.0.0.1\nSEARXNG_PORT=8080\nSEARXNG_SECRET=change-me-run-openssl-rand-hex-32\n");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Fact]
    public void EnsureEnvironmentFile_generates_a_secret_and_does_not_overwrite()
    {
        Assert.True(SearxngManager.EnsureEnvironmentFile(_dir, out var secret));
        Assert.Equal(64, secret.Length);

        var envPath = Path.Combine(_dir, ".env");
        var content = File.ReadAllText(envPath);
        Assert.Contains($"SEARXNG_SECRET={secret}", content);
        Assert.DoesNotContain("change-me-run-openssl-rand-hex-32", content);

        // Second call must not overwrite the existing file.
        Assert.False(SearxngManager.EnsureEnvironmentFile(_dir, out var secondSecret));
        Assert.Empty(secondSecret);
        Assert.Equal(content, File.ReadAllText(envPath));
    }

    [Fact]
    public async Task StatusAsync_reports_a_reachable_api()
    {
        var manager = CreateManager("""{"results":[{"title":"x","url":"https://x"}]}""");

        Assert.True(await manager.StatusAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StatusAsync_reports_an_unreachable_api()
    {
        var manager = new SearxngManager(
            TestOptions.Create(),
            NullLogger<SearxngManager>.Instance,
            new ThrowingHandler());

        Assert.False(await manager.StatusAsync(CancellationToken.None));
    }

    private static SearxngManager CreateManager(string body) =>
        new(TestOptions.Create(), NullLogger<SearxngManager>.Instance, new FakeHandler(body));

    private sealed class FakeHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("unreachable");
    }
}
