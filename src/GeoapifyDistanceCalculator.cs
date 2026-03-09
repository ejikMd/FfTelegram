using System;
using System.Net.Http;
using System.Text.Json;
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

            if (result?.routes?.Count > 0 && result.routes[0].summary != null)
            {
                // Return distance in km
                //double distanceInMeters = result.routes[0].summary.distance;
                //decimal distanceInKm = (decimal)(distanceInMeters / 1000.0);
                return 0m;//$"{distanceInKm:F1} km";
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
        public System.Collections.Generic.List<Route>? routes { get; set; }
    }

    private class Route
    {
        public RouteSummary? summary { get; set; }
    }

    private class RouteSummary
    {
        public double distance { get; set; }
        public int duration { get; set; }
    }
}