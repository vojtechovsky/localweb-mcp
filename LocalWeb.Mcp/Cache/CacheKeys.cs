using System.Security.Cryptography;
using System.Text;

namespace LocalWeb.Mcp.Cache;

/// <summary>
/// Builds deterministic, fixed-length cache keys for the different operation
/// shapes. Hashing keeps arbitrary query strings and URLs out of the key column.
/// </summary>
public static class CacheKeys
{
    /// <summary>Cache key for a search, independent of the requested result count.</summary>
    /// <param name="query">Raw query; normalized (trimmed, lower-cased).</param>
    /// <param name="language">Language code, or "auto".</param>
    /// <param name="page">1-based page number.</param>
    public static string Search(string query, string language, int page)
    {
        var normalized = string.Join('\u001f',
            query.Trim().ToLowerInvariant(),
            (language ?? "auto").Trim().ToLowerInvariant(),
            page.ToString());

        return "search:" + Sha256(normalized);
    }

    /// <summary>Cache key for a fetched page, scoped by mode (e.g. auto, browser, html).</summary>
    /// <param name="url">Page URL.</param>
    /// <param name="mode">Fetch mode discriminator.</param>
    public static string Fetch(string url, string mode)
    {
        var normalized = string.Join('\u001f', NormalizeUrl(url), mode);
        return "fetch:" + Sha256(normalized);
    }

    /// <summary>
    /// Normalizes a URL for cache lookups: lowercase scheme/host, drop the
    /// fragment, keep the query. Credentials are never present (UrlGuard rejects them).
    /// </summary>
    public static string NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url.Trim().ToLowerInvariant();
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            Host = uri.Host.ToLowerInvariant(),
            Scheme = uri.Scheme.ToLowerInvariant(),
        };

        return builder.Uri.AbsoluteUri;
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes);
    }
}
