using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

/// <summary>
/// Synchronises station name and address data between the cache and GasBuddy.
///
/// Two modes:
///   SyncUnknownAsync — only rows where name = 'Unknown'
///   SyncAllAsync     — every row in the cache
/// </summary>
public sealed class StationSyncService
{
    private readonly StationCacheService        _cache;
    private readonly IStationDetailsService     _detailsService;
    private readonly ILogger<StationSyncService> _logger;

    // Prevent two sync jobs running at the same time.
    private readonly SemaphoreSlim _lock = new(1, 1);

    public StationSyncService(
        StationCacheService cache,
        IStationDetailsService detailsService,
        ILogger<StationSyncService> logger)
    {
        _cache          = cache          ?? throw new ArgumentNullException(nameof(cache));
        _detailsService = detailsService ?? throw new ArgumentNullException(nameof(detailsService));
        _logger         = logger;
    }

    /// <summary>
    /// Fetches fresh details from GasBuddy only for stations cached as 'Unknown'.
    /// Returns a summary of what was updated.
    /// </summary>
    public Task<SyncResult> SyncUnknownAsync() => RunSyncAsync(unknown: true);

    /// <summary>
    /// Fetches fresh details from GasBuddy for every station in the cache.
    /// Returns a summary of what was updated.
    /// </summary>
    public Task<SyncResult> SyncAllAsync() => RunSyncAsync(unknown: false);

    // ── Core ──────────────────────────────────────────────────────────────────

    private async Task<SyncResult> RunSyncAsync(bool unknown)
    {
        if (!await _lock.WaitAsync(0))
            return new SyncResult { Skipped = true, Reason = "Another sync is already running." };

        var mode = unknown ? "unknown-only" : "full";
        _logger.LogInformation("Station sync started ({Mode}).", mode);

        var result = new SyncResult { Mode = mode };

        try
        {
            var stations = unknown
                ? await _cache.GetUnknownStationsAsync()
                : await _cache.GetAllStationsAsync();

            result.Total = stations.Count;
            _logger.LogInformation("Syncing {Count} stations ({Mode}).", stations.Count, mode);

            foreach (var station in stations)
            {
                try
                {
                    var details = await _detailsService.GetStationDetailsAsync(station.Id);

                    // Only write back if we actually got useful data.
                    if (string.IsNullOrWhiteSpace(details.Name) || details.Name == "Unknown")
                    {
                        result.Failed++;
                        _logger.LogWarning("Could not resolve name for station {Id}.", station.Id);
                        continue;
                    }

                    // Preserve existing coordinates if GasBuddy didn't return any.
                    if (details.Latitude == 0 && details.Longitude == 0)
                    {
                        details.Latitude  = station.Lat;
                        details.Longitude = station.Lng;
                    }

                    await _cache.SetAsync(station.Id, details);
                    result.Updated++;

                    _logger.LogInformation(
                        "Station {Id} updated: {OldName} -> {NewName}",
                        station.Id, station.Name, details.Name);
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    _logger.LogError(ex, "Failed to sync station {Id}.", station.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Station sync aborted unexpectedly.");
            result.Error = ex.Message;
        }
        finally
        {
            _lock.Release();
            _logger.LogInformation(
                "Station sync finished ({Mode}): {Updated} updated, {Failed} failed out of {Total}.",
                mode, result.Updated, result.Failed, result.Total);
        }

        return result;
    }
}

public sealed class SyncResult
{
    public string Mode    { get; init; } = string.Empty;
    public int    Total   { get; set; }
    public int    Updated { get; set; }
    public int    Failed  { get; set; }
    public bool   Skipped { get; init; }
    public string Reason  { get; init; } = string.Empty;
    public string Error   { get; set; } = string.Empty;
}