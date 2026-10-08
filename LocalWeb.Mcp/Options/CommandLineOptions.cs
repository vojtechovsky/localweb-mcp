using CommandLine;

namespace LocalWeb.Mcp.Options;

/// <summary>
/// Command-line overrides for a few frequently tuned values. Every option is
/// optional; unspecified values fall back to appsettings.json / environment.
/// Parsing never writes to stdout, because stdout is reserved for the MCP
/// protocol (see <see cref="CommandLineUtilities"/>).
/// </summary>
public sealed class CommandLineOptions
{
    [Option("searxng-url", HelpText = "Base URL of the local SearXNG instance.")]
    public string? SearxngUrl { get; set; }

    [Option("cache-path", HelpText = "Path to the SQLite cache file.")]
    public string? CachePath { get; set; }

    [Option("log-level", HelpText = "Minimum log level (Trace, Debug, Information, Warning, Error). Logs are written to stderr.")]
    public string? LogLevel { get; set; }

    [Option("http-timeout", HelpText = "HTTP request timeout in seconds.")]
    public int? HttpTimeoutSeconds { get; set; }

    [Option("browser-timeout", HelpText = "Browser navigation timeout in seconds.")]
    public int? BrowserTimeoutSeconds { get; set; }

    [Option("install-browser", HelpText = "Install the Playwright Chromium browser and exit.")]
    public bool InstallBrowser { get; set; }

    [Option("install-browser-deps", HelpText = "Install the OS dependencies required by Playwright browsers (needs root) and exit.")]
    public bool InstallBrowserDeps { get; set; }
}
