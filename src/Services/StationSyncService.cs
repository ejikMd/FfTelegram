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

    // Timeout to prevent deadlocks
    private static readonly TimeSpan SyncTimeout = TimeSpan.FromMinutes(5);

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
    public Task<SyncResult> SyncUnknownAsync() => RunSyncAsync(unknownOnly: true);

    /// <summary>
    /// Fetches fresh details from GasBuddy for every station in the cache.
    /// Returns a summary of what was updated.
    /// </summary>
    public Task<SyncResult> SyncAllAsync() => RunSyncAsync(unknownOnly: false);

    // ── Core ──────────────────────────────────────────────────────────────────

    private async Task<SyncResult> RunSyncAsync(bool unknownOnly)
    {
        var mode = unknownOnly ? "unknown-only" : "full";

        // Try to acquire the lock with a timeout to prevent deadlocks
        bool lockAcquired = false;
        try
        {
            lockAcquired = await _lock.WaitAsync(SyncTimeout);

            if (!lockAcquired)
            {
                _logger.LogWarning("Station sync ({Mode}) timed out after {Timeout} - another sync may be stuck.", 
                    mode, SyncTimeout);
                return new SyncResult 
                { 
                    Skipped = true, 
                    Reason = $"Sync timed out after {SyncTimeout.TotalMinutes} minutes. Another sync may be hung."
                };
            }

            _logger.LogInformation("Station sync started ({Mode}).", mode);
            return await ExecuteSyncAsync(unknownOnly, mode);
        }
        catch (ObjectDisposedException)
        {
            // Semaphore was disposed - service is shutting down
            _logger.LogWarning("Station sync ({Mode}) cancelled - service is shutting down.", mode);
            return new SyncResult 
            { 
                Skipped = true, 
                Reason = "Sync cancelled - service is shutting down."
            };
        }
        finally
        {
            if (lockAcquired)
            {
                try
                {
                    _lock.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Semaphore already disposed during shutdown - ignore
                }
                catch (SemaphoreFullException)
                {
                    // This shouldn't happen, but log just in case
                    _logger.LogError("Semaphore was released too many times during {Mode} sync.", mode);
                }
            }
        }
    }

    private async Task<SyncResult> ExecuteSyncAsync(bool unknownOnly, string mode)
    {
        var result = new SyncResult { Mode = mode };

        try
        {
            var stations = unknownOnly
                ? await _cache.GetUnknownStationsAsync()
                : await _cache.GetAllStationsAsync();

            result.Total = stations.Count;

            if (stations.Count == 0)
            {
                _logger.LogInformation("No stations to sync ({Mode}).", mode);
                return result;
            }

            _logger.LogInformation("Syncing {Count} stations ({Mode}).", stations.Count, mode);

            foreach (var station in stations)
            {
                // Check if semaphore is still held (not disposed) periodically
                if (_lock.CurrentCount == 0)
                {
                    try
                    {
                        // Quick test to see if semaphore is still valid
                        await _lock.WaitAsync(TimeSpan.FromMilliseconds(1));
                        _lock.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        _logger.LogWarning("Station sync ({Mode}) interrupted - service shutting down.", mode);
                        result.Error = "Sync interrupted - service shutting down";
                        return result;
                    }
                }

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
            _logger.LogInformation(
                "Station sync finished ({Mode}): {Updated} updated, {Failed} failed out of {Total}.",
                mode, result.Updated, result.Failed, result.Total);
        }

        return result;
    }

    /// <summary>
    /// Attempts to cancel any ongoing sync operation by disposing the semaphore.
    /// Called during service shutdown.
    /// </summary>
    public void Cancel()
    {
        try
        {
            _lock.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while cancelling station sync.");
        }
    }
}

/// <summary>
/// Result of a station sync operation
/// </summary>
public sealed class SyncResult
{
    /// <summary>
    /// The sync mode that was used (unknown-only or full)
    /// </summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>
    /// Total number of stations processed
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// Number of stations successfully updated
    /// </summary>
    public int Updated { get; set; }

    /// <summary>
    /// Number of stations that failed to update
    /// </summary>
    public int Failed { get; set; }

    /// <summary>
    /// Whether the sync was skipped (another sync running or timeout)
    /// </summary>
    public bool Skipped { get; init; }

    /// <summary>
    /// Reason why the sync was skipped
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Error message if the sync failed
    /// </summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>
    /// Whether the sync completed successfully (no errors and at least one update)
    /// </summary>
    public bool IsSuccessful => !Skipped && string.IsNullOrEmpty(Error) && Updated > 0;

    /// <summary>
    /// Whether the sync completed with warnings (some failures but no fatal error)
    /// </summary>
    public bool HasWarnings => !Skipped && string.IsNullOrEmpty(Error) && Failed > 0;

    /// <summary>
    /// Returns a summary string representation of the result
    /// </summary>
    public override string ToString()
    {
        if (Skipped)
            return $"Sync skipped: {Reason}";

        if (!string.IsNullOrEmpty(Error))
            return $"Sync failed: {Error}";

        return $"Sync completed: {Updated} updated, {Failed} failed out of {Total}";
    }
}