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
    /// <summary>Base URL of the local SearXNG instance.</summary>
    [Option("searxng-url", HelpText = "Base URL of the local SearXNG instance.")]
    public string? SearxngUrl { get; set; }

    /// <summary>Path to the SQLite cache file.</summary>
    [Option("cache-path", HelpText = "Path to the SQLite cache file.")]
    public string? CachePath { get; set; }

    /// <summary>Minimum log level; logs are written to stderr.</summary>
    [Option("log-level", HelpText = "Minimum log level (Trace, Debug, Information, Warning, Error). Logs are written to stderr.")]
    public string? LogLevel { get; set; }

    /// <summary>HTTP request timeout in seconds.</summary>
    [Option("http-timeout", HelpText = "HTTP request timeout in seconds.")]
    public int? HttpTimeoutSeconds { get; set; }

    /// <summary>Browser navigation timeout in seconds.</summary>
    [Option("browser-timeout", HelpText = "Browser navigation timeout in seconds.")]
    public int? BrowserTimeoutSeconds { get; set; }

    /// <summary>When set, installs the Playwright Chromium browser and exits.</summary>
    [Option("install-browser", HelpText = "Install the Playwright Chromium browser and exit.")]
    public bool InstallBrowser { get; set; }

    /// <summary>When set, installs the OS dependencies required by Playwright browsers and exits.</summary>
    [Option("install-browser-deps", HelpText = "Install the OS dependencies required by Playwright browsers (needs root) and exit.")]
    public bool InstallBrowserDeps { get; set; }

    /// <summary>Testing only: allows loopback targets (disables loopback SSRF protection).</summary>
    [Option("allow-loopback", HelpText = "TESTING ONLY: allow loopback targets (disables loopback SSRF protection).")]
    public bool AllowLoopback { get; set; }
}
