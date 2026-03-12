using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;

/// <summary>
/// Caches gas station name + address in the Replit PostgreSQL database
/// (table: stations, columns: id, name, address).
///
/// The primary key is the GasBuddy station id from <c>GasStationMapItem.id</c>.
///
/// Schema (created automatically on startup via <see cref="EnsureTableAsync"/>):
///   CREATE TABLE IF NOT EXISTS stations (
///       id      INT  PRIMARY KEY,
///       name    TEXT NOT NULL,
///       address TEXT NOT NULL
///   );
/// </summary>
public sealed class StationCacheService : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<StationCacheService> _logger;

    public StationCacheService(ILogger<StationCacheService> logger)
    {
        _logger = logger;

        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? throw new InvalidOperationException(
                "DATABASE_URL environment variable is not set. " +
                "Enable the PostgreSQL database in your Replit project.");

        _connectionString = BuildConnectionString(databaseUrl);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Ensures the <c>stations</c> table exists. Call once at startup.</summary>
    public async Task EnsureTableAsync()
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS stations (
                id      INT  PRIMARY KEY,
                name    TEXT NOT NULL,
                address TEXT NOT NULL
            );
            """;

        await using var conn = await OpenAsync();
        await using var cmd  = new NpgsqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("Station cache table ready.");
    }

    /// <summary>
    /// Returns cached <see cref="StationDetails"/> for the given station id,
    /// or <c>null</c> if no entry exists.
    /// </summary>
    public async Task<StationDetails?> GetAsync(int stationId)
    {
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd  = new NpgsqlCommand(
                "SELECT name, address FROM stations WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", stationId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var name    = reader.GetString(0);
                var address = reader.GetString(1);
                _logger.LogDebug("Cache HIT for station {Id} → {Name}", stationId, name);
                return new StationDetails { Name = name, Address = address };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache read failed for station {Id}.", stationId);
        }

        _logger.LogDebug("Cache MISS for station {Id}.", stationId);
        return null;
    }

    /// <summary>Inserts or updates a station entry in the cache.</summary>
    public async Task SetAsync(int stationId, StationDetails details)
    {
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd  = new NpgsqlCommand("""
                INSERT INTO stations (id, name, address)
                VALUES (@id, @name, @address)
                ON CONFLICT (id) DO UPDATE
                    SET name    = EXCLUDED.name,
                        address = EXCLUDED.address;
                """, conn);

            cmd.Parameters.AddWithValue("id",      stationId);
            cmd.Parameters.AddWithValue("name",    details.Name);
            cmd.Parameters.AddWithValue("address", details.Address);

            await cmd.ExecuteNonQueryAsync();
            _logger.LogDebug("Cache SET for station {Id} → {Name}", stationId, details.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache write failed for station {Id}.", stationId);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>
    /// Converts a postgres:// URI (as provided by Replit) to an Npgsql
    /// key=value connection string.
    /// e.g. postgres://user:pass@host/dbname?sslmode=require
    ///   →  Host=host;Database=dbname;Username=user;Password=pass;SSL Mode=Require
    /// </summary>
    private static string BuildConnectionString(string url)
    {
        // Accept both postgres:// and postgresql:// schemes.
        url = url.Replace("postgresql://", "postgres://");

        if (!url.StartsWith("postgres://"))
            return url; // Already a key=value string — use as-is.

        var uri      = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        var user     = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var host     = uri.Host;
        var port     = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.TrimStart('/');

        // Parse optional query parameters (e.g. sslmode=require).
        var sslMode = "Prefer";
        var query   = uri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                sslMode = kv.Length > 1 ? kv[1] : sslMode;
        }

        return $"Host={host};Port={port};Database={database};Username={user};Password={password};SSL Mode={sslMode};Trust Server Certificate=true";
    }

    public void Dispose() { /* Npgsql connections are disposed per-call */ }
}