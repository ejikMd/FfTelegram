using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class RequestMapService : IRequestService
{
    private readonly IGeocoder _geocoder;
    private readonly HttpClient _httpClient;
    private readonly string _requestVerificationToken;
    private readonly string _cfClearance;
    private readonly string _cfuvid;
    private readonly string _sessionId;
    private readonly string _optanonConsent;
    private bool _disposed = false;

    public RequestMapService(IGeocoder geocoder)
    {
        _geocoder = geocoder ?? throw new ArgumentNullException(nameof(geocoder));

        // These values should be refreshed periodically
        _requestVerificationToken = "RV_9tJD3J9geShrpLxWI49iFLhLdXgM5c6Wd_lXYG8GwHnjEwGAQQXz90TvgmPMZc2QtPmSt8TINVxxOP5XwiQL6F8_TVX16fadBkpoTJ481";
        _cfClearance = "Dhb4Hnxusy08_QDXnQ4N4S8WLy7PD5XgFl.XsheX7to-1772229390-1.2.1.1-qFUpsVIwg8hE9j88RBDMsPxUkPhz9W_9fKXYvpcF0Ol2I2dh4C1h1BOP2L.MIXm1YuJzGwU8EH6oWUvHMdu9xLvIBIQ2rdsz2_z.EGvjHUXBo6fOI17wJ_FF9hqkO8BTFTqgmtJl400fIWhpUt3JEGZbHjiQl8jr8uPqm1sMK..4HMi7BEdISeyuyp8k5AzdBX1nWEpMyXmCdnJMB7GsoP5a6T2b7a78NEuZfsnjy9A";
        _cfuvid = "Ugmm2XJsZ7S8L0dr4v7NGTmDeyw1RiTtJxYkUHQzCWI-1772160532888-0.0.1.1-604800000";
        _sessionId = "1wso33swfyxurobzyagau0pe";
        _optanonConsent = "isGpcEnabled=0&datestamp=Fri+Feb+27+2026+14%3A34%3A22+GMT-0500+(Eastern+Standard+Time)&version=202309.1.0&browserGpcFlag=0&isIABGlobal=false&hosts=&consentId=5d8e870c-f206-4c38-998c-39910b23c933&interactionCount=1&landingPath=NotLandingPage&groups=C0004%3A0%2CC0003%3A0%2CC0002%3A0%2CC0001%3A1&geolocation=CA%3BQC&AwaitingReconsent=false";

        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri("https://www.gasbuddy.com");

        // Set up headers
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        _httpClient.DefaultRequestHeaders.Add("accept-language", "en-US,en;q=0.9,ru;q=0.8,ro;q=0.7,fr;q=0.6,sv;q=0.5");
        _httpClient.DefaultRequestHeaders.Add("__requestverificationtoken", _requestVerificationToken);
        //_httpClient.DefaultRequestHeaders.Add("content-type", "application/json; charset=UTF-8");
        _httpClient.DefaultRequestHeaders.Add("dnt", "1");
        _httpClient.DefaultRequestHeaders.Add("origin", "https://www.gasbuddy.com");
        _httpClient.DefaultRequestHeaders.Add("priority", "u=1, i");
        _httpClient.DefaultRequestHeaders.Add("sec-ch-ua", "\"Not:A-Brand\";v=\"99\", \"Google Chrome\";v=\"145\", \"Chromium\";v=\"145\"");
        _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-arch", "\"x86\"");
        _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-bitness", "\"64\"");
        _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-full-version", "\"145.0.7632.110\"");
        _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-full-version-list", "\"Not:A-Brand\";v=\"99.0.0.0\", \"Google Chrome\";v=\"145.0.7632.110\", \"Chromium\";v=\"145.0.7632.110\"");
        _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
        _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-model", "\"\"");
        _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
        _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-platform-version", "\"19.0.0\"");
        _httpClient.DefaultRequestHeaders.Add("sec-fetch-dest", "empty");
        _httpClient.DefaultRequestHeaders.Add("sec-fetch-mode", "cors");
        _httpClient.DefaultRequestHeaders.Add("sec-fetch-site", "same-origin");
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("x-requested-with", "XMLHttpRequest");

        // Add cookies
        var cookieHeader = $"_cfuvid={_cfuvid}; " +
                          $"OptanonAlertBoxClosed=2026-02-27T02:48:54.669Z; " +
                          $"ASP.NET_SessionId={_sessionId}; " +
                          $"PreferredFuelId=1; " +
                          $"PreferredFuelType=A; " +
                          $"__RequestVerificationToken=JLhXEZgEKRXz341pbmsqv9lEfuCpphH5pIAN9MKcRRMWolzxYD8KwDqVbG1etmFfKDrVeRMWyPGYAXzM3Sh-a_B-t2cg1VVprjlDBpY-rvw1; " +
                          $"OptanonConsent={_optanonConsent}; " +
                          $"g_state={{\"i_l\":0,\"i_ll\":1772220863300,\"i_b\":\"AaGllvcqellUM9iUM0DEmj5fL71NzvgCavzu6Ba1AXQ\",\"i_e\":{{\"enable_itp_optimization\":0}}}}; " +
                          $"__cf_bm=VeDk1ufO65lTDtazoSjP3JDvWJXfVxsyJ6BNr76XIQw-1772229332-1.0.1.1-3.51z4zAsADHTmVUx3zFAv5anKrYKutqLIX4Hl75KQZVn1hwaKJC9hRNrp9KWt52ru1jJq7SGBd3dVaCAUSVClmGvld4qMeBGCAqrlB7SCA; " +
                          $"cf_clearance={_cfClearance}";

        _httpClient.DefaultRequestHeaders.Add("Cookie", cookieHeader);
    }

    public async Task<List<FuelStation>> GetDataAsync(string startAddress, int cursor = 0)
    {
        try
        {
            // Get coordinates from the address
            var (latitude, longitude) = await _geocoder.GetCoordinatesAsync(startAddress);

            if (latitude == 0 || longitude == 0)
            {
                Console.WriteLine($"Could not get coordinates for: {startAddress}");
                return new List<FuelStation>();
            }

            Console.WriteLine($"Geocoded {startAddress} to coordinates: {latitude}, {longitude}");

            // Get gas stations using the coordinates
            return await GetGasStationsByCoordinatesAsync(latitude, longitude);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetDataAsync: {ex.Message}");
            throw;
        }
    }

    private async Task<List<FuelStation>> GetGasStationsByCoordinatesAsync(double latitude, double longitude)
    {
        try
        {
            // Define a bounding box around the coordinates (approximately 5km x 5km area)
            double latOffset = 0.045; // Roughly 5km in latitude
            double lngOffset = 0.06;  // Roughly 5km in longitude (adjusted for latitude)

            double minLat = latitude - latOffset;
            double maxLat = latitude + latOffset;
            double minLng = longitude - lngOffset;
            double maxLng = longitude + lngOffset;

            // Create request payload based on the curl example
            var payload = new
            {
                fuelTypeId = "1", // Regular fuel
                minLat = minLat,
                maxLat = maxLat,
                minLng = minLng,
                maxLng = maxLng,
                width = 1114,
                height = 600
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            string url = "https://www.gasbuddy.com/gaspricemap/map";

            Console.WriteLine($"Requesting gas stations in bounding box: {minLat},{minLng} to {maxLat},{maxLng}");

            string response = await PostJsonAsync(url, jsonPayload);

            if (string.IsNullOrEmpty(response))
            {
                return new List<FuelStation>();
            }

            // Parse the response
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var mapResponse = JsonSerializer.Deserialize<GasPriceMapResponse>(response, options);

            if (mapResponse == null)
            {
                return new List<FuelStation>();
            }

            var result = new List<FuelStation>();

            // Process primary stations (closest/most relevant)
            if (mapResponse.primaryStations != null)
            {
                foreach (var station in mapResponse.primaryStations)
                {
                    // Skip stations without valid price
                    if (station.price == "--" || string.IsNullOrEmpty(station.price))
                        continue;

                    if (decimal.TryParse(station.price, out decimal price))
                    {
                        // We'll add a placeholder name - this will be resolved later by a separate service
                        result.Add(new FuelStation(
                            $"Station ID: {station.id}",
                            $"Coordinates: {station.lat}, {station.lng}",
                            price / 100 // Convert from cents to dollars if needed
                        ));
                    }
                }
            }

            // Also add secondary stations if we need more results
            if (result.Count < 10 && mapResponse.secondaryStations != null)
            {
                foreach (var station in mapResponse.secondaryStations)
                {
                    if (station.price == "--" || string.IsNullOrEmpty(station.price))
                        continue;

                    if (decimal.TryParse(station.price, out decimal price))
                    {
                        result.Add(new FuelStation(
                            $"Station ID: {station.id}",
                            $"Coordinates: {station.lat}, {station.lng}",
                            price / 100
                        ));

                        if (result.Count >= 15) break; // Limit total results
                    }
                }
            }

            Console.WriteLine($"Found {result.Count} stations with prices");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetGasStationsByCoordinatesAsync: {ex.Message}");
            return new List<FuelStation>();
        }
    }

    private async Task<string> PostJsonAsync(string url, string data)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(data, Encoding.UTF8, "application/json");

            // Add referer header
            request.Headers.Referrer = new Uri($"https://www.gasbuddy.com/gaspricemap?fuel=1&z=14&lat=45.4580767734426&lng=-73.4545422846292");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error response: {errorContent}");
                response.EnsureSuccessStatusCode();
            }

            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in PostJsonAsync: {ex.Message}");
            throw;
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
                _geocoder?.Dispose();
            }
            _disposed = true;
        }
    }
}

// Response model classes
public class GasPriceMapResponse
{
    public List<GasStationMapItem> primaryStations { get; set; } = new List<GasStationMapItem>();
    public List<GasStationMapItem> secondaryStations { get; set; } = new List<GasStationMapItem>();
}

public class GasStationMapItem
{
    public int id { get; set; }
    public double lat { get; set; }
    public double lng { get; set; }
    public string price { get; set; } = string.Empty;
    public bool iscash { get; set; }
    public string tme { get; set; } = string.Empty;
    public int brand_id { get; set; }
}