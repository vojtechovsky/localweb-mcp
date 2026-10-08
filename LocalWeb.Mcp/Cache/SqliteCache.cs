using LocalWeb.Mcp.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalWeb.Mcp.Cache;

/// <summary>
/// Small SQLite-backed cache with a hard TTL per entry. Expired rows are ignored
/// on read and purged at startup. Writes use WAL and are serialized through a
/// semaphore since a single MCP server only ever produces a handful of writers.
/// </summary>
public sealed class SqliteCache : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteCache> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>Initializes the cache and purges expired entries.</summary>
    public SqliteCache(IOptions<LocalWebOptions> options, ILogger<SqliteCache> logger)
    {
        _logger = logger;

        var path = options.Value.CachePath;
        // Relative paths resolve against the binary location, not the client's
        // working directory, so the cache has a stable home after install.
        var fullPath = Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();

        Initialize();
    }

    /// <summary>Returns a non-expired value, or <see langword="null"/>.</summary>
    /// <param name="key">Cache key.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM cache WHERE key = $key AND expires_utc > $now;";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$now", NowMs());

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result as string;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stores a value under a key with a time-to-live.</summary>
    /// <param name="key">Cache key.</param>
    /// <param name="value">Value to store.</param>
    /// <param name="ttl">Time-to-live.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    public async Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = NowMs();
            var expires = now + (long)ttl.TotalMilliseconds;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO cache (key, value, created_utc, expires_utc)
                VALUES ($key, $value, $created, $expires)
                ON CONFLICT(key) DO UPDATE SET
                    value = excluded.value,
                    created_utc = excluded.created_utc,
                    expires_utc = excluded.expires_utc;
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$created", now);
            command.Parameters.AddWithValue("$expires", expires);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Removes a key if present.</summary>
    /// <param name="key">Cache key.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM cache WHERE key = $key;";
            command.Parameters.AddWithValue("$key", key);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            pragma.ExecuteNonQuery();
        }

        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS cache (
                  key TEXT PRIMARY KEY,
                  value TEXT NOT NULL,
                  created_utc INTEGER NOT NULL,
                  expires_utc INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_cache_expires ON cache(expires_utc);
                """;
            schema.ExecuteNonQuery();
        }

        using (var purge = connection.CreateCommand())
        {
            purge.CommandText = "DELETE FROM cache WHERE expires_utc <= $now;";
            purge.Parameters.AddWithValue("$now", NowMs());
            var removed = purge.ExecuteNonQuery();
            if (removed > 0)
            {
                _logger.LogInformation("Purged {Count} expired cache entries at startup.", removed);
            }
        }
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Drains in-flight operations and clears the connection pool.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Drain in-flight operations (they hold the gate) before clearing pools.
        // The semaphore is intentionally not disposed: a caller that already
        // passed the _disposed check may still be waiting on it, and disposing it
        // would throw instead of letting that call finish during shutdown.
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            SqliteConnection.ClearAllPools();
        }
        finally
        {
            _gate.Release();
        }
    }
}
