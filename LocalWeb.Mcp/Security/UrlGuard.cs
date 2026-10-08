using System.Net;
using System.Net.Sockets;
using LocalWeb.Mcp.Options;
using Microsoft.Extensions.Options;

namespace LocalWeb.Mcp.Security;

/// <summary>
/// Central SSRF protection. Every outbound request (HTTP or Playwright) must be
/// validated here before it is allowed to leave the process.
///
/// Rules enforced:
/// <list type="bullet">
/// <item>Only schemes listed in <see cref="LocalWebOptions.AllowedSchemes"/>.</item>
/// <item>Only ports listed in <see cref="LocalWebOptions.AllowedPorts"/>.</item>
/// <item>No URLs containing embedded credentials (<c>user:pass@host</c>).</item>
/// <item>DNS is resolved and <em>every</em> returned address is inspected.</item>
/// <item>Loopback, link-local, private, CGNAT, multicast, unspecified and their
/// IPv6 equivalents are rejected.</item>
/// </list>
///
/// <see cref="ResolveValidatedAsync"/> is used by the HTTP client's
/// <c>ConnectCallback</c> so the socket is opened against the exact address that
/// was validated, which closes the DNS-rebinding window.
/// </summary>
public sealed class UrlGuard
{
    private readonly LocalWebOptions _options;

    public UrlGuard(IOptions<LocalWebOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Validates a URL and returns the resolved addresses. Throws
    /// <see cref="UrlGuardException"/> when the URL is not allowed.
    /// </summary>
    public async Task<IReadOnlyList<IPAddress>> ValidateAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (!url.IsAbsoluteUri)
        {
            throw new UrlGuardException("Only absolute URLs are allowed.");
        }

        if (!_options.AllowedSchemes.Contains(url.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            throw new UrlGuardException(
                $"Scheme '{url.Scheme}' is not allowed. Allowed schemes: {string.Join(", ", _options.AllowedSchemes)}.");
        }

        if (!string.IsNullOrEmpty(url.UserInfo))
        {
            throw new UrlGuardException("URLs containing credentials are not allowed.");
        }

        if (!_options.AllowedPorts.Contains(url.Port))
        {
            throw new UrlGuardException(
                $"Port {url.Port} is not allowed. Allowed ports: {string.Join(", ", _options.AllowedPorts)}.");
        }

        var addresses = await ResolveAsync(url.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Count == 0)
        {
            throw new UrlGuardException($"Host '{url.Host}' did not resolve to any address.");
        }

        // Reject if ANY resolved address is blocked. This defends against
        // DNS answers that mix public and private addresses.
        var blocked = addresses.Where(IsBlocked).ToArray();
        if (blocked.Length > 0)
        {
            // Do not echo the resolved address: it would turn the tool into an
            // internal-DNS oracle for the caller.
            throw new UrlGuardException($"Host '{url.Host}' resolves to a disallowed address.");
        }

        return addresses;
    }

    /// <summary>
    /// Resolves and validates a host/port pair, returning a single address that
    /// is safe to connect to. Used as the HTTP client's <c>ConnectCallback</c>.
    /// </summary>
    public async Task<IPAddress> ResolveValidatedAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (!_options.AllowedPorts.Contains(port))
        {
            throw new UrlGuardException(
                $"Port {port} is not allowed. Allowed ports: {string.Join(", ", _options.AllowedPorts)}.");
        }

        var addresses = await ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        foreach (var address in addresses)
        {
            if (!IsBlocked(address))
            {
                return address;
            }
        }

        throw new UrlGuardException($"Host '{host}' resolves only to blocked addresses.");
    }

    /// <summary>
    /// True when the address must never be contacted. Exposed for tests.
    /// </summary>
    public bool IsBlocked(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (_options.AllowLoopbackForTests && IsLoopback(address))
        {
            return false;
        }

        if (IsLoopback(address))
        {
            return true;
        }

        switch (address.AddressFamily)
        {
            case AddressFamily.InterNetwork:
                var b = address.GetAddressBytes();
                if (b[0] == 10) return true;                                  // 10.0.0.0/8
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;     // 172.16.0.0/12
                if (b[0] == 192 && b[1] == 168) return true;                  // 192.168.0.0/16
                if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;    // 100.64.0.0/10 (CGNAT)
                if (b[0] == 169 && b[1] == 254) return true;                  // 169.254.0.0/16 (link-local)
                if (b[0] == 0) return true;                                   // 0.0.0.0/8 (unspecified)
                if (b[0] >= 224) return true;                                 // 224.0.0.0/4 multicast + reserved
                if (b[0] == 192 && b[1] == 0 && b[2] == 0) return true;       // 192.0.0.0/24 special purpose
                if (b[0] == 198 && (b[1] == 18 || b[1] == 19)) return true;   // 198.18.0.0/15 benchmarking
                return false;

            case AddressFamily.InterNetworkV6:
                if (IPAddress.IPv6Any.Equals(address) || IPAddress.IPv6None.Equals(address)) return true;
                if (address.IsIPv6LinkLocal) return true;                     // fe80::/10
                if (address.IsIPv6Multicast) return true;                     // ff00::/8

                var v6 = address.GetAddressBytes();

                // IPv4-compatible ::/96 (also covers :: and ::1) and the
                // IPv4-mapped range are normalized above, but be explicit here.
                if (IsAllZero(v6, 0, 12)) return true;                        // ::a.b.c.d and ::

                // IPv6 transition/translation forms that embed an IPv4 address
                // can reach internal IPv4 networks on NAT64/6to4/Teredo hosts.
                if (v6[0] == 0x00 && v6[1] == 0x64 && v6[2] == 0xFF && v6[3] == 0x9B) return true; // NAT64 64:ff9b::/96 and 64:ff9b:1::/48
                if (v6[0] == 0x20 && v6[1] == 0x02) return true;              // 6to4 2002::/16
                if (v6[0] == 0x20 && v6[1] == 0x01 && v6[2] == 0x00 && v6[3] == 0x00) return true; // Teredo 2001::/32

                if ((v6[0] & 0xFE) == 0xFC) return true;                      // fc00::/7 unique local
                if (v6[0] == 0xFE && (v6[1] & 0xC0) == 0xC0) return true;     // fec0::/10 site local (deprecated)
                return false;

            default:
                return true;
        }
    }

    private static bool IsLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        // IPAddress.IsLoopback only covers 127.0.0.1 / ::1, so cover all of 127.0.0.0/8.
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return address.GetAddressBytes()[0] == 127;
        }

        return false;
    }

    private static bool IsAllZero(byte[] bytes, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (bytes[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new UrlGuardException("The URL does not contain a host.");
        }

        if (IPAddress.TryParse(host, out var literal))
        {
            return [literal];
        }

        try
        {
            return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            throw new UrlGuardException($"Could not resolve host '{host}'.", ex);
        }
    }
}
