using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

public class HereMapDistanceCalculator : IDistanceCalculator
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private bool _disposed = false;

    public HereMapDistanceCalculator(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new ArgumentNullException(nameof(apiKey), "HERE Maps API key is required");

        _apiKey = apiKey;
        _httpClient = new HttpClient();
    }

    public async Task<string> CalculateDrivingDistanceAsync(string startAddress, string endAddress)
    {
        try
        {
            // Encode addresses for URL
            string encodedStart = Uri.EscapeDataString(startAddress);
            string encodedEnd = Uri.EscapeDataString(endAddress);

            // Build URL for HERE Maps Routing API
            string url = $"https://router.hereapi.com/v8/routes?" +
                        $"transportMode=car" +
                        $"&origin={encodedStart}" +
                        $"&destination={encodedEnd}" +
                        $"&return=summary" +
                        $"&apiKey={_apiKey}";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"HERE Maps API error: {response.StatusCode}");
                return "N/A";
            }

            string content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<HereRoutingResponse>(content, options);

            if (result?.routes?.Count > 0 && result.routes[0].summary != null)
            {
                // Return distance in km
                double distanceInMeters = result.routes[0].summary.distance;
                double distanceInKm = distanceInMeters / 1000.0;
                return $"{distanceInKm:F1} km";
            }

            return "N/A";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error calculating distance: {ex.Message}");
            return "N/A";
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

    private class HereRoutingResponse
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
