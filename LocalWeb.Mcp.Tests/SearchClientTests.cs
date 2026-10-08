using System.Net;
using System.Text;
using LocalWeb.Mcp.Options;
using LocalWeb.Mcp.Search;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalWeb.Mcp.Tests;

public sealed class SearchClientTests
{
    private const string SampleJson = """
        {
          "query": "test",
          "number_of_results": 42,
          "results": [
            { "title": "One", "url": "https://one.example", "content": "first", "engine": "duckduckgo", "publishedDate": "2024-01-01T00:00:00" },
            { "title": "Two", "url": "https://two.example", "content": "second", "engine": "google" },
            { "title": "Three", "url": "https://three.example", "content": "third", "engine": "brave" }
          ],
          "suggestions": ["testing"]
        }
        """;

    private static SearxngClient Create(HttpStatusCode status, string body, out List<Uri> requested)
    {
        var uris = new List<Uri>();
        var handler = new FakeHandler(status, body, uris);
        requested = uris;
        return new SearxngClient(
            TestOptions.Create(),
            NullLogger<SearxngClient>.Instance,
            handler);
    }

    [Fact]
    public async Task SearchAsync_parses_results_and_truncates_to_max()
    {
        var client = Create(HttpStatusCode.OK, SampleJson, out _);

        var response = await client.SearchAsync("test", maxResults: 2, "auto", 1, CancellationToken.None);

        Assert.Equal(2, response.Results.Count);
        Assert.Equal("One", response.Results[0].Title);
        Assert.Equal("https://one.example", response.Results[0].Url);
        Assert.Equal("2024-01-01T00:00:00", response.Results[0].PublishedDate);
        Assert.Equal(42, response.NumberOfResults);
    }

    [Fact]
    public async Task SearchAsync_builds_expected_request_uri()
    {
        var client = Create(HttpStatusCode.OK, SampleJson, out var requested);

        await client.SearchAsync("hello world", maxResults: 5, "cs", 2, CancellationToken.None);

        var uri = Assert.Single(requested);
        Assert.Equal("/search", uri.AbsolutePath);
        Assert.Contains("q=hello%20world", uri.Query);
        Assert.Contains("format=json", uri.Query);
        Assert.Contains("language=cs", uri.Query);
        Assert.Contains("pageno=2", uri.Query);
    }

    [Fact]
    public async Task SearchAsync_maps_forbidden_to_actionable_error()
    {
        var client = Create(HttpStatusCode.Forbidden, "forbidden", out _);

        var ex = await Assert.ThrowsAsync<LocalWebException>(
            () => client.SearchAsync("test", 8, "auto", 1, CancellationToken.None));

        Assert.Contains("settings.yml", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_maps_invalid_json_to_error()
    {
        var client = Create(HttpStatusCode.OK, "<html>not json</html>", out _);

        await Assert.ThrowsAsync<LocalWebException>(
            () => client.SearchAsync("test", 8, "auto", 1, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_rejects_empty_query()
    {
        var client = Create(HttpStatusCode.OK, SampleJson, out _);

        await Assert.ThrowsAsync<LocalWebException>(
            () => client.SearchAsync("   ", 8, "auto", 1, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_handles_null_collections()
    {
        var client = Create(HttpStatusCode.OK, """{"query":"q","results":null,"suggestions":null}""", out _);

        var response = await client.SearchAsync("q", 8, "auto", 1, CancellationToken.None);

        Assert.Empty(response.Results);
        Assert.Empty(response.Suggestions);
    }

    private sealed class FakeHandler(HttpStatusCode status, string body, List<Uri> requested) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null)
            {
                requested.Add(request.RequestUri);
            }

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
