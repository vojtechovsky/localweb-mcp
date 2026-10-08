using LocalWeb.Mcp.Fetch;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalWeb.Mcp.Tests;

public sealed class ContentExtractorTests
{
    private static ContentExtractor Create() =>
        new(TestOptions.Create(), NullLogger<ContentExtractor>.Instance);

    private static string ArticleHtml(string extraHead = "")
    {
        var body = string.Concat(Enumerable.Repeat(
            "<p>This is a reasonably long paragraph of article content that should be extracted as the main body text of the page.</p>",
            6));
        return $"<html><head><title>Test Article</title>{extraHead}</head><body><article><h1>Heading</h1>{body}</article></body></html>";
    }

    [Fact]
    public void Extract_returns_main_content_as_markdown()
    {
        var extractor = Create();

        var result = extractor.Extract("https://example.com/article", ArticleHtml());

        Assert.Contains("Heading", result.Markdown);
        Assert.Contains("reasonably long paragraph", result.Markdown);
        Assert.Equal("Test Article", result.Title);
        Assert.False(extractor.NeedsBrowser(ArticleHtml(), result));
    }

    [Fact]
    public void ExtractPlainText_preserves_text()
    {
        var result = Create().ExtractPlainText(string.Empty, "line one\r\n\r\n\r\n\r\nline two");

        Assert.Equal("line one\n\nline two", result.Markdown);
    }

    [Fact]
    public void NeedsBrowser_is_true_for_empty_spa_shell()
    {
        var html = "<html><head><title>App</title></head><body><div id=\"root\"></div></body></html>";
        var extractor = Create();

        var result = extractor.Extract("https://example.com/app", html);

        Assert.True(extractor.NeedsBrowser(html, result));
    }

    [Fact]
    public void NeedsBrowser_is_true_when_visible_text_asks_for_javascript()
    {
        var html = ArticleHtml().Replace("</body>", "<p>You need to enable JavaScript to run this app.</p></body>");
        var extractor = Create();

        var result = extractor.Extract("https://example.com/app", html);

        Assert.True(extractor.NeedsBrowser(html, result));
    }

    [Fact]
    public void NeedsBrowser_ignores_javascript_phrase_only_in_noscript()
    {
        var html = ArticleHtml().Replace("</body>", "<noscript>You need to enable JavaScript to run this app.</noscript></body>");
        var extractor = Create();

        var result = extractor.Extract("https://example.com/app", html);

        Assert.False(extractor.NeedsBrowser(html, result));
    }
}
