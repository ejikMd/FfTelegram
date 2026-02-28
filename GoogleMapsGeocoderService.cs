using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

public class GoogleMapsGeocoderService : IGeocoder
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private bool _disposed = false;

    public GoogleMapsGeocoderService(string apiKey)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TelegramBot/1.0");
    }

    public async Task<(double latitude, double longitude)> GetCoordinatesAsync(string location)
    {
        try
        {
            string encodedLocation = HttpUtility.UrlEncode(location);
            string url = $"https://maps.googleapis.com/maps/api/geocode/json?address={encodedLocation}&key={_apiKey}";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Google Maps Geocoding returned {response.StatusCode}");
                return (0, 0);
            }

            string json = await response.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string status = root.GetProperty("status").GetString();

            if (status == "OK" || status == "ZERO_RESULTS")
            {
                if (status == "ZERO_RESULTS")
                {
                    Console.WriteLine($"Google Maps: No results for {location}");
                    return (0, 0);
                }

                var location_ = root.GetProperty("results")[0]
                                   .GetProperty("geometry")
                                   .GetProperty("location");

                double lat = location_.GetProperty("lat").GetDouble();
                double lng = location_.GetProperty("lng").GetDouble();

                Console.WriteLine($"Google Maps: {location} -> {lat}, {lng}");
                return (lat, lng);
            }

            Console.WriteLine($"Google Maps error: {status}");
            return (0, 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GoogleMapsGeocoderService: {ex.Message}");
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