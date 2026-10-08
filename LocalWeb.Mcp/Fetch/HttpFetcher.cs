using System.Net;
using System.Net.Sockets;
using System.Text;
using LocalWeb.Mcp.Options;
using LocalWeb.Mcp.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalWeb.Mcp.Fetch;

/// <summary>
/// Fetch a page over plain HTTP. Redirects are followed manually so every hop
/// can be re-validated by <see cref="UrlGuard"/>, and the socket is opened
/// against the exact address that was validated (no DNS rebinding).
/// </summary>
public sealed class HttpFetcher : IDisposable
{
    private readonly LocalWebOptions _options;
    private readonly UrlGuard _guard;
    private readonly ILogger<HttpFetcher> _logger;
    private readonly HttpClient _http;

    public HttpFetcher(IOptions<LocalWebOptions> options, UrlGuard guard, ILogger<HttpFetcher> logger)
    {
        _options = options.Value;
        _guard = guard;
        _logger = logger;

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            // DNS pinning in ConnectAsync only holds if we connect to the target
            // ourselves; an HTTP proxy would send ConnectAsync the proxy address
            // and let the proxy re-resolve the target. Disable proxies explicitly.
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds),
            ConnectCallback = ConnectAsync,
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,text/plain;q=0.9,*/*;q=0.1");
    }

    public async Task<HttpFetchResponse> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        // Bound the ENTIRE request (connect + headers + body). HttpClient.Timeout
        // stops applying once headers are read with ResponseHeadersRead, so a
        // server that stalls after the headers would otherwise hang forever.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.HttpTimeoutSeconds));
        var token = timeoutCts.Token;

        var current = url;
        var redirects = 0;

        while (true)
        {
            await _guard.ValidateAsync(current, token).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new LocalWebException($"Request to {current} timed out after {_options.HttpTimeoutSeconds}s.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new LocalWebException($"Could not fetch {current}: {ex.Message}", ex);
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode) && response.Headers.Location is not null)
                {
                    if (redirects >= _options.MaxRedirects)
                    {
                        throw new LocalWebException($"Too many redirects (maximum {_options.MaxRedirects}) for {url}.");
                    }

                    var location = response.Headers.Location;
                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    // Fail fast if the redirect target is not allowed.
                    await _guard.ValidateAsync(next, token).ConfigureAwait(false);
                    _logger.LogDebug("Following redirect {From} -> {To}.", current, next);
                    current = next;
                    redirects++;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new LocalWebException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase} for {current}.");
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;
                if (mediaType is not ("text/html" or "application/xhtml+xml" or "text/plain"))
                {
                    throw new LocalWebException(
                        $"Unsupported content type '{mediaType}' for {current}. Only HTML and plain text are supported.");
                }

                var (body, truncated) = await ReadBodyAsync(response, current, token).ConfigureAwait(false);
                return new HttpFetchResponse(current, mediaType, body, truncated);
            }
        }
    }

    private async Task<(string Body, bool Truncated)> ReadBodyAsync(
        HttpResponseMessage response,
        Uri url,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadLimitedAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LocalWebException($"Reading the response from {url} timed out.", ex);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            throw new LocalWebException($"The connection to {url} failed while reading the response.", ex);
        }
    }

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;
        var address = await _guard.ResolveValidatedAsync(endpoint.Host, endpoint.Port, cancellationToken)
            .ConfigureAwait(false);

        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task<(string Body, bool Truncated)> ReadLimitedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var max = _options.MaxResponseBytes;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        var truncated = false;
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            var remaining = max - (int)buffer.Length;
            if (remaining <= 0)
            {
                truncated = true;
                break;
            }

            if (read > remaining)
            {
                buffer.Write(chunk, 0, remaining);
                truncated = true;
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        var encoding = ResolveEncoding(response.Content.Headers.ContentType?.CharSet);
        var body = encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);

        // Strip a UTF-8 BOM so it does not become a leading U+FEFF in the Markdown.
        if (body.Length > 0 && body[0] == '\uFEFF')
        {
            body = body[1..];
        }

        return (body, truncated);
    }

    private static Encoding ResolveEncoding(string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset.Trim('"', ' '));
            }
            catch (ArgumentException)
            {
                // Unknown charset; fall through to UTF-8.
            }
        }

        return Encoding.UTF8;
    }

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Found or
        HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    public void Dispose() => _http.Dispose();
}
