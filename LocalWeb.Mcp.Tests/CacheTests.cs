using LocalWeb.Mcp.Cache;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalWeb.Mcp.Tests;

public sealed class CacheTests : IAsyncLifetime
{
    private string _path = string.Empty;
    private SqliteCache _cache = null!;

    public Task InitializeAsync()
    {
        _path = Path.Combine(Path.GetTempPath(), "localweb-mcp-tests", Guid.NewGuid() + ".db");
        _cache = new SqliteCache(TestOptions.Create(o => o.CachePath = _path), NullLogger<SqliteCache>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _cache.DisposeAsync();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var file = _path + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public async Task Set_then_get_roundtrips()
    {
        await _cache.SetAsync("k", "value", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal("value", await _cache.GetAsync("k", CancellationToken.None));
    }

    [Fact]
    public async Task Set_overwrites_existing_value()
    {
        await _cache.SetAsync("k", "one", TimeSpan.FromMinutes(5), CancellationToken.None);
        await _cache.SetAsync("k", "two", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal("two", await _cache.GetAsync("k", CancellationToken.None));
    }

    [Fact]
    public async Task Get_returns_null_for_missing_key()
    {
        Assert.Null(await _cache.GetAsync("missing", CancellationToken.None));
    }

    [Fact]
    public async Task Get_returns_null_for_expired_entry()
    {
        await _cache.SetAsync("k", "value", TimeSpan.FromMinutes(-1), CancellationToken.None);

        Assert.Null(await _cache.GetAsync("k", CancellationToken.None));
    }

    [Fact]
    public async Task Remove_deletes_entry()
    {
        await _cache.SetAsync("k", "value", TimeSpan.FromMinutes(5), CancellationToken.None);
        await _cache.RemoveAsync("k", CancellationToken.None);

        Assert.Null(await _cache.GetAsync("k", CancellationToken.None));
    }

    [Fact]
    public void CacheKeys_are_deterministic_and_normalize_urls()
    {
        Assert.Equal(
            CacheKeys.Search("Hello", "EN", 1, 8),
            CacheKeys.Search("  hello  ", "en", 1, 8));

        Assert.Equal(
            CacheKeys.Fetch("https://Example.com/Path#frag", "auto"),
            CacheKeys.Fetch("https://example.com/Path", "auto"));

        Assert.NotEqual(
            CacheKeys.Fetch("https://example.com/a", "auto"),
            CacheKeys.Fetch("https://example.com/a", "browser"));
    }
}
