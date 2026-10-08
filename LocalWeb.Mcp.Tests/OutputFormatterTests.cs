using LocalWeb.Mcp.Actions;
using LocalWeb.Mcp.Fetch;
using LocalWeb.Mcp.Search;

namespace LocalWeb.Mcp.Tests;

public sealed class OutputFormatterTests
{
    [Fact]
    public void FormatSearch_handles_no_results()
    {
        var response = new SearchResponse { Query = "nothing" };

        var text = OutputFormatter.FormatSearch("nothing", response);

        Assert.Contains("No results", text);
    }

    [Fact]
    public void FormatSearch_lists_results()
    {
        var response = new SearchResponse
        {
            NumberOfResults = 2,
            Results =
            [
                new SearchResult { Title = "First", Url = "https://first.example", Content = "snippet one", Engine = "google" },
                new SearchResult { Title = "Second", Url = "https://second.example" },
            ],
        };

        var text = OutputFormatter.FormatSearch("q", response);

        Assert.Contains("1. **First**", text);
        Assert.Contains("https://first.example", text);
        Assert.Contains("snippet one", text);
        Assert.Contains("2. **Second**", text);
    }

    [Fact]
    public void FormatFetch_includes_title_url_and_source()
    {
        var outcome = new FetchOutcome("My Title", "https://example.com/a", FetchSource.Browser, "body text");

        var text = OutputFormatter.FormatFetch(outcome);

        Assert.Contains("# My Title", text);
        Assert.Contains("URL: <https://example.com/a>", text);
        Assert.Contains("Source: browser", text);
        Assert.Contains("body text", text);
    }

    [Fact]
    public void FormatLinks_lists_links_and_handles_empty()
    {
        var links = new List<LinkItem>
        {
            new("Docs [v2]", "https://example.com/docs"),
            new(string.Empty, "https://example.com/bare"),
        };

        var text = OutputFormatter.FormatLinks("Page", "https://example.com/", FetchSource.Cache, links);

        Assert.Contains("# Page", text);
        Assert.Contains("Source: cache", text);
        Assert.Contains("Links: 2", text);
        Assert.Contains("[Docs \\[v2\\]](<https://example.com/docs>)", text);
        Assert.Contains("[<https://example.com/bare>](<https://example.com/bare>)", text);

        var empty = OutputFormatter.FormatLinks(string.Empty, "https://example.com/", FetchSource.Http, []);
        Assert.Contains("No http(s) links found.", empty);
    }

    [Fact]
    public void Output_escapes_untrusted_content()
    {
        var links = new List<LinkItem>
        {
            new("click **here** <img>", "https://evil.example/x"),
            new("brace", "https://evil.example/a> b\n[click](https://phish.example)"),
        };

        var linksText = OutputFormatter.FormatLinks("Page", "https://example.com/", FetchSource.Http, links);

        // Anchor text cannot inject bold/HTML.
        Assert.Contains("click \\*\\*here\\*\\* &lt;img&gt;", linksText);
        // Destinations are wrapped so ')' cannot break out...
        Assert.Contains("](<https://evil.example/x>)", linksText);
        // ...and '>' / whitespace / newlines are neutralized.
        Assert.Contains("<https://evil.example/a%3E", linksText);
        Assert.DoesNotContain("a> b", linksText);
        Assert.DoesNotContain("]\n", linksText);

        var response = new SearchResponse
        {
            Results =
            [
                new SearchResult { Title = "Evil](https://phish.example)", Url = "https://evil.example/", Content = "a\n# heading" },
            ],
        };

        var searchText = OutputFormatter.FormatSearch("q", response);

        // The ']' in the title is escaped, so it cannot forge a link.
        Assert.Contains("**Evil\\](https://phish.example)**", searchText);
        // Newlines are collapsed, so a snippet cannot start a heading.
        Assert.DoesNotContain("\n# heading", searchText);
    }
}
