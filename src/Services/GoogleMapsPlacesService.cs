using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

/// <summary>
/// Retrieves a gas station name by scraping the Google Maps place page
/// for the given coordinates — no API key required.
///
/// URL pattern: https://www.google.com/maps/place/{lat},{lon}
/// The page title contains the place name when a known POI is at that location.
/// </summary>
public class GoogleMapsPlacesService
{
    // e.g. "Shell - Google Maps"  or  "Petro-Canada · Gas station - Google Maps"
    private static readonly Regex TitleRegex =
        new(@"<title>([^<]+?)\s*[-·]\s*(?:Gas station\s*[-·]\s*)?Google Maps</title>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Fallback: structured data / meta og:title
    private static readonly Regex OgTitleRegex =
        new(@"<meta[^>]+property=""og:title""[^>]+content=""([^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns the name of the place (typically a gas station) shown by Google Maps
    /// at the supplied coordinates, or <c>null</c> when no named POI is found.
    /// </summary>
    /// <param name="latitude">WGS-84 latitude  (e.g. 45.3494219)</param>
    /// <param name="longitude">WGS-84 longitude (e.g. -73.6499431)</param>
    /// <param name="radiusMeters">Unused — kept for API compatibility with the Places SDK version.</param>
    public async Task<string?> GetNearbyGasStationNameAsync(
        double latitude,
        double longitude,
        double radiusMeters = 20)
    {
        // Google Maps resolves coordinates to the nearest named POI automatically.
        string url = $"https://www.google.com/maps/place/{latitude},{longitude}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        // Mimic a real browser so Google returns the full HTML page.
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/124.0.0.0 Safari/537.36");
        request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
        request.Headers.Add("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

        try
        {
            var response = await HttpClientProvider.Instance.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[GoogleMapsPlacesService] HTTP {response.StatusCode} for {url}");
                return "Unknown";
            }

            string html = await response.Content.ReadAsStringAsync();

            //Console.WriteLine(html);
            // 1. Try <title> tag — most reliable when Google resolves to a named place
            var titleMatch = TitleRegex.Match(html);
            if (titleMatch.Success)
            {
                string name = titleMatch.Groups[1].Value.Trim();
                // If Google returned raw coordinates in the title there is no named POI
                if (!IsCoordinateString(name))
                    return name;
            }

            // 2. Fallback: og:title meta tag
            var ogMatch = OgTitleRegex.Match(html);
            if (ogMatch.Success)
            {
                string name = ogMatch.Groups[1].Value.Trim();
                if (!IsCoordinateString(name))
                    return name;
            }

            return "Unknown";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GoogleMapsPlacesService] Exception: {ex.Message}");
            return "Unknown";
        }
    }

    /// <summary>
    /// Returns true when the string looks like a raw coordinate pair
    /// (meaning Google did not resolve it to a named place).
    /// </summary>
    private static bool IsCoordinateString(string value) =>
        Regex.IsMatch(value, @"^-?\d+\.\d+,\s*-?\d+\.\d+$");
}