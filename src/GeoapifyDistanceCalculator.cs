using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

public class GeoapifyDistanceCalculator : IDistanceCalculator
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private bool _disposed = false;

    public GeoapifyDistanceCalculator(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new ArgumentNullException(nameof(apiKey), "Geoapify API key is required");
  
        _apiKey = apiKey;
        _httpClient = new HttpClient();
    }

    public async Task<decimal> CalculateDrivingDistanceAsync(double startLatitude, double startLongitude, double endLatitude, double endLongitude)
    {
        try
        {          
            string url = $"https://api.geoapify.com/v1/routing?" +
                        $"mode=drive" +
                        $"&waypoints={startLatitude},{startLongitude}|{endLatitude},{endLongitude}" +
                        $"&apiKey={_apiKey}";
            
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Geoapify API error: {response.StatusCode}");
                return 0m;
            }

            string content = await response.Content.ReadAsStringAsync();
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
            Console.WriteLine($"Error calculating distance: {ex.Message}");
            return 0m;
        } 
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient?.Dispose();
            _disposed = true;
        }
    }

    private class GeoapifyRoutingResponse
    {
        [JsonPropertyName("features")]
        public List<Feature> Features { get; set; }
    }

    public class Feature
    {
        [JsonPropertyName("properties")]
        public Properties Properties { get; set; }
    }

    public class Properties
    {
        [JsonPropertyName("units")]
        public string Units { get; set; }

        [JsonPropertyName("distance")]
        public int Distance { get; set; }

        [JsonPropertyName("distance_units")]
        public string DistanceUnits { get; set; }

        [JsonPropertyName("time")]
        public double Time { get; set; }
    }
}