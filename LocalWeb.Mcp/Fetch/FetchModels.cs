namespace LocalWeb.Mcp.Fetch;

/// <summary>Main content extracted from a page, converted to Markdown.</summary>
/// <param name="Title">Page title, if one could be determined.</param>
/// <param name="Markdown">Main content as Markdown.</param>
/// <param name="TextLength">Length of the extracted Markdown, used for fallback decisions.</param>
public sealed record ExtractedContent(string Title, string Markdown, int TextLength);

/// <summary>Raw result of an HTTP fetch.</summary>
/// <param name="FinalUri">URI after following redirects.</param>
/// <param name="ContentType">Media type of the response body.</param>
/// <param name="Body">Decoded response body.</param>
/// <param name="Truncated">True when the body hit <c>MaxResponseBytes</c>.</param>
public sealed record HttpFetchResponse(Uri FinalUri, string ContentType, string Body, bool Truncated);

/// <summary>Raw result of a browser render.</summary>
/// <param name="FinalUrl">URL of the page after navigation.</param>
/// <param name="Html">Fully rendered HTML.</param>
public sealed record RenderedPage(string FinalUrl, string Html);

/// <summary>Origin of the returned content.</summary>
public enum FetchSource
{
    /// <summary>Retrieved over plain HTTP.</summary>
    Http,

    /// <summary>Rendered by the headless browser.</summary>
    Browser,

    /// <summary>Served from the cache.</summary>
    Cache,
}

/// <summary>Final result returned by the fetch pipeline.</summary>
public sealed record FetchOutcome(string Title, string Url, FetchSource Source, string Markdown);

/// <summary>Raw HTML of a page together with its resolved metadata.</summary>
/// <param name="Title">Page title, if one could be determined.</param>
/// <param name="Url">URL after following redirects / after rendering.</param>
/// <param name="Html">Raw or rendered HTML.</param>
/// <param name="Source">Where the HTML came from.</param>
public sealed record RawPage(string Title, string Url, string Html, FetchSource Source);

/// <summary>A link discovered on a page.</summary>
/// <param name="Text">Visible anchor text (may be empty).</param>
/// <param name="Url">Absolute http(s) URL.</param>
public sealed record LinkItem(string Text, string Url);
