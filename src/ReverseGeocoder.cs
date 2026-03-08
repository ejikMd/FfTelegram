using System;
using System.Threading.Tasks;

public interface IReverseGeocoder : IDisposable
{
    Task<string> GetAddressAsync(double latitude, double longitude);
}

public class OpenStreetMapReverseGeocoder : IReverseGeocoder
{
    private readonly HttpClient _httpClient;
    private bool _disposed = false;

    public OpenStreetMapReverseGeocoder()
    {
        _httpClient = new HttpClient();
    }

    public async Task<string> GetAddressAsync(double latitude, double longitude)
    {
        try
        {
            string url = $"https://nominatim.openstreetmap.org/reverse?format=json&lat={latitude}&lon={longitude}";
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "GasBuddyBot");

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Reverse geocoding failed with status code: {response.StatusCode}");
                return $"{latitude}, {longitude}";
            }

            string content = await response.Content.ReadAsStringAsync();
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = System.Text.Json.JsonSerializer.Deserialize<ReverseGeocodeResult>(content, options);

            Console.WriteLine($"Reverse geocoding result for {latitude}, {longitude}: {result?.DisplayName}");

            return result?.DisplayName ?? $"{latitude}, {longitude}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in reverse geocoding: {ex.Message}");
            return $"{latitude}, {longitude}";
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

    private class ReverseGeocodeResult
    {
        public string? DisplayName { get; set; }
    }
}