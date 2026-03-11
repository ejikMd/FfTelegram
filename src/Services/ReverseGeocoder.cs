using System;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

public class ReverseGeocodeInfo
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}

public interface IReverseGeocoder : IDisposable
{
    Task<ReverseGeocodeInfo> GetAddressAsync(double latitude, double longitude);
}

public class OpenStreetMapReverseGeocoder : IReverseGeocoder
{
    private bool _disposed = false;

    public OpenStreetMapReverseGeocoder()
    {
    }

    public async Task<ReverseGeocodeInfo> GetAddressAsync(double latitude, double longitude)
    {
        try
        {
            string url = $"https://nominatim.openstreetmap.org/reverse?format=json&lat={latitude}&lon={longitude}";
            if (!HttpClientProvider.Instance.DefaultRequestHeaders.Contains("User-Agent"))
                HttpClientProvider.Instance.DefaultRequestHeaders.Add("User-Agent", "GasBuddyBot");

            var response = await HttpClientProvider.Instance.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Reverse geocoding failed with status code: {response.StatusCode}");
                return new ReverseGeocodeInfo 
                { 
                    Name = $"{latitude}, {longitude}",
                    Address = $"{latitude}, {longitude}"
                };
            }

            string content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<ReverseGeocodeResult>(content, options);

            return new ReverseGeocodeInfo
            {
                Name = result?.Name ?? "Unknown",
                Address = result?.GetAddress() ?? $"{latitude}, {longitude}"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in reverse geocoding: {ex.Message}");
            return new ReverseGeocodeInfo 
            { 
                Name = "Unknown",
                Address = $"{latitude}, {longitude}"
            };
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }

    private class ReverseGeocodeResult
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }
        
        [JsonPropertyName("address")]
        public Address Address { get; set; } = new();

        public string GetAddress(){
            return Address?.HouseNumber + " " + Address?.Road + ", " + Address?.City + ", " + Address?.State + " " + Address?.Postcode;
        }
    }

    public class Address
    {
        [JsonPropertyName("house_number")]
        public string? HouseNumber { get; set; }

        [JsonPropertyName("road")]
        public string? Road { get; set; }

        [JsonPropertyName("suburb")]
        public string? Suburb { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("county")]
        public string? County { get; set; }

        [JsonPropertyName("state_district")]
        public string? StateDistrict { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("ISO3166-2-lvl4")]
        public string? ISO31662Lvl4 { get; set; }

        [JsonPropertyName("postcode")]
        public string? Postcode { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("country_code")]
        public string? CountryCode { get; set; }
    }
}