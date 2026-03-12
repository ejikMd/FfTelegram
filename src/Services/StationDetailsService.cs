using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

public interface IStationDetailsService : IDisposable
{
    Task<StationDetails> GetStationDetailsAsync(int stationId);
    Task<StationDetails> GetStationDetailsAsync(double latitude, double longitude);
}

public class StationDetails
{
    public int    Id        { get; set; }
    public string Name      { get; set; } = string.Empty;
    public string Address   { get; set; } = string.Empty;
    public string City      { get; set; } = string.Empty;
    public string ZipCode   { get; set; } = string.Empty;
    public string State     { get; set; } = string.Empty;
    public string Phone     { get; set; } = string.Empty;
    public double Latitude  { get; set; }
    public double Longitude { get; set; }
    public string Brand     { get; set; } = string.Empty;
}

public class StationDetailsService : IStationDetailsService
{
    private readonly IReverseGeocoder  _reverseGeocoder;
    private readonly GasBuddyHttpClient _gasBuddyClient;
    private bool _disposed = false;

    public StationDetailsService(IReverseGeocoder reverseGeocoder, GasBuddyHttpClient gasBuddyClient)
    {
        _reverseGeocoder = reverseGeocoder  ?? throw new ArgumentNullException(nameof(reverseGeocoder));
        _gasBuddyClient  = gasBuddyClient   ?? throw new ArgumentNullException(nameof(gasBuddyClient));
    }

    /// <summary>
    /// Fetches station name and address from GasBuddy using the station's numeric id.
    /// Falls back to a placeholder if the request fails.
    /// </summary>
    public async Task<StationDetails> GetStationDetailsAsync(int stationId)
    {
        try
        {
            var payload  = $"{{\"id\":{stationId},\"fuelTypeId\":\"1\"}}";
            var response = await _gasBuddyClient.PostJsonAsync(
                "https://www.gasbuddy.com/gaspricemap/station",
                payload,
                referrer: "https://www.gasbuddy.com/gaspricemap");

            if (string.IsNullOrWhiteSpace(response))
                return Fallback(stationId);

            using var doc = JsonDocument.Parse(response);

            // Response shape: { "station": { "Id", "Name", "Address", "City", ... }, "prices": [...] }
            if (!doc.RootElement.TryGetProperty("station", out var station))
                return Fallback(stationId);

            var name    = GetString(station, "Name");
            var address = GetString(station, "Address");
            var city    = GetString(station, "City");
            var state   = GetString(station, "State");
            var zip     = GetString(station, "ZipCode");
            var phone   = GetString(station, "Phone");
            var brand   = name; // On GasBuddy the station Name is the brand (e.g. "Couche-Tard")

            var fullAddress = string.Join(", ",
                new[] { address, city, state, zip }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));

            double lat = 0, lng = 0;
            if (station.TryGetProperty("Lat", out var latEl)) lat = latEl.GetDouble();
            if (station.TryGetProperty("Lng", out var lngEl)) lng = lngEl.GetDouble();

            //Console.WriteLine($"[StationDetails] id={stationId} name={name} address={fullAddress}");

            return new StationDetails
            {
                Id        = stationId,
                Name      = string.IsNullOrWhiteSpace(name)        ? "Unknown" : name,
                Address   = string.IsNullOrWhiteSpace(fullAddress) ? "Unknown" : fullAddress,
                City      = city,
                State     = state,
                ZipCode   = zip,
                Phone     = phone,
                Latitude  = lat,
                Longitude = lng,
                Brand     = brand,
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StationDetails] Error fetching station {stationId}: {ex.Message}");
            return Fallback(stationId);
        }
    }

    /// <summary>
    /// Fetches station name and address by reverse-geocoding the given coordinates.
    /// </summary>
    public async Task<StationDetails> GetStationDetailsAsync(double latitude, double longitude)
    {
        try
        {
            var geocodeInfo = await _reverseGeocoder.GetAddressAsync(latitude, longitude);

            return new StationDetails
            {
                Id        = 0,
                Name      = geocodeInfo.Name,
                Address   = geocodeInfo.Address,
                Latitude  = latitude,
                Longitude = longitude,
                Brand     = "Unknown",
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StationDetails] Error reverse-geocoding ({latitude},{longitude}): {ex.Message}");
            return new StationDetails
            {
                Id        = 0,
                Name      = "Unknown",
                Address   = $"{latitude}, {longitude}",
                Latitude  = latitude,
                Longitude = longitude,
                Brand     = "Unknown",
            };
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static StationDetails Fallback(int stationId) => new()
    {
        Id      = stationId,
        Name    = "Unknown",
        Address = "Unknown",
        Brand   = "Unknown",
    };

    private static string GetString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? string.Empty
            : string.Empty;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _reverseGeocoder?.Dispose();
            }
            _disposed = true;
        }
    }
}