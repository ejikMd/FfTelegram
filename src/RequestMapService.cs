using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

public class RequestMapService : IRequestService
{
    private readonly IGeocoder _geocoder;
    private readonly IStationDetailsService _stationDetailsService;
    private HttpClient _currentHttpClient;
    private readonly object _clientLock = new object();
    private readonly Random _random = new Random();
    private readonly SemaphoreSlim _requestThrottler = new SemaphoreSlim(1, 1);
    private DateTime _lastRequestTime = DateTime.MinValue;
    private bool _disposed = false;

    public RequestMapService(
        IGeocoder geocoder,
        IStationDetailsService stationDetailsService)
    {
        _geocoder = geocoder ?? throw new ArgumentNullException(nameof(geocoder));
        _stationDetailsService = stationDetailsService ?? throw new ArgumentNullException(nameof(stationDetailsService));

        // Use the static shared client
        _currentHttpClient = GasBuddyHttpClientBuilder.GetClient();
    }

    public async Task<List<FuelStation>> GetDataAsync(string startAddress)
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
            return await GetGasStationsByCoordinatesAsync(latitude, longitude);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetDataAsync: {ex.Message}");
            throw;
        }
    }

    private async Task<List<FuelStation>> GetGasStationsByCoordinatesAsync(double latitude, double longitude)
    {
        // Add random delay before starting (3-7 seconds)
        await Task.Delay(3000 + _random.Next(4000));

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
                Console.WriteLine($"Received {mapResponse.primaryStations.Count} primary stations ");
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

                        // Small delay between detail requests to avoid rate limiting
                        await Task.Delay(500 + _random.Next(1000));
                    }
                }
            }

            // Also add secondary stations if we need more results
            if (result.Count < 15 && mapResponse.secondaryStations != null)
            {
                Console.WriteLine($"Received {mapResponse.secondaryStations.Count} secondary stations ");
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

                        // Small delay between detail requests
                        await Task.Delay(500 + _random.Next(1000));

                        if (result.Count >= 20) break; // Limit total results
                    }
                }
            }

            Console.WriteLine($"Found {result.Count} stations with prices");

            // Add delay after successful request (2-5 seconds)
            await Task.Delay(2000 + _random.Next(3000));

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetGasStationsByCoordinatesAsync: {ex.Message}");
            return new List<FuelStation>();
        }
    }

    private static int _requestCount = 0;
    private static readonly object _countLock = new object();
    private const int DelayThreshold = 9;
    private const int LongDelayMs = 5000;

    public static void IncrementRequestCount()
    {
        lock (_countLock)
        {
            _requestCount++;
        }
    }

    private async Task<string> PostJsonAsync(string url, string data)
    {
        int maxRetries = 5;
        int retryCount = 0;
        int baseDelayMs = 2000; // Start with 2 seconds

        while (retryCount < maxRetries)
        {
            // Implement throttling
            await _requestThrottler.WaitAsync();
            try
            {
                IncrementRequestCount();
                int currentCount;
                lock (_countLock)
                {
                    currentCount = _requestCount;
                }

                if (currentCount % (DelayThreshold + 1) == 0)
                {
                    Console.WriteLine($"Throttling: 9th request reached. Waiting {LongDelayMs}ms");
                    await Task.Delay(LongDelayMs);
                }

                // Ensure minimum time between requests (5 seconds)
                var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
                if (timeSinceLastRequest.TotalSeconds < 5)
                {
                    var delayMs = (int)((5 - timeSinceLastRequest.TotalSeconds) * 1000) + _random.Next(1000, 3000);
                    Console.WriteLine($"Throttling: Waiting {delayMs}ms before next request");
                    await Task.Delay(delayMs);
                }

                HttpClient client;
                lock (_clientLock)
                {
                    client = _currentHttpClient;
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(data, Encoding.UTF8, "application/json");
                request.Headers.Referrer = new Uri($"https://www.gasbuddy.com/gaspricemap?fuel=1&z=14&lat=45.4580767734426&lng=-73.4545422846292");

                var response = await client.SendAsync(request);
                _lastRequestTime = DateTime.UtcNow;

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                if (response.StatusCode == (HttpStatusCode)429)
                {
                    retryCount++;
                    Console.WriteLine($"Rate limited (429). Attempt {retryCount}/{maxRetries}");

                    // Check for Retry-After header
                    if (response.Headers.TryGetValues("Retry-After", out var retryAfterValues))
                    {
                        if (int.TryParse(retryAfterValues.FirstOrDefault(), out int retryAfterSeconds))
                        {
                            Console.WriteLine($"Server requested Retry-After: {retryAfterSeconds}s");
                            await Task.Delay(retryAfterSeconds * 1000);
                            continue;
                        }
                    }

                    // Exponential backoff with jitter
                    int delayMs = (int)(baseDelayMs * Math.Pow(2, retryCount - 1) * (0.8 + 0.4 * _random.NextDouble()));
                    Console.WriteLine($"Exponential backoff: Waiting {delayMs}ms before retry");
                    await Task.Delay(delayMs);
                    continue;
                }

                // Handle other error status codes
                string errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error response ({response.StatusCode}): {errorContent}");

                // Don't retry on client errors (4xx) except 429
                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500 && response.StatusCode != (HttpStatusCode)429)
                {
                    throw new HttpRequestException($"Client error: {response.StatusCode} - {errorContent}");
                }

                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex) when (retryCount < maxRetries - 1)
            {
                retryCount++;
                int delayMs = (int)(baseDelayMs * Math.Pow(2, retryCount - 1) * (0.8 + 0.4 * _random.NextDouble()));
                Console.WriteLine($"Request failed: {ex.Message}. Retry {retryCount}/{maxRetries} in {delayMs}ms");
                await Task.Delay(delayMs);
            }
            finally
            {
                _requestThrottler.Release();
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
                _currentHttpClient?.Dispose();
                _requestThrottler?.Dispose();
                _geocoder?.Dispose();
                _stationDetailsService?.Dispose();
            }
            _disposed = true;
        }
    }
}