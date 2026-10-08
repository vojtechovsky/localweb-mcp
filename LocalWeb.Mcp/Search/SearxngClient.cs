using System.Net;
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

    public SearxngClient(IOptions<LocalWebOptions> options, ILogger<SearxngClient> logger)
        : this(options, logger, new SocketsHttpHandler())
    {
    }

    /// <summary>Constructor used by tests to inject a fake transport.</summary>
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

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
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
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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

    public void Dispose()
    {
        _http.Dispose();
    }
}
