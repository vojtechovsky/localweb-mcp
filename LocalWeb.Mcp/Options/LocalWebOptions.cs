namespace LocalWeb.Mcp.Options;

/// <summary>
/// Strongly typed configuration for the local web layer. Bound from the
/// "LocalWeb" section of appsettings.json and overridable from the command line.
/// </summary>
public sealed class LocalWebOptions
{
    public const string SectionName = "LocalWeb";

    /// <summary>Base URL of the local SearXNG instance (no trailing slash required).</summary>
    public string SearxngUrl { get; set; } = "http://127.0.0.1:8080";

    /// <summary>Path to the SQLite cache file. Relative paths are resolved against the application base directory (<c>AppContext.BaseDirectory</c>).</summary>
    public string CachePath { get; set; } = "cache.db";

    /// <summary>Time-to-live for successful search results.</summary>
    public int SearchTtlMinutes { get; set; } = 180;

    /// <summary>Time-to-live for successfully fetched pages.</summary>
    public int FetchTtlMinutes { get; set; } = 720;

    /// <summary>Time-to-live for cached error responses (kept short).</summary>
    public int ErrorTtlMinutes { get; set; } = 2;

    /// <summary>Hard cap on the number of bytes read from an HTTP response.</summary>
    public int MaxResponseBytes { get; set; } = 5_000_000;

    /// <summary>Maximum number of characters returned to the MCP client.</summary>
    public int MaxOutputChars { get; set; } = 20_000;

    /// <summary>HTTP request timeout in seconds.</summary>
    public int HttpTimeoutSeconds { get; set; } = 20;

    /// <summary>Browser navigation timeout in seconds.</summary>
    public int BrowserTimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum number of HTTP redirects to follow manually.</summary>
    public int MaxRedirects { get; set; } = 5;

    /// <summary>Maximum number of concurrently open browser pages.</summary>
    public int MaxConcurrentBrowserPages { get; set; } = 2;

    /// <summary>Allowed URL schemes.</summary>
    public string[] AllowedSchemes { get; set; } = ["http", "https"];

    /// <summary>Allowed destination ports.</summary>
    public int[] AllowedPorts { get; set; } = [80, 443];

    /// <summary>User-Agent header sent by HTTP and browser fetches.</summary>
    public string UserAgent { get; set; } = "LocalWebMcp/1.0";

    /// <summary>Default number of search results.</summary>
    public int DefaultMaxResults { get; set; } = 8;

    /// <summary>Upper bound for the requested number of search results.</summary>
    public int MaxMaxResults { get; set; } = 20;

    /// <summary>
    /// Extracted text shorter than this many characters is treated as an empty
    /// page and triggers the browser fallback.
    /// </summary>
    public int MinExtractCharsForBrowser { get; set; } = 400;

    /// <summary>
    /// TESTING ONLY. When true, loopback addresses are allowed by <c>UrlGuard</c>.
    /// This exists so integration tests can target a local test web server.
    /// Never enable this in production: it disables the SSRF protection for
    /// localhost.
    /// </summary>
    public bool AllowLoopbackForTests { get; set; }
}
