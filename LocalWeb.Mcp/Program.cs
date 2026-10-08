using System.Text;
using LocalWeb.Mcp.Cache;
using LocalWeb.Mcp.Composition;
using LocalWeb.Mcp.Fetch;
using LocalWeb.Mcp.Options;
using LocalWeb.Mcp.Search;
using LocalWeb.Mcp.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

// Legacy pages declare charsets such as windows-1250 that are not available
// unless the code-pages provider is registered.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = [],
    // Resolve appsettings.json against the binary location so the server works
    // no matter which working directory the MCP client launches it from.
    ContentRootPath = AppContext.BaseDirectory,
});

// Command-line overrides win over appsettings.json and environment variables.
builder.Configuration.AddInMemoryCollection(commandLine.ToConfiguration());

// The configuration binder APPENDS to non-empty collection defaults, so clear
// them first: a configured AllowedSchemes/AllowedPorts must replace the defaults
// (otherwise egress could never be narrowed), then validate the result.
builder.Services.AddOptions<LocalWebOptions>()
    .Configure(options =>
    {
        options.AllowedSchemes = [];
        options.AllowedPorts = [];
    })
    .Bind(builder.Configuration.GetSection(LocalWebOptions.SectionName))
    .Validate(o => o.MaxOutputChars > 0, "LocalWeb:MaxOutputChars must be greater than 0.")
    .Validate(o => o.MaxResponseBytes > 0, "LocalWeb:MaxResponseBytes must be greater than 0.")
    .Validate(o => o.MaxMaxResults >= 1, "LocalWeb:MaxMaxResults must be at least 1.")
    .Validate(o => o.MaxRedirects >= 0, "LocalWeb:MaxRedirects must not be negative.")
    .Validate(o => o.HttpTimeoutSeconds > 0, "LocalWeb:HttpTimeoutSeconds must be greater than 0.")
    .Validate(o => o.BrowserTimeoutSeconds > 0, "LocalWeb:BrowserTimeoutSeconds must be greater than 0.")
    .Validate(o => Uri.TryCreate(o.SearxngUrl, UriKind.Absolute, out _), "LocalWeb:SearxngUrl must be an absolute URL.")
    .ValidateOnStart();

// The loopback bypass is test-only: configuration/env cannot enable it, only the
// explicit --allow-loopback command-line flag can.
builder.Services.PostConfigure<LocalWebOptions>(options => options.AllowLoopbackForTests = commandLine.AllowLoopback);

// Local web layer. The MCP tool types in Actions/ are discovered and constructed
// per call by WithToolsFromAssembly, so they are not registered here.
builder.Services.AddLocalWebServices();

// stdout belongs to the MCP protocol, so every log line must go to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
if (commandLine.LogLevel is not null)
{
    builder.Logging.SetMinimumLevel(ResolveLogLevel(commandLine.LogLevel));
}

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

var options = app.Services.GetRequiredService<IOptions<LocalWebOptions>>().Value;
if (options.AllowLoopbackForTests)
{
    app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("LocalWeb.Mcp.Startup")
        .LogWarning("AllowLoopbackForTests is enabled: loopback SSRF protection is disabled. Never use this outside tests.");
}

await app.RunAsync();
return 0;

static LogLevel ResolveLogLevel(string? value) =>
    Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level) ? level : LogLevel.Information;
