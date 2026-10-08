using System.Net;
using LocalWeb.Mcp.Security;

namespace LocalWeb.Mcp.Tests;

public sealed class UrlGuardTests
{
    private static UrlGuard CreateGuard(Action<Options.LocalWebOptions>? configure = null)
        => new(TestOptions.Create(configure));

    [Theory]
    // Loopback, by name and by address (including the whole 127.0.0.0/8 range).
    [InlineData("http://localhost/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.5.5.5/")]
    [InlineData("http://[::1]/")]
    // Cloud metadata / link-local.
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[fe80::1]/")]
    // Private ranges.
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://172.31.255.255/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://100.64.0.1/")]
    // Unspecified, multicast, benchmarking.
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://224.0.0.1/")]
    [InlineData("http://198.18.0.1/")]
    // IPv6 unique-local and IPv4-mapped IPv6.
    [InlineData("http://[fc00::1]/")]
    [InlineData("http://[::ffff:10.0.0.1]/")]
    // Non-HTTP schemes.
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/")]
    [InlineData("gopher://example.com/")]
    // Embedded credentials.
    [InlineData("http://user:pass@example.com/")]
    public async Task ValidateAsync_rejects_blocked_urls(string url)
    {
        var guard = CreateGuard();

        await Assert.ThrowsAsync<UrlGuardException>(
            () => guard.ValidateAsync(new Uri(url), CancellationToken.None));
    }

    [Theory]
    [InlineData("http://1.1.1.1/")]
    [InlineData("https://1.1.1.1/")]
    [InlineData("https://[2606:4700:4700::1111]/")]
    public async Task ValidateAsync_allows_public_addresses(string url)
    {
        var guard = CreateGuard();

        var addresses = await guard.ValidateAsync(new Uri(url), CancellationToken.None);

        Assert.NotEmpty(addresses);
    }

    [Fact]
    public async Task ValidateAsync_rejects_disallowed_port()
    {
        var guard = CreateGuard();

        await Assert.ThrowsAsync<UrlGuardException>(
            () => guard.ValidateAsync(new Uri("http://1.1.1.1:8080/"), CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_allows_loopback_when_testing_flag_is_set()
    {
        var guard = CreateGuard(o => o.AllowLoopbackForTests = true);

        var addresses = await guard.ValidateAsync(new Uri("http://127.0.0.1/"), CancellationToken.None);

        Assert.NotEmpty(addresses);
    }

    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("192.168.0.1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("224.0.0.1", true)]
    [InlineData("1.1.1.1", false)]
    [InlineData("93.184.216.34", false)]
    [InlineData("::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    // IPv6 transition/translation forms that embed an IPv4 address.
    [InlineData("64:ff9b::a00:1", true)]
    [InlineData("64:ff9b::7f00:1", true)]
    [InlineData("2002:0a00:0001::", true)]
    [InlineData("::10.0.0.1", true)]
    [InlineData("2001:0000:4136:e378:8000:63bf:3fff:fdd2", true)]
    [InlineData("2606:4700:4700::1111", false)]
    public void IsBlocked_classifies_addresses(string address, bool expected)
    {
        var guard = CreateGuard();

        Assert.Equal(expected, guard.IsBlocked(IPAddress.Parse(address)));
    }
}
