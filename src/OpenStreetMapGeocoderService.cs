using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

public class OpenStreetMapGeocoderService : IGeocoder
{
    private bool _disposed = false;

    public OpenStreetMapGeocoderService()
    {
        // Add headers if not already set (static client is shared)
        if (!HttpClientProvider.Instance.DefaultRequestHeaders.Contains("User-Agent"))
        {
            HttpClientProvider.Instance.DefaultRequestHeaders.Add("User-Agent", "TelegramGasBot/1.0 (contact@example.com)");
            HttpClientProvider.Instance.DefaultRequestHeaders.Add("Referer", "https://yourbot.com");
        }
    }

    public async Task<(double latitude, double longitude)> GetCoordinatesAsync(string location)
    {
        try
        {
            string encodedLocation = HttpUtility.UrlEncode(location);
            // Using Nominatim (OpenStreetMap) - free but rate limited
            string url = $"https://nominatim.openstreetmap.org/search?q={encodedLocation}&format=json&limit=1";

            var response = await HttpClientProvider.Instance.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Nominatim returned {response.StatusCode}");
                return (0, 0);
            }

            string json = await response.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var firstResult = root[0];

                if (firstResult.TryGetProperty("lat", out JsonElement latElement) &&
                    firstResult.TryGetProperty("lon", out JsonElement lonElement))
                {
                    if (double.TryParse(latElement.GetString(), out double lat) &&
                        double.TryParse(lonElement.GetString(), out double lon))
                    {
                        Console.WriteLine($"Nominatim: {location} -> {lat}, {lon}");
                        return (lat, lon);
                    }
                }
            }

            return (0, 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in OpenStreetMapGeocoderService: {ex.Message}");
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
            _disposed = true;
        }
    }
}