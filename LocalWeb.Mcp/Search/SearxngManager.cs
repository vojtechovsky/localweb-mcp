using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using LocalWeb.Mcp.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalWeb.Mcp.Search;

/// <summary>
/// Manages the bundled SearXNG Docker Compose instance that backs
/// <c>web_search</c>. The maintenance commands are explicit (CLI flags); the MCP
/// server itself never starts or stops containers.
/// </summary>
public sealed class SearxngManager : IDisposable
{
    private const string PlaceholderSecret = "change-me-run-openssl-rand-hex-32";

    private readonly LocalWebOptions _options;
    private readonly ILogger<SearxngManager> _logger;
    private readonly HttpClient _http;

    /// <summary>Initializes a new manager with the default HTTP transport.</summary>
    public SearxngManager(IOptions<LocalWebOptions> options, ILogger<SearxngManager> logger)
        : this(options, logger, new SocketsHttpHandler())
    {
    }

    /// <summary>Constructor used by tests to inject a fake transport.</summary>
    /// <param name="options">Bound options.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="handler">HTTP transport; the manager takes ownership.</param>
    public SearxngManager(IOptions<LocalWebOptions> options, ILogger<SearxngManager> logger, HttpMessageHandler handler)
    {
        _options = options.Value;
        _logger = logger;
        _http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    /// <summary>Directory holding the bundled Compose files.</summary>
    public string InfraDirectory => string.IsNullOrWhiteSpace(_options.SearxngInfraDir)
        ? Path.Combine(AppContext.BaseDirectory, "infra", "searxng")
        : Path.GetFullPath(_options.SearxngInfraDir);

    /// <summary>Starts SearXNG (<c>docker compose up -d</c>) and waits for the JSON API.</summary>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>True when the instance is up and reachable.</returns>
    public async Task<bool> UpAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Path.Combine(InfraDirectory, "docker-compose.yml")))
        {
            _logger.LogError("SearXNG compose files were not found in {Directory}.", InfraDirectory);
            return false;
        }

        if (EnsureEnvironmentFile(InfraDirectory, out _))
        {
            _logger.LogInformation("Generated {Path} with a fresh secret.", Path.Combine(InfraDirectory, ".env"));
        }

        var (exitCode, output) = await RunDockerAsync(["compose", "up", "-d"], cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            _logger.LogError("docker compose up failed (exit {Code}):\n{Output}", exitCode, output);
            return false;
        }

        _logger.LogInformation("SearXNG started; waiting for the API at {Url}…", _options.SearxngUrl);
        if (await WaitForApiAsync(cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("SearXNG is reachable at {Url}.", _options.SearxngUrl);
            return true;
        }

        _logger.LogError("SearXNG did not answer at {Url} in time. Check 'docker compose logs'.", _options.SearxngUrl);
        return false;
    }

    /// <summary>Stops the bundled SearXNG instance (<c>docker compose down</c>).</summary>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>True when the containers were stopped.</returns>
    public async Task<bool> DownAsync(CancellationToken cancellationToken)
    {
        var (exitCode, output) = await RunDockerAsync(["compose", "down"], cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            _logger.LogError("docker compose down failed (exit {Code}):\n{Output}", exitCode, output);
            return false;
        }

        _logger.LogInformation("SearXNG container(s) stopped.");
        return true;
    }

    /// <summary>Reports whether the configured SearXNG API answers.</summary>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>True when the API returns a JSON <c>results</c> array.</returns>
    public async Task<bool> StatusAsync(CancellationToken cancellationToken)
    {
        var reachable = await IsApiReachableAsync(cancellationToken).ConfigureAwait(false);
        if (reachable)
        {
            _logger.LogInformation("SearXNG is reachable at {Url}.", _options.SearxngUrl);
        }
        else
        {
            _logger.LogWarning("SearXNG is NOT reachable at {Url}.", _options.SearxngUrl);
        }

        return reachable;
    }

    /// <summary>
    /// Creates <c>.env</c> from <c>.env.example</c> with a freshly generated
    /// secret when it does not exist yet.
    /// </summary>
    /// <param name="infraDirectory">Directory containing the Compose files.</param>
    /// <param name="secret">The generated secret, or empty when a file already existed.</param>
    /// <returns>True when a new <c>.env</c> was written.</returns>
    /// <exception cref="LocalWebException">Thrown when <c>.env.example</c> is missing.</exception>
    public static bool EnsureEnvironmentFile(string infraDirectory, out string secret)
    {
        secret = string.Empty;

        var envPath = Path.Combine(infraDirectory, ".env");
        if (File.Exists(envPath))
        {
            return false;
        }

        var examplePath = Path.Combine(infraDirectory, ".env.example");
        if (!File.Exists(examplePath))
        {
            throw new LocalWebException($".env.example was not found in '{infraDirectory}'.");
        }

        secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var content = File.ReadAllText(examplePath)
            .Replace(PlaceholderSecret, secret, StringComparison.Ordinal);

        File.WriteAllText(envPath, content);
        return true;
    }

    private async Task<bool> WaitForApiAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await IsApiReachableAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    private async Task<bool> IsApiReachableAsync(CancellationToken cancellationToken)
    {
        try
        {
            var uri = $"{_options.SearxngUrl.TrimEnd('/')}/search?q=localweb&format=json";
            using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("results", out _);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return false;
        }
    }

    private async Task<(int ExitCode, string Output)> RunDockerAsync(string[] args, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            WorkingDirectory = InfraDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _logger.LogError(ex, "Could not start 'docker'. Is Docker installed and on PATH?");
            return (-1, ex.Message);
        }

        if (process is null)
        {
            _logger.LogError("Could not start 'docker'.");
            return (-1, "The docker process was not started.");
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (process.ExitCode, (await stdout) + (await stderr));
        }
    }

    /// <summary>Disposes the underlying HTTP client.</summary>
    public void Dispose() => _http.Dispose();
}
