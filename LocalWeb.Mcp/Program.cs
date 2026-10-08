using LocalWeb.Mcp.Actions;
using LocalWeb.Mcp.Cache;
using LocalWeb.Mcp.Fetch;
using LocalWeb.Mcp.Options;
using LocalWeb.Mcp.Search;
using LocalWeb.Mcp.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var commandLine = CommandLineUtilities.Parse(args);

// Maintenance modes: install the Playwright browser / OS dependencies and exit.
// These exist so the browser can be installed on Linux without PowerShell.
if (commandLine.InstallBrowserDeps)
{
    return Microsoft.Playwright.Program.Main(["install-deps", "chromium"]);
}

if (commandLine.InstallBrowser)
{
    return Microsoft.Playwright.Program.Main(["install", "chromium"]);
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = [],
    // Resolve appsettings.json against the binary location so the server works
    // no matter which working directory the MCP client launches it from.
    ContentRootPath = AppContext.BaseDirectory,
});

// Command-line overrides win over appsettings.json and environment variables.
builder.Configuration.AddInMemoryCollection(commandLine.ToConfiguration());

builder.Services.Configure<LocalWebOptions>(
    builder.Configuration.GetSection(LocalWebOptions.SectionName));

// The configuration binder appends to collections that already have default
// values, so de-duplicate the scheme/port lists after binding.
builder.Services.PostConfigure<LocalWebOptions>(options =>
{
    options.AllowedSchemes = options.AllowedSchemes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    options.AllowedPorts = options.AllowedPorts.Distinct().ToArray();
});

// Local web layer.
builder.Services.AddSingleton<UrlGuard>();
builder.Services.AddSingleton<SqliteCache>();
builder.Services.AddSingleton<SearxngClient>();
builder.Services.AddSingleton<HttpFetcher>();
builder.Services.AddSingleton<PlaywrightFetcher>();
builder.Services.AddSingleton<ContentExtractor>();
builder.Services.AddSingleton<LinkExtractor>();
builder.Services.AddSingleton<PageFetcher>();

// MCP tool types (also discovered by WithToolsFromAssembly, registered explicitly
// so constructor injection is guaranteed to resolve).
builder.Services.AddSingleton<WebSearchAction>();
builder.Services.AddSingleton<WebFetchAction>();
builder.Services.AddSingleton<WebRenderAction>();
builder.Services.AddSingleton<WebExtractLinksAction>();

// stdout belongs to the MCP protocol, so every log line must go to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(ResolveLogLevel(commandLine.LogLevel));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
return 0;

static LogLevel ResolveLogLevel(string? value) =>
    Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level) ? level : LogLevel.Information;
