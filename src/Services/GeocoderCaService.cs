using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

public class GeocoderCaService : IGeocoder
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private bool _disposed = false;

    public GeocoderCaService(string apiKey = "")
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TelegramBot/1.0");
        _apiKey = apiKey;
    }

    public async Task<(double latitude, double longitude)> GetCoordinatesAsync(string location)
    {
        try
        {
            string encodedLocation = HttpUtility.UrlEncode(location);
            string url = $"https://geocoder.ca/?locate={encodedLocation}&geoit=XML&json=1";

            if (!string.IsNullOrEmpty(_apiKey))
            {
                url += $"&auth={_apiKey}";
            }

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Geocoder.ca returned {response.StatusCode}");
                return (0, 0);
            }

            string jsonResponse = await response.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            JsonElement root = doc.RootElement;

            // Check if we have coordinates
            if (root.TryGetProperty("latt", out JsonElement lattElement) &&
                root.TryGetProperty("longt", out JsonElement longtElement))
            {
                if (double.TryParse(lattElement.GetString(), out double lat) &&
                    double.TryParse(longtElement.GetString(), out double lng))
                {
                    Console.WriteLine($"Geocoder.ca: {location} -> {lat}, {lng}");
                    return (lat, lng);
                }
            }

            return (0, 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GeocoderCaService: {ex.Message}");
            return (0, 0);
        }
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