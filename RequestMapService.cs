using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class RequestMapService : IRequestService
{
    private readonly IGeocoder _geocoder;
    private readonly IStationDetailsService _stationDetailsService;
    private readonly HttpClient _httpClient;
    private bool _disposed = false;

    public RequestMapService(IGeocoder geocoder, IStationDetailsService stationDetailsService)
    {
        _geocoder = geocoder ?? throw new ArgumentNullException(nameof(geocoder));
        _stationDetailsService = stationDetailsService ?? throw new ArgumentNullException(nameof(stationDetailsService));

        _httpClient = new GasBuddyHttpClientBuilder().Build();
    }

    public async Task<List<FuelStation>> GetDataAsync(string startAddress, int cursor = 0)
    {
        try
        {
            // Get coordinates from the address
            var (latitude, longitude) = await _geocoder.GetCoordinatesAsync(startAddress);

            if (latitude == 0 || longitude == 0)
            {
                Console.WriteLine($"Could not get coordinates for: {startAddress}");
                return new List<FuelStation>();
            }

            Console.WriteLine($"Geocoded {startAddress} to coordinates: {latitude}, {longitude}");

            // Get gas stations using the coordinates
            return await GetGasStationsByCoordinatesAsync(latitude, longitude, cursor);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetDataAsync: {ex.Message}");
            throw;
        }
    }

    private async Task<List<FuelStation>> GetGasStationsByCoordinatesAsync(double latitude, double longitude, int cursor)
    {
        try
        {
            // Define a bounding box around the coordinates (approximately 5km x 5km area)
            double latOffset = 0.045; // Roughly 5km in latitude
            double lngOffset = 0.06;  // Roughly 5km in longitude (adjusted for latitude)

            double minLat = latitude - latOffset;
            double maxLat = latitude + latOffset;
            double minLng = longitude - lngOffset;
            double maxLng = longitude + lngOffset;

            // Create request payload based on the curl example
            var payload = new
            {
                fuelTypeId = "1", // Regular fuel
                minLat = minLat,
                maxLat = maxLat,
                minLng = minLng,
                maxLng = maxLng,
                width = 1114,
                height = 600
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            string url = "https://www.gasbuddy.com/gaspricemap/map";

            Console.WriteLine($"Requesting gas stations in bounding box: {minLat},{minLng} to {maxLat},{maxLng}");

            string response = await PostJsonAsync(url, jsonPayload);

            if (string.IsNullOrEmpty(response))
            {
                return new List<FuelStation>();
            }

            // Parse the response
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var mapResponse = JsonSerializer.Deserialize<GasPriceMapResponse>(response, options);

            if (mapResponse == null)
            {
                return new List<FuelStation>();
            }

            var result = new List<FuelStation>();
            var stationIds = new HashSet<int>(); // To avoid duplicates

            // Process primary stations (closest/most relevant)
            if (mapResponse.primaryStations != null)
            {
                foreach (var station in mapResponse.primaryStations)
                {
                    // Skip stations without valid price
                    if (station.price == "--" || string.IsNullOrEmpty(station.price))
                        continue;

                    if (decimal.TryParse(station.price, out decimal price) && !stationIds.Contains(station.id))
                    {
                        stationIds.Add(station.id);

                        // Get detailed station information
                        var details = await _stationDetailsService.GetStationDetailsAsync(station.id);

                        result.Add(new FuelStation(
                            details.Name,
                            details.Address,
                            price // Keep original price (not divided by 100)
                        ));
                    }
                }
            }

            // Also add secondary stations if we need more results
            if (result.Count < 15 && mapResponse.secondaryStations != null)
            {
                foreach (var station in mapResponse.secondaryStations)
                {
                    if (station.price == "--" || string.IsNullOrEmpty(station.price))
                        continue;

                    if (decimal.TryParse(station.price, out decimal price) && !stationIds.Contains(station.id))
                    {
                        stationIds.Add(station.id);

                        // Get detailed station information
                        var details = await _stationDetailsService.GetStationDetailsAsync(station.id);

                        result.Add(new FuelStation(
                            details.Name,
                            details.Address,
                            price // Keep original price (not divided by 100)
                        ));

                        if (result.Count >= 20) break; // Limit total results
                    }
                }
            }

            Console.WriteLine($"Found {result.Count} stations with prices");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetGasStationsByCoordinatesAsync: {ex.Message}");
            return new List<FuelStation>();
        }
    }

    private async Task<string> PostJsonAsync(string url, string data)
    {
        int maxRetries = 5;
        int retryCount = 0;
        int baseDelayMs = 2000; // Start with 2 seconds

        while (retryCount < maxRetries)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(data, Encoding.UTF8, "application/json");
                request.Headers.Referrer = new Uri($"https://www.gasbuddy.com/gaspricemap?fuel=1&z=14&lat=45.4580767734426&lng=-73.4545422846292");

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                if (response.StatusCode == (HttpStatusCode)429)
                {
                    retryCount++;

                    // Check for Retry-After header
                    if (response.Headers.TryGetValues("Retry-After", out var retryAfterValues))
                    {
                        if (int.TryParse(retryAfterValues.FirstOrDefault(), out int retryAfterSeconds))
                        {
                            Console.WriteLine($"Rate limited. Retry-After: {retryAfterSeconds}s. Attempt {retryCount}/{maxRetries}");
                            await Task.Delay(retryAfterSeconds * 1000);
                            continue;
                        }
                    }

                    // Exponential backoff with jitter
                    int delayMs = (int)(baseDelayMs * Math.Pow(2, retryCount - 1) * (0.8 + 0.4 * new Random().NextDouble()));
                    Console.WriteLine($"Rate limited. Waiting {delayMs}ms before retry {retryCount}/{maxRetries}");
                    await Task.Delay(delayMs);
                    continue;
                }

                // Handle other error status codes
                string errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error response: {errorContent}");
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex) when (retryCount < maxRetries - 1)
            {
                retryCount++;
                int delayMs = (int)(baseDelayMs * Math.Pow(2, retryCount - 1) * (0.8 + 0.4 * new Random().NextDouble()));
                Console.WriteLine($"Request failed: {ex.Message}. Retry {retryCount}/{maxRetries} in {delayMs}ms");
                await Task.Delay(delayMs);
            }
        }

        throw new Exception($"Failed after {maxRetries} retries");
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
                _geocoder?.Dispose();
                _stationDetailsService?.Dispose();
            }
            _disposed = true;
        }
    }
}