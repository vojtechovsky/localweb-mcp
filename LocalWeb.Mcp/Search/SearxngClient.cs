using System.Net;
using System.Text;
using System.Text.Json;
using LocalWeb.Mcp.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalWeb.Mcp.Search;

/// <summary>
/// Minimal client for the SearXNG JSON API.
/// Verified endpoint: <c>GET {base}/search?q=...&amp;format=json</c>.
/// The JSON format must be enabled in the instance's settings.yml, otherwise the
/// API returns 403.
/// </summary>
public sealed class SearxngClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly LocalWebOptions _options;
    private readonly ILogger<SearxngClient> _logger;
    private readonly HttpClient _http;

    /// <summary>Initializes the client with a default transport.</summary>
    public SearxngClient(IOptions<LocalWebOptions> options, ILogger<SearxngClient> logger)
        : this(options, logger, new SocketsHttpHandler())
    {
    }

    /// <summary>Constructor used by tests to inject a fake transport.</summary>
    /// <param name="options">Bound options.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="handler">The HTTP transport to use; the client takes ownership.</param>
    public SearxngClient(IOptions<LocalWebOptions> options, ILogger<SearxngClient> logger, HttpMessageHandler handler)
    {
        _options = options.Value;
        _logger = logger;

        _http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    /// <summary>Runs a search against the local SearXNG instance.</summary>
    /// <param name="query">Search query.</param>
    /// <param name="maxResults">Maximum results to return.</param>
    /// <param name="language">Language code, or "auto".</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    public async Task<SearchResponse> SearchAsync(
        string query,
        int maxResults,
        string language,
        int page,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new LocalWebException("The search query must not be empty.");
        }

        var requestUri = BuildRequestUri(query, language, page);
        _logger.LogDebug("Querying SearXNG: {Uri}", requestUri);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.HttpTimeoutSeconds));
        var token = timeoutCts.Token;

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LocalWebException(
                $"SearXNG did not respond within {_options.HttpTimeoutSeconds}s at {_options.SearxngUrl}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new LocalWebException(
                $"Could not reach SearXNG at {_options.SearxngUrl}. Is the instance running?", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new LocalWebException(
                    "SearXNG refused the request (403). Make sure 'json' is listed under search.formats in settings.yml.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new LocalWebException(
                    $"SearXNG returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            string body;
            try
            {
                body = await ReadLimitedBodyAsync(response, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new LocalWebException("Reading the SearXNG response timed out.", ex);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException)
            {
                throw new LocalWebException("Failed to read the SearXNG response body.", ex);
            }

            SearchResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<SearchResponse>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new LocalWebException("SearXNG returned an invalid JSON response.", ex);
            }

            if (parsed is null)
            {
                throw new LocalWebException("SearXNG returned an empty JSON response.");
            }

            // An explicit JSON null overwrites the collection initializers.
            parsed.Results ??= [];
            parsed.Suggestions ??= [];

            if (parsed.Results.Count > maxResults)
            {
                parsed.Results = parsed.Results.Take(maxResults).ToList();
            }

            return parsed;
        }
    }

    private Uri BuildRequestUri(string query, string language, int page)
    {
        var baseUrl = _options.SearxngUrl.TrimEnd('/');
        var parts = new List<string>
        {
            "q=" + Uri.EscapeDataString(query),
            "format=json",
            "safesearch=0",
        };

        if (!string.IsNullOrWhiteSpace(language) &&
            !language.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("language=" + Uri.EscapeDataString(language));
        }

        if (page > 1)
        {
            parts.Add("pageno=" + page);
        }

        var builder = new UriBuilder(baseUrl + "/search")
        {
            Query = string.Join('&', parts),
        };

        return builder.Uri;
    }

    private async Task<string> ReadLimitedBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            var remaining = _options.MaxResponseBytes - (int)buffer.Length;
            if (remaining <= 0)
            {
                break;
            }

            buffer.Write(chunk, 0, Math.Min(read, remaining));
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Disposes the underlying HTTP client.</summary>
    public void Dispose()
    {
        _http.Dispose();
    }
}
