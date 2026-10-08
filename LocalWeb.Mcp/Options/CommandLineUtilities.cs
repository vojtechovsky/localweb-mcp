using CommandLine;

namespace LocalWeb.Mcp.Options;

/// <summary>
/// Thin wrapper around <see cref="Parser"/> that is safe to use inside an MCP
/// stdio server: help text and parse errors are written to stderr, and unknown
/// arguments are ignored so unrelated flags never crash startup.
/// </summary>
public static class CommandLineUtilities
{
    public static CommandLineOptions Parse(string[] args)
    {
        var parser = new Parser(settings =>
        {
            settings.CaseSensitive = false;
            settings.IgnoreUnknownArguments = true;
            // stdout carries the MCP protocol; help and errors must go to stderr.
            settings.HelpWriter = Console.Error;
        });

        var result = parser.ParseArguments<CommandLineOptions>(args);
        return result.Value ?? new CommandLineOptions();
    }

    /// <summary>
    /// Flattens the parsed options into configuration keys under the LocalWeb section.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string?>> ToConfiguration(this CommandLineOptions options)
    {
        var prefix = LocalWebOptions.SectionName + ":";

        if (!string.IsNullOrWhiteSpace(options.SearxngUrl))
        {
            yield return new($"{prefix}{nameof(LocalWebOptions.SearxngUrl)}", options.SearxngUrl);
        }

        if (!string.IsNullOrWhiteSpace(options.CachePath))
        {
            yield return new($"{prefix}{nameof(LocalWebOptions.CachePath)}", options.CachePath);
        }

        if (options.HttpTimeoutSeconds is > 0)
        {
            yield return new($"{prefix}{nameof(LocalWebOptions.HttpTimeoutSeconds)}", options.HttpTimeoutSeconds.Value.ToString());
        }

        if (options.BrowserTimeoutSeconds is > 0)
        {
            yield return new($"{prefix}{nameof(LocalWebOptions.BrowserTimeoutSeconds)}", options.BrowserTimeoutSeconds.Value.ToString());
        }
    }
}
