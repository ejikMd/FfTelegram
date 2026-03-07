using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class GasBuddyStationDetailsService : IStationDetailsService
{
    private readonly GasBuddyHttpClient _gasBuddyClient;
    private readonly IReverseGeocoder _reverseGeocoder;
    private bool _disposed = false;

    public GasBuddyStationDetailsService()
    {
        _gasBuddyClient = new GasBuddyHttpClient();
        _reverseGeocoder = new OpenStreetMapReverseGeocoder();
    }

    public async Task<StationDetails> GetStationDetailsAsync(int stationId)
    {
        try
        {
            string url = "https://www.gasbuddy.com/gaspricemap/station";
            string referrer = "https://www.gasbuddy.com/gaspricemap?fuel=1&z=14&lat=45.4580767734426&lng=-73.4545422846292";

            // Create payload based on the curl example
            var payload = new
            {
                id = stationId,
                fuelTypeId = "1"
            };

            string jsonPayload = JsonSerializer.Serialize(payload);

            string jsonResponse = await _gasBuddyClient.PostJsonAsync(url, jsonPayload, referrer);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var stationResponse = JsonSerializer.Deserialize<StationDetailsResponse>(jsonResponse, options);

            if (stationResponse?.station == null)
            {
                return CreateFallbackStationDetails(stationId);
            }

            var station = stationResponse.station;

            // Build full address
            string fullAddress = station.Address;
            if (!string.IsNullOrEmpty(station.City))
            {
                fullAddress += $", {station.City}";
            }
            if (!string.IsNullOrEmpty(station.State))
            {
                fullAddress += $", {station.State}";
            }
            if (!string.IsNullOrEmpty(station.ZipCode))
            {
                fullAddress += $" {station.ZipCode}";
            }

            Console.WriteLine($"Information found for station: {stationId}, name {station.Name}");
            return new StationDetails
            {
                Id = station.Id,
                Name = station.Name ?? $"Station {stationId}",
                Address = fullAddress,
                City = station.City ?? string.Empty,
                ZipCode = station.ZipCode ?? string.Empty,
                State = station.State ?? string.Empty,
                Phone = station.Phone ?? string.Empty,
                Latitude = station.Lat,
                Longitude = station.Lng,
                Brand = station.Name ?? "Unknown"
            };
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            Console.WriteLine($"Rate limit reached while getting station details for ID {stationId}. Stopping.");
            throw; // Re-throw to stop the whole process
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting station details for ID {stationId}: {ex.Message}");
            return CreateFallbackStationDetails(stationId);
        }
    }

    public async Task<StationDetails> GetStationDetailsAsync(double latitude, double longitude)
    {
        try
        {
            string address = await _reverseGeocoder.GetAddressAsync(latitude, longitude);
            
            return new StationDetails
            {
                Id = 0,
                Name = "Location",
                Address = address,
                Latitude = latitude,
                Longitude = longitude,
                City = string.Empty,
                State = string.Empty,
                ZipCode = string.Empty,
                Phone = string.Empty,
                Brand = "Unknown"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting station details from coordinates: {ex.Message}");
            return new StationDetails
            {
                Id = 0,
                Name = "Location",
                Address = $"{latitude}, {longitude}",
                Latitude = latitude,
                Longitude = longitude,
                City = string.Empty,
                State = string.Empty,
                ZipCode = string.Empty,
                Phone = string.Empty,
                Brand = "Unknown"
            };
        }
    }

    private StationDetails CreateFallbackStationDetails(int stationId)
    {
        return new StationDetails
        {
            Id = stationId,
            Name = $"Station {stationId}",
            Address = "Address unavailable",
            City = string.Empty,
            ZipCode = string.Empty,
            State = string.Empty,
            Phone = string.Empty,
            Latitude = 0,
            Longitude = 0,
            Brand = "Unknown"
        };
    }

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
                _gasBuddyClient?.Dispose();
                _reverseGeocoder?.Dispose();
            }
            _disposed = true;
        }
    }
}