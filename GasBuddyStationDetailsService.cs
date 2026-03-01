using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class GasBuddyStationDetailsService : IStationDetailsService
{
    private readonly HttpClient _httpClient;
    private bool _disposed = false;

    public GasBuddyStationDetailsService()
    {
        bool useProxyRotation = true; // Set to false if you want to test without proxies
        var httpClientBuilder = new GasBuddyHttpClientBuilder(useProxyRotation: useProxyRotation);
        
        _httpClient = httpClientBuilder.CreateClient();
    }

    public async Task<StationDetails> GetStationDetailsAsync(int stationId)
    {
        try
        {
            string url = "https://www.gasbuddy.com/gaspricemap/station";

            // Create payload based on the curl example
            var payload = new
            {
                id = stationId,
                fuelTypeId = "1"
            };

            string jsonPayload = JsonSerializer.Serialize(payload);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);

            // Set content with content-type header
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // Add referer header
            request.Headers.Referrer = new Uri($"https://www.gasbuddy.com/gaspricemap?fuel=1&z=14&lat=45.4580767734426&lng=-73.4545422846292");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error response from station details: {errorContent}");
                return CreateFallbackStationDetails(stationId);
            }

            string jsonResponse = await response.Content.ReadAsStringAsync();

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
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting station details for ID {stationId}: {ex.Message}");
            return CreateFallbackStationDetails(stationId);
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
                _httpClient?.Dispose();
            }
            _disposed = true;
        }
    }
}