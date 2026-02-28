using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class GasBuddyStationDetailsService : IStationDetailsService
{
    private readonly HttpClient _httpClient;
    private readonly string _requestVerificationToken;
    private readonly string _cfClearance;
    private readonly string _cfuvid;
    private readonly string _sessionId;
    private readonly string _optanonConsent;
    private bool _disposed = false;

    public GasBuddyStationDetailsService()
    {
        // These values should be refreshed periodically (same as in RequestMapService)
        _requestVerificationToken = "RV_9tJD3J9geShrpLxWI49iFLhLdXgM5c6Wd_lXYG8GwHnjEwGAQQXz90TvgmPMZc2QtPmSt8TINVxxOP5XwiQL6F8_TVX16fadBkpoTJ481";
        _cfClearance = "Dhb4Hnxusy08_QDXnQ4N4S8WLy7PD5XgFl.XsheX7to-1772229390-1.2.1.1-qFUpsVIwg8hE9j88RBDMsPxUkPhz9W_9fKXYvpcF0Ol2I2dh4C1h1BOP2L.MIXm1YuJzGwU8EH6oWUvHMdu9xLvIBIQ2rdsz2_z.EGvjHUXBo6fOI17wJ_FF9hqkO8BTFTqgmtJl400fIWhpUt3JEGZbHjiQl8jr8uPqm1sMK..4HMi7BEdISeyuyp8k5AzdBX1nWEpMyXmCdnJMB7GsoP5a6T2b7a78NEuZfsnjy9A";
        _cfuvid = "Ugmm2XJsZ7S8L0dr4v7NGTmDeyw1RiTtJxYkUHQzCWI-1772160532888-0.0.1.1-604800000";
        _sessionId = "1wso33swfyxurobzyagau0pe";
        _optanonConsent = "isGpcEnabled=0&datestamp=Fri+Feb+27+2026+14%3A34%3A22+GMT-0500+(Eastern+Standard+Time)&version=202309.1.0&browserGpcFlag=0&isIABGlobal=false&hosts=&consentId=5d8e870c-f206-4c38-998c-39910b23c933&interactionCount=1&landingPath=NotLandingPage&groups=C0004%3A0%2CC0003%3A0%2CC0002%3A0%2CC0001%3A1&geolocation=CA%3BQC&AwaitingReconsent=false";

        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri("https://www.gasbuddy.com");

        // Set up headers (same as in RequestMapService)
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        _httpClient.DefaultRequestHeaders.Add("accept-language", "en-US,en;q=0.9,ru;q=0.8,ro;q=0.7,fr;q=0.6,sv;q=0.5");
        _httpClient.DefaultRequestHeaders.Add("__requestverificationtoken", _requestVerificationToken);
        _httpClient.DefaultRequestHeaders.Add("content-type", "application/json; charset=UTF-8");
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

    public async Task<StationDetails> GetStationDetailsAsync(int stationId)
    {
        try
        {
            string url = "https://www.gasbuddy.com/gaspricemap/station";

            // Create payload based on the curl example
            var payload = new
            {
                id = stationId,
                fuelTypeId = "1"
            };

            string jsonPayload = JsonSerializer.Serialize(payload);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // Add referer header
            request.Headers.Referrer = new Uri($"https://www.gasbuddy.com/gaspricemap?fuel=1&z=14&lat=45.4580767734426&lng=-73.4545422846292");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error response from station details: {errorContent}");
                return CreateFallbackStationDetails(stationId);
            }

            string jsonResponse = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var stationResponse = JsonSerializer.Deserialize<StationDetailsResponse>(jsonResponse, options);

            if (stationResponse?.station == null)
            {
                return CreateFallbackStationDetails(stationId);
            }

            var station = stationResponse.station;

            // Build full address
            string fullAddress = station.Address;
            if (!string.IsNullOrEmpty(station.City))
            {
                fullAddress += $", {station.City}";
            }
            if (!string.IsNullOrEmpty(station.State))
            {
                fullAddress += $", {station.State}";
            }
            if (!string.IsNullOrEmpty(station.ZipCode))
            {
                fullAddress += $" {station.ZipCode}";
            }

            return new StationDetails
            {
                Id = station.Id,
                Name = station.Name ?? $"Station {stationId}",
                Address = fullAddress,
                City = station.City ?? string.Empty,
                ZipCode = station.ZipCode ?? string.Empty,
                State = station.State ?? string.Empty,
                Phone = station.Phone ?? string.Empty,
                Latitude = station.Lat,
                Longitude = station.Lng,
                Brand = station.Name ?? "Unknown"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting station details for ID {stationId}: {ex.Message}");
            return CreateFallbackStationDetails(stationId);
        }
    }

    private StationDetails CreateFallbackStationDetails(int stationId)
    {
        return new StationDetails
        {
            Id = stationId,
            Name = $"Station {stationId}",
            Address = "Address unavailable",
            City = string.Empty,
            ZipCode = string.Empty,
            State = string.Empty,
            Phone = string.Empty,
            Latitude = 0,
            Longitude = 0,
            Brand = "Unknown"
        };
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

// Response model classes
public class StationDetailsResponse
{
    public StationData station { get; set; } = new StationData();
}

public class StationData
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int BrandId { get; set; }
}