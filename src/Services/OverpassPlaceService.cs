using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class OverpassPlaceService
{
  public async Task<string> GetNearbyGasStationNameAsync(
      double latitude,
      double longitude,
      double radiusMeters = 20)
  {
      // Validate coordinates
      if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180)
      {
          Console.WriteLine($"Invalid coordinates: {latitude}, {longitude}");
          return "Unknown";
      }

      // Overpass API query
      string query = $"[out:json];(node[\"amenity\"=\"fuel\"](around:{radiusMeters},{latitude},{longitude});way[\"amenity\"=\"fuel\"](around:{radiusMeters},{latitude},{longitude});rel[\"amenity\"=\"fuel\"](around:{radiusMeters},{latitude},{longitude}););out center;";

      using (HttpClient client = new HttpClient())
      {
          try
          {
              // Set timeout
              client.Timeout = TimeSpan.FromSeconds(30);

              // Create request content
              var content = new StringContent(query, Encoding.UTF8, "text/plain");

              // Send request to Overpass API
              HttpResponseMessage response = await client.PostAsync("https://overpass-api.de/api/interpreter", content);

              // Check if request was successful
              if (!response.IsSuccessStatusCode)
              {
                  Console.WriteLine($"Overpass API returned status code {response.StatusCode}");
                  return "Unknown";
              }

              // Read response
              string jsonResponse = await response.Content.ReadAsStringAsync();

              // Parse JSON response
              using JsonDocument doc = JsonDocument.Parse(jsonResponse);
              JsonElement root = doc.RootElement;

              // Check if elements exist
              if (!root.TryGetProperty("elements", out JsonElement elements) || elements.GetArrayLength() == 0)
              {
                  Console.WriteLine("Overpass No gas stations found within the specified radius");
                  return "Unknown";
              }

              // Get the first gas station
              JsonElement firstStation = elements[0];

              // Extract name information
              string stationName = GetStationName(firstStation);

              // Return name or "Unknown" if null/empty
              return string.IsNullOrEmpty(stationName) ? "Unknown" : stationName;
          }
          catch (HttpRequestException ex)
          {
              Console.WriteLine($"Overpass Network error: {ex.Message}");
              return "Unknown";
          }
          catch (JsonException ex)
          {
              Console.WriteLine($"Overpass Error parsing response: {ex.Message}");
              return "Unknown";
          }
          catch (TaskCanceledException)
          {
              Console.WriteLine("Overpass Request timed out");
              return "Unknown";
          }
          catch (Exception ex)
          {
              Console.WriteLine($"Overpass Unexpected error: {ex.Message}");
              return "Unknown";
          }
      }
  }

  // Helper method to extract station name from various tags
  private string GetStationName(JsonElement station)
  {
      try
      {
          if (station.TryGetProperty("tags", out JsonElement tags))
          {
              // Check for name (most common)
              if (tags.TryGetProperty("name", out JsonElement name) && 
                  !string.IsNullOrWhiteSpace(name.GetString()))
              {
                  return name.GetString();
              }

              // Check for brand (common for chains)
              if (tags.TryGetProperty("brand", out JsonElement brand) && 
                  !string.IsNullOrWhiteSpace(brand.GetString()))
              {
                  return brand.GetString();
              }

              // Check for operator
              if (tags.TryGetProperty("operator", out JsonElement operator_) && 
                  !string.IsNullOrWhiteSpace(operator_.GetString()))
              {
                  return operator_.GetString();
              }

              // Check for name:en (English name)
              if (tags.TryGetProperty("name:en", out JsonElement nameEn) && 
                  !string.IsNullOrWhiteSpace(nameEn.GetString()))
              {
                  return nameEn.GetString();
              }
          }
      }
      catch (Exception ex)
      {
          Console.WriteLine($"Overpass Error extracting station name: {ex.Message}");
      }

      return null;
  }
}