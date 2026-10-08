using LocalWeb.Mcp.Fetch;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalWeb.Mcp.Tests;

public sealed class LinkExtractorTests
{
    private static LinkExtractor Create() => new(NullLogger<LinkExtractor>.Instance);

    [Fact]
    public void Extract_resolves_relative_urls_and_filters_schemes()
    {
        const string html = """
            <html><body>
              <a href="/about">About</a>
              <a href="https://example.com/abs">Absolute</a>
              <a href="mailto:x@example.com">mail</a>
              <a href="#frag">fragment</a>
              <a href="tel:+15551234">phone</a>
              <a href="/about">About duplicate</a>
            </body></html>
            """;

        var links = Create().Extract(html, "https://example.com/dir/page", 50);

        Assert.Equal(2, links.Count);
        Assert.Equal("https://example.com/about", links[0].Url);
        Assert.Equal("About", links[0].Text);
        Assert.Equal("https://example.com/abs", links[1].Url);
    }

    [Fact]
    public void Extract_honours_base_href()
    {
        const string html = """
            <html><head><base href="https://cdn.example.com/root/"></head>
            <body><a href="x">x</a></body></html>
            """;

        var links = Create().Extract(html, "https://example.com/page", 50);

        var link = Assert.Single(links);
        Assert.Equal("https://cdn.example.com/root/x", link.Url);
    }

    [Fact]
    public void Extract_respects_max_links()
    {
        var html = string.Concat(Enumerable.Range(0, 10).Select(i => $"<a href=\"/{i}\">{i}</a>"));

        var links = Create().Extract(html, "https://example.com/", 3);

        Assert.Equal(3, links.Count);
        Assert.Equal("https://example.com/0", links[0].Url);
    }

    [Fact]
    public void Extract_returns_empty_for_empty_html()
    {
        Assert.Empty(Create().Extract(string.Empty, "https://example.com/", 50));
    }
}
