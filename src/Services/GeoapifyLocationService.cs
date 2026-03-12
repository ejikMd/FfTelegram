using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

public class GeoapifyLocationService : IDistanceCalculator, IReverseGeocoder
{
    private readonly ILogger<GeoapifyLocationService> _logger;
    private readonly string _apiKey;
    private bool _disposed = false;

    public GeoapifyLocationService(string apiKey, ILogger<GeoapifyLocationService> logger)
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new ArgumentNullException(nameof(apiKey), "Geoapify API key is required");

        _apiKey = apiKey;
        _logger = logger;
    }

    public async Task<decimal> CalculateDrivingDistanceAsync(double startLatitude, double startLongitude, double endLatitude, double endLongitude)
    {
        string content = "";
        try
        {
            string url = $"https://api.geoapify.com/v1/routing?" +
                        $"mode=drive" +
                        $"&waypoints={startLatitude},{startLongitude}|{endLatitude},{endLongitude}" +
                        $"&apiKey={_apiKey}";

            var response = await HttpClientProvider.Instance.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Geoapify API error: {response.StatusCode}");
                return 0m;
            }

            content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<GeoapifyRoutingResponse>(content, options);

            if (result?.Features?.Count > 0 && result.Features[0].Properties != null)
            {
                // Return distance in km
                double distanceInMeters = result.Features[0].Properties.Distance;
                decimal distanceInKm = (decimal)(distanceInMeters / 1000.0);
                return distanceInKm;
            }

            return 0m;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error calculating distance: {ex.Message}");
            _logger.LogError(content);
            return 0m;
        }
    }

    private async Task<string> GetNameAsync(double? latitude, double? longitude)
    {    
        string content = "";
        string url = $"https://api.geoapify.com/v2/places?categories=service.vehicle" +
        $"&format=json" +
        $"&filter=circle:{longitude},{latitude},20" +
        $"&bias=proximity:{longitude},{latitude}" +
        $"&limit=2" +
        $"&apiKey={_apiKey}";
        
        try
        {          
            var response = await HttpClientProvider.Instance.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Finding name failed with status code: {response.StatusCode}");
                return "Unknown";
            }

            content = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<GeoapifyRoutingResponse>(content, options);

            return result?.Features[0].Properties.Name ?? "Unknown";
        }
        catch (Exception ex)
        {
            Console.WriteLine("URL: " + url);
            Console.WriteLine(content);
            Console.WriteLine($"Error in finding name: {ex.Message}");
            return "Unknown";
        }
    }

    public async Task<ReverseGeocodeInfo> GetAddressAsync(double latitude, double longitude)
    {
        try
        {
            string url = $"https://api.geoapify.com/v1/geocode/reverse?" +
            $"format=json" +
            $"&lat={latitude}&lon={longitude}" +
            $"&apiKey={_apiKey}";
            //Console.WriteLine(url);

            var response = await HttpClientProvider.Instance.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Reverse geocoding failed with status code: {response.StatusCode}");
                return new ReverseGeocodeInfo
                {
                    Name = $"{latitude}, {longitude}",
                    Address = $"{latitude}, {longitude}"
                };
            }

            string content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<ReverseGeocodeResult>(content, options);

            //Console.WriteLine(content);
            var stationInfo = result?.Results.Count > 0 ? result.Results[0] : null;

            var stationName = "Unknown";

            if (stationName == "Unknown")
                stationName = await GetNameAsync(stationInfo?.Latitude, stationInfo?.Longitude);
            if (stationName == "Unknown")
                stationName = await GetNameAsync(latitude, longitude);
            if (stationName == "Unknown")
            {
                OverpassPlaceService placeService = new OverpassPlaceService();
                stationName = await placeService.GetNearbyGasStationNameAsync(latitude, longitude);
            }

            if (stationName == "Unknown")
            {
                _logger.LogError("Failed to find name for " + latitude + "," + longitude 
                    + ". StationInfo:" + stationInfo?.Latitude + "," + stationInfo?.Longitude + ", " + stationInfo?.Address);
            }    
                
            return new ReverseGeocodeInfo
            {
                Name = stationName,
                Address = stationInfo?.Address ?? $"{latitude}, {longitude}"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in reverse geocoding: {ex.Message}");
            return new ReverseGeocodeInfo
            {
                Name = "Unknown",
                Address = $"{latitude}, {longitude}"
            };
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }

    private class GeoapifyRoutingResponse
    {
        [JsonPropertyName("features")]
        public List<Feature> Features { get; set; } = new();
    }

    private class Feature
    {
        [JsonPropertyName("properties")]
        public Properties Properties { get; set; } = new();
    }

    private class Properties
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("units")]
        public string? Units { get; set; }

        [JsonPropertyName("distance")]
        public double Distance { get; set; }

        [JsonPropertyName("distance_units")]
        public string? DistanceUnits { get; set; }

        [JsonPropertyName("time")]
        public double Time { get; set; }
    }

    private class Result
    {
        [JsonPropertyName("formatted")]
        public string? Formatted { get; set; }

        [JsonPropertyName("lon")]
        public double? Longitude { get; set; }

        [JsonPropertyName("lat")]
        public double? Latitude { get; set; }

        public string? Address
        {
            get
            {
                if (string.IsNullOrEmpty(Formatted) || char.IsDigit(Formatted[0]))
                    return Formatted;

                int commaIndex = Formatted.IndexOf(',');
                return commaIndex >= 0 
                    ? Formatted[(commaIndex + 1)..].TrimStart()
                    : Formatted;
            }
        }
    }

    private class ReverseGeocodeResult
    {
        [JsonPropertyName("results")]
        public List<Result> Results { get; set; } = new();

        public string Name 
        { 
            get
            {
                var formatted = Results?.FirstOrDefault()?.Formatted;

                if (!string.IsNullOrEmpty(formatted) && !char.IsDigit(formatted[0]))
                    return formatted.Split(',')[0];

                return "Unknown";
            }
        }
    }
}