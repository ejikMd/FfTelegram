using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

public static class GasBuddyHttpClientBuilder
{
    private static readonly string _requestVerificationToken = "RV_9tJD3J9geShrpLxWI49iFLhLdXgM5c6Wd_lXYG8GwHnjEwGAQQXz90TvgmPMZc2QtPmSt8TINVxxOP5XwiQL6F8_TVX16fadBkpoTJ481";
    private static readonly string _cfClearance = "Dhb4Hnxusy08_QDXnQ4N4S8WLy7PD5XgFl.XsheX7to-1772229390-1.2.1.1-qFUpsVIwg8hE9j88RBDMsPxUkPhz9W_9fKXYvpcF0Ol2I2dh4C1h1BOP2L.MIXm1YuJzGwU8EH6oWUvHMdu9xLvIBIQ2rdsz2_z.EGvjHUXBo6fOI17wJ_FF9hqkO8BTFTqgmtJl400fIWhpUt3JEGZbHjiQl8jr8uPqm1sMK..4HMi7BEdISeyuyp8k5AzdBX1nWEpMyXmCdnJMB7GsoP5a6T2b7a78NEuZfsnjy9A";
    private static readonly string _cfuvid = "Ugmm2XJsZ7S8L0dr4v7NGTmDeyw1RiTtJxYkUHQzCWI-1772160532888-0.0.1.1-604800000";
    private static readonly string _sessionId = "1wso33swfyxurobzyagau0pe";
    private static readonly string _optanonConsent = "isGpcEnabled=0&datestamp=Fri+Feb+27+2026+14%3A34%3A22+GMT-0500+(Eastern+Standard+Time)&version=202309.1.0&browserGpcFlag=0&isIABGlobal=false&hosts=&consentId=5d8e870c-f206-4c38-998c-39910b23c933&interactionCount=1&landingPath=NotLandingPage&groups=C0004%3A0%2CC0003%3A0%2CC0002%3A0%2CC0001%3A1&geolocation=CA%3BQC&AwaitingReconsent=false";

    private static readonly HttpClient _httpClient;
    private static readonly List<WebProxy> _proxies = new List<WebProxy>();
    private static readonly Random _random = new Random();
    private static readonly object _proxyLock = new object();
    private static int _currentProxyIndex = 0;
    private static readonly Dictionary<WebProxy, DateTime> _proxyFailureTimes = new Dictionary<WebProxy, DateTime>();
    private static readonly TimeSpan _proxyCooldownPeriod = TimeSpan.FromMinutes(5);

    static GasBuddyHttpClientBuilder()
    {
        // Initial setup for the static client
        var httpClientHandler = new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        _httpClient = new HttpClient(httpClientHandler);
        _httpClient.BaseAddress = new Uri("https://www.gasbuddy.com");

        // Set up headers once
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        _httpClient.DefaultRequestHeaders.Add("accept-language", "en-US,en;q=0.9,ru;q=0.8,ro;q=0.7,fr;q=0.6,sv;q=0.5");
        _httpClient.DefaultRequestHeaders.Add("__requestverificationtoken", _requestVerificationToken);
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

    public static HttpClient GetClient() => _httpClient;

    // These methods remain for proxy management if needed, but simplified
    public static void MarkProxyFailure(WebProxy proxy)
    {
        if (proxy != null)
        {
            lock (_proxyLock)
            {
                _proxyFailureTimes[proxy] = DateTime.UtcNow;
            }
        }
    }

    public static void RefreshProxies()
    {
        // Placeholder for proxy refresh logic if needed
    }

}
