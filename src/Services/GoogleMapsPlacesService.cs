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

    // Matches raw coordinate pairs — means Google did not resolve to a named place
    private static readonly Regex CoordinateRegex =
        new(@"^-?\d+\.\d+,\s*-?\d+\.\d+$", RegexOptions.Compiled);

    /// <summary>
    /// Returns the name of the place (typically a gas station) shown by Google Maps
    /// at the supplied coordinates, or "Unknown" when no named POI is found.
    /// </summary>
    public async Task<string> GetNearbyGasStationNameAsync(
        double latitude,
        double longitude,
        double radiusMeters = 20)
    {
        string url = $"https://www.google.com/maps/place/{latitude},{longitude}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/124.0.0.0 Safari/537.36");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        request.Headers.TryAddWithoutValidation("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

        try
        {
            using var response = await HttpClientProvider.Instance.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[GoogleMapsPlacesService] HTTP {response.StatusCode} for {url}");
                return "Unknown";
            }

            if (response.Content is null)
            {
                Console.WriteLine("[GoogleMapsPlacesService] Response content is null.");
                return "Unknown";
            }

            string html = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrEmpty(html))
                return "Unknown";

            // 1. Try <title> tag — most reliable when Google resolves to a named place
            var titleMatch = TitleRegex.Match(html);
            if (titleMatch.Success)
            {
                string name = titleMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(name) && !IsCoordinateString(name))
                    return name;
            }

            // 2. Fallback: og:title meta tag
            var ogMatch = OgTitleRegex.Match(html);
            if (ogMatch.Success)
            {
                string name = ogMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(name) && !IsCoordinateString(name))
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

    private static bool IsCoordinateString(string value) =>
        CoordinateRegex.IsMatch(value);
}
