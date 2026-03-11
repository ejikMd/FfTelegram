using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class RequestService : IRequestService
{
    private bool _disposed = false;
    private readonly string _gbcsrf;
    private readonly string _cfClearance;
    private readonly string _cfuvid;
    private readonly string _sessionId;
    private readonly string _optanonConsent;

    public RequestService()
    {
        // These values should be obtained from a real browser session
        // You'll need to update these periodically as they expire
        _gbcsrf = "1.gMGRJ9mYhV7G/Ixw";
        _cfClearance = "3fZ7ivWngL5oPpQvZH719Z8tdeRzhNC1smMKIhJwwC4-1772161333-1.2.1.1-bdkuE5Fe7lsMPl8UT3blesgMXHhgVEtnoXTWn3XrvUjMmH2U9ayff4nUEf86R.E.GqmuVHODvVCqXwSUyTttCv09bQkMbruSDUmslfihUohm266XAh2eEOHnFckZ36OgViv6f64f6M4MxMe97Oq8cyWjQYz628TUSKrxmEiHnN.0AloYBeZVeosTtipeqAe_sVuVLE7tw8N5LbxIDXWuO41Cyr0elujOwDo0DRKpVDE";
        _cfuvid = "Ugmm2XJsZ7S8L0dr4v7NGTmDeyw1RiTtJxYkUHQzCWI-1772160532888-0.0.1.1-604800000";
        _sessionId = "1wso33swfyxurobzyagau0pe";
        _optanonConsent = "isGpcEnabled=0&datestamp=Fri+Feb+27+2026+10%3A31%3A59+GMT-0500+(Eastern+Standard+Time)&version=202309.1.0&browserGpcFlag=0&isIABGlobal=false&hosts=&consentId=5d8e870c-f206-4c38-998c-39910b23c933&interactionCount=1&landingPath=NotLandingPage&groups=C0004%3A0%2CC0003%3A0%2CC0002%3A0%2CC0001%3A1&geolocation=CA%3BQC&AwaitingReconsent=false";

        HttpClientProvider.Instance.BaseAddress = new Uri("https://www.gasbuddy.com");
        HttpClientProvider.Instance.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("accept-language", "en-US,en;q=0.9,ru;q=0.8,ro;q=0.7,fr;q=0.6,sv;q=0.5");
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("apollo-require-preflight", "true");
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("dnt", "1");
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("gbcsrf", _gbcsrf);
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("origin", "https://www.gasbuddy.com");
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("priority", "u=1, i");
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("sec-ch-ua", "\"Not:A-Brand\";v=\"99\", \"Google Chrome\";v=\"145\", \"Chromium\";v=\"145\"");
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("sec-ch-ua-arch", "\"x86\"");
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("sec-ch-ua-bitness", "\"64\"");
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("sec-ch-ua-full-version", "\"145.0.7632.110\"");
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("sec-ch-ua-full-version-list", "\"Not:A-Brand\";v=\"99.0.0.0\", \"Google Chrome\";v=\"145.0.7632.110\", \"Chromium\";v=\"145.0.7632.110\"");
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("sec-ch-ua-model", "\"\"");
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("sec-ch-ua-platform-version", "\"19.0.0\"");
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("sec-fetch-dest", "empty");
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("sec-fetch-mode", "cors");
        HttpClientProvider.Instance.DefaultRequestHeaders.Add("sec-fetch-site", "same-origin");
        HttpClientProvider.Instance.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36");

        // Add cookies
        var cookieHeader = $"_cfuvid={_cfuvid}; " +
                          $"OptanonAlertBoxClosed=2026-02-27T02:48:54.669Z; " +
                          $"ASP.NET_SessionId={_sessionId}; " +
                          $"PreferredFuelId=1; " +
                          $"PreferredFuelType=A; " +
                          $"__RequestVerificationToken=JLhXEZgEKRXz341pbmsqv9lEfuCpphH5pIAN9MKcRRMWolzxYD8KwDqVbG1etmFfKDrVeRMWyPGYAXzM3Sh-a_B-t2cg1VVprjlDBpY-rvw1; " +
                          $"cf_clearance={_cfClearance}; " +
                          $"OptanonConsent={_optanonConsent}";

        HttpClientProvider.Instance.DefaultRequestHeaders.Add("Cookie", cookieHeader);
    }

    public async Task<List<FuelStation>> GetDataAsync(string startAddress)
    {
        int cursor = 0;        
        try
        {
            var allStations = new List<FuelStation>();
            int totalRequests = 0;
            int maxRequests = 3; // Set to a default value or passed via parameter if needed
            int cursorStep = 5;
            int expectedPerPage = 5;

            // If a cursor is provided, we might just want that specific page, 
            // but the request was to move the loop functionality here.
            // Assuming we want to fetch multiple pages if cursor is 0.
            
            int startPage = cursor / cursorStep;
            int endPage = cursor > 0 ? startPage + 1 : maxRequests;

            for (int i = startPage; i < endPage; i++)
            {
                int currentCursor = i * cursorStep;
                Console.WriteLine($"Making request {i + 1}/{maxRequests} with cursor: {currentCursor}");

                try
                {
                    var fuelStationData = await GetHtmlDocumentAsync(startAddress, currentCursor);

                    if (fuelStationData?.data?.locationBySearchTerm?.stations?.results == null)
                    {
                        break;
                    }

                    var stations = new List<FuelStation>();
                    foreach (var station in fuelStationData.data.locationBySearchTerm.stations.results)
                    {
                        var name = station.name;
                        var address = $"{station.address.line1}, {station.address.locality}, {station.address.postalCode}, {station.address.region}";

                        decimal price = 0;
                        if (station.prices != null && station.prices.Count > 0)
                        {
                            price = station.prices[0].credit.price;
                        }

                        stations.Add(new FuelStation(name, address, 0.0, 0.0, price));
                    }

                    if (stations.Count > 0)
                    {
                        allStations.AddRange(stations);
                        Console.WriteLine($"Added {stations.Count} stations from cursor {currentCursor}");

                        if (stations.Count < expectedPerPage)
                        {
                            Console.WriteLine($"Received {stations.Count} stations (less than expected {expectedPerPage}), stopping pagination");
                            break;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"No stations returned for cursor {currentCursor}");
                        break;
                    }

                    totalRequests++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in request {i + 1} with cursor {currentCursor}: {ex.Message}");
                    totalRequests++;
                }

                if (i < endPage - 1)
                {
                    await Task.Delay(500);
                }
            }

            Console.WriteLine($"Total requests made: {totalRequests}, Total stations collected: {allStations.Count}");
            return allStations;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetDataAsync: {ex.Message}");
            throw;
        }
    }

    private async Task<FuelStationData?> GetHtmlDocumentAsync(string startAddress, int cursor = 0)
    {
        try
        {
            string url = "https://www.gasbuddy.com/graphql";

            // Updated GraphQL query with cursor parameter
            string payload = @"{
                ""operationName"": ""LocationBySearchTerm"",
                ""variables"": {
                    ""fuel"": 1,
                    ""lang"": ""en"",
                    ""maxAge"": 0,
                    ""search"": """ + startAddress + @""",
                    ""cursor"": " + (cursor > 0 ? "\"" + cursor + "\"" : "null") + @"
                },
                ""query"": ""query LocationBySearchTerm($brandId: Int, $cursor: String, $fuel: Int, $lang: String, $lat: Float, $lng: Float, $maxAge: Int, $search: String) {\n  locationBySearchTerm(\n    lat: $lat\n    lng: $lng\n    search: $search\n    priority: \""locality\""\n  ) {\n    countryCode\n    displayName\n    latitude\n    longitude\n    regionCode\n    stations(\n      brandId: $brandId\n      cursor: $cursor\n      fuel: $fuel\n      lat: $lat\n      lng: $lng\n      maxAge: $maxAge\n      priority: \""locality\""\n    ) {\n      count\n      cursor {\n        next\n        __typename\n      }\n      results {\n        address {\n          country\n          line1\n          line2\n          locality\n          postalCode\n          region\n          __typename\n        }\n        badges(lang: $lang) {\n          badgeId\n          callToAction\n          campaignId\n          clickTrackingUrl\n          description\n          detailsImageUrl\n          detailsImpressionTrackingUrls\n          imageUrl\n          impressionTrackingUrls\n          targetUrl\n          title\n          __typename\n        }\n        brands {\n          brandId\n          brandingType\n          imageUrl\n          name\n          __typename\n        }\n        distance\n        emergencyStatus {\n          hasDiesel {\n            nickname\n            reportStatus\n            updateDate\n            __typename\n          }\n          hasGas {\n            nickname\n            reportStatus\n            updateDate\n            __typename\n          }\n          hasPower {\n            nickname\n            reportStatus\n            updateDate\n            __typename\n          }\n          __typename\n        }\n        enterprise\n        fuels\n        hasActiveOutage\n        id\n        isFuelmanSite\n        name\n        offers {\n          discounts {\n            grades\n            highlight\n            pwgbDiscount\n            receiptDiscount\n            __typename\n          }\n          highlight\n          id\n          types\n          use\n          __typename\n        }\n        payStatus {\n          isPayAvailable\n          __typename\n        }\n        prices {\n          cash {\n            nickname\n            postedTime\n            price\n            formattedPrice\n            __typename\n          }\n          credit {\n            nickname\n            postedTime\n            price\n            formattedPrice\n            __typename\n          }\n          discount\n          fuelProduct\n          __typename\n        }\n        priceUnit\n        ratingsCount\n        starRating\n        __typename\n      }\n      __typename\n    }\n    trends {\n      areaName\n      country\n      today\n      todayLow\n      trend\n      __typename\n    }\n    __typename\n  }\n}\n""
            }";

            string json = await PostJsonAsync(url, payload);

            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<FuelStationData>(json, options);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetHtmlDocumentAsync: {ex.Message}");
            throw;
        }
    }

    private async Task<string> PostJsonAsync(string url, string data)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(data, Encoding.UTF8, "application/json");

            // Add referer header
            request.Headers.Referrer = new Uri($"https://www.gasbuddy.com/home?search={Uri.EscapeDataString("J4W 2L1")}&fuel=1&method=all&maxAge=0");

            var response = await HttpClientProvider.Instance.SendAsync(request);

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
            _disposed = true;
        }
    }
}