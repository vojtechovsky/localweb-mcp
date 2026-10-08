using LocalWeb.Mcp.Actions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;

namespace LocalWeb.Mcp.Tests;

/// <summary>
/// Expected tool failures must surface as MCP errors (isError=true) rather than
/// as a normal string, so clients can tell failure from content.
/// </summary>
public sealed class ActionErrorTests
{
    [Fact]
    public async Task WebFetch_reports_a_relative_url_as_an_error()
    {
        var action = new WebFetchAction(null!, NullLogger<WebFetchAction>.Instance);

        await Assert.ThrowsAsync<McpException>(() => action.WebFetchAsync("not a url"));
    }

    [Fact]
    public async Task WebRender_reports_a_relative_url_as_an_error()
    {
        var action = new WebRenderAction(null!, NullLogger<WebRenderAction>.Instance);

        await Assert.ThrowsAsync<McpException>(() => action.WebRenderAsync("not a url"));
    }

    [Fact]
    public async Task WebExtractLinks_reports_a_relative_url_as_an_error()
    {
        var action = new WebExtractLinksAction(null!, null!, NullLogger<WebExtractLinksAction>.Instance);

        await Assert.ThrowsAsync<McpException>(() => action.WebExtractLinksAsync("not a url"));
    }

    [Fact]
    public async Task WebSearch_reports_an_empty_query_as_an_error()
    {
        var action = new WebSearchAction(null!, null!, TestOptions.Create(), NullLogger<WebSearchAction>.Instance);

        await Assert.ThrowsAsync<McpException>(() => action.WebSearchAsync("   "));
    }
}
