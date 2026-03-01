using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

public class GasBuddyHttpClientBuilder
{
    private readonly string _requestVerificationToken;
    private readonly string _cfClearance;
    private readonly string _cfuvid;
    private readonly string _sessionId;
    private readonly string _optanonConsent;
    private readonly bool _useProxyRotation;
    private readonly List<WebProxy> _proxies;
    private readonly Random _random = new Random();
    private readonly object _proxyLock = new object();
    private int _currentProxyIndex = 0;
    private readonly Dictionary<WebProxy, DateTime> _proxyFailureTimes = new Dictionary<WebProxy, DateTime>();
    private readonly TimeSpan _proxyCooldownPeriod = TimeSpan.FromMinutes(5);

    public GasBuddyHttpClientBuilder(bool useProxyRotation = false) 
        : this(
            requestVerificationToken: "RV_9tJD3J9geShrpLxWI49iFLhLdXgM5c6Wd_lXYG8GwHnjEwGAQQXz90TvgmPMZc2QtPmSt8TINVxxOP5XwiQL6F8_TVX16fadBkpoTJ481",
            cfClearance: "Dhb4Hnxusy08_QDXnQ4N4S8WLy7PD5XgFl.XsheX7to-1772229390-1.2.1.1-qFUpsVIwg8hE9j88RBDMsPxUkPhz9W_9fKXYvpcF0Ol2I2dh4C1h1BOP2L.MIXm1YuJzGwU8EH6oWUvHMdu9xLvIBIQ2rdsz2_z.EGvjHUXBo6fOI17wJ_FF9hqkO8BTFTqgmtJl400fIWhpUt3JEGZbHjiQl8jr8uPqm1sMK..4HMi7BEdISeyuyp8k5AzdBX1nWEpMyXmCdnJMB7GsoP5a6T2b7a78NEuZfsnjy9A",
            cfuvid: "Ugmm2XJsZ7S8L0dr4v7NGTmDeyw1RiTtJxYkUHQzCWI-1772160532888-0.0.1.1-604800000",
            sessionId: "1wso33swfyxurobzyagau0pe",
            optanonConsent: "isGpcEnabled=0&datestamp=Fri+Feb+27+2026+14%3A34%3A22+GMT-0500+(Eastern+Standard+Time)&version=202309.1.0&browserGpcFlag=0&isIABGlobal=false&hosts=&consentId=5d8e870c-f206-4c38-998c-39910b23c933&interactionCount=1&landingPath=NotLandingPage&groups=C0004%3A0%2CC0003%3A0%2CC0002%3A0%2CC0001%3A1&geolocation=CA%3BQC&AwaitingReconsent=false",
            useProxyRotation: useProxyRotation
        )
    {
    }
    
    public GasBuddyHttpClientBuilder(
        string requestVerificationToken,
        string cfClearance,
        string cfuvid,
        string sessionId,
        string optanonConsent,
        bool useProxyRotation = false)
    {
        _requestVerificationToken = requestVerificationToken;
        _cfClearance = cfClearance;
        _cfuvid = cfuvid;
        _sessionId = sessionId;
        _optanonConsent = optanonConsent;
        _useProxyRotation = useProxyRotation;

        if (_useProxyRotation)
        {
            _proxies = LoadPublicProxies().GetAwaiter().GetResult();
            Console.WriteLine($"Loaded {_proxies.Count} proxies for rotation");
        }
        else
        {
            _proxies = new List<WebProxy>();
        }
    }

    public HttpClient CreateClient()
    {
        var httpClientHandler = new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        // Configure proxy if rotation is enabled
        if (_useProxyRotation && _proxies.Any())
        {
            var proxy = GetNextWorkingProxy();
            if (proxy != null)
            {
                httpClientHandler.Proxy = proxy;
                httpClientHandler.UseProxy = true;
                Console.WriteLine($"Using proxy: {proxy.Address}");
            }
        }

        var client = new HttpClient(httpClientHandler);
        client.BaseAddress = new Uri("https://www.gasbuddy.com");

        // Set up headers
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        client.DefaultRequestHeaders.Add("accept-language", "en-US,en;q=0.9,ru;q=0.8,ro;q=0.7,fr;q=0.6,sv;q=0.5");
        client.DefaultRequestHeaders.Add("__requestverificationtoken", _requestVerificationToken);
        client.DefaultRequestHeaders.Add("dnt", "1");
        client.DefaultRequestHeaders.Add("origin", "https://www.gasbuddy.com");
        client.DefaultRequestHeaders.Add("priority", "u=1, i");
        client.DefaultRequestHeaders.Add("sec-ch-ua", "\"Not:A-Brand\";v=\"99\", \"Google Chrome\";v=\"145\", \"Chromium\";v=\"145\"");
        client.DefaultRequestHeaders.Add("sec-ch-ua-arch", "\"x86\"");
        client.DefaultRequestHeaders.Add("sec-ch-ua-bitness", "\"64\"");
        client.DefaultRequestHeaders.Add("sec-ch-ua-full-version", "\"145.0.7632.110\"");
        client.DefaultRequestHeaders.Add("sec-ch-ua-full-version-list", "\"Not:A-Brand\";v=\"99.0.0.0\", \"Google Chrome\";v=\"145.0.7632.110\", \"Chromium\";v=\"145.0.7632.110\"");
        client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
        client.DefaultRequestHeaders.Add("sec-ch-ua-model", "\"\"");
        client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
        client.DefaultRequestHeaders.Add("sec-ch-ua-platform-version", "\"19.0.0\"");
        client.DefaultRequestHeaders.Add("sec-fetch-dest", "empty");
        client.DefaultRequestHeaders.Add("sec-fetch-mode", "cors");
        client.DefaultRequestHeaders.Add("sec-fetch-site", "same-origin");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Add("x-requested-with", "XMLHttpRequest");

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

        client.DefaultRequestHeaders.Add("Cookie", cookieHeader);

        return client;
    }

    private async Task<List<WebProxy>> LoadPublicProxies()
    {
        var proxies = new List<WebProxy>();

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            // Multiple proxy sources for redundancy
            var proxySources = new[]
            {
                "https://raw.githubusercontent.com/TheSpeedX/PROXY-List/master/http.txt",
                "https://raw.githubusercontent.com/ShiftyTR/Proxy-List/master/http.txt",
                "https://raw.githubusercontent.com/mertguvencli/http-proxy-list/main/proxy-list/data.txt"
            };

            foreach (var source in proxySources)
            {
                try
                {
                    var response = await client.GetStringAsync(source);
                    var lines = response.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var line in lines.Take(50)) // Limit to first 50 from each source
                    {
                        var parts = line.Trim().Split(':');
                        if (parts.Length == 2 && 
                            IPAddress.TryParse(parts[0], out _) && 
                            int.TryParse(parts[1], out int port) && 
                            port > 0 && port < 65536)
                        {
                            var proxy = new WebProxy($"http://{parts[0]}:{parts[1]}");
                            proxies.Add(proxy);
                        }
                    }

                    Console.WriteLine($"Loaded {proxies.Count} proxies from {source}");
                    if (proxies.Count >= 50) break; // Stop if we have enough
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load proxies from {source}: {ex.Message}");
                }
            }

            // Validate and test proxies
            proxies = await TestProxies(proxies);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading proxies: {ex.Message}");
        }

        return proxies.Distinct().ToList();
    }

    private async Task<List<WebProxy>> TestProxies(List<WebProxy> proxies)
    {
        var workingProxies = new List<WebProxy>();
        var tasks = proxies.Select(async proxy =>
        {
            try
            {
                using var handler = new HttpClientHandler { Proxy = proxy, UseProxy = true };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

                var response = await client.GetAsync("http://httpbin.org/ip");
                if (response.IsSuccessStatusCode)
                {
                    lock (workingProxies)
                    {
                        workingProxies.Add(proxy);
                    }
                    Console.WriteLine($"Proxy {proxy.Address} is working");
                }
            }
            catch
            {
                // Proxy is not working, skip
            }
        });

        await Task.WhenAll(tasks);
        Console.WriteLine($"Found {workingProxies.Count} working proxies");
        return workingProxies;
    }

    private WebProxy? GetNextWorkingProxy()
    {
        lock (_proxyLock)
        {
            if (!_proxies.Any()) return null;

            // Remove proxies that have been on cooldown for too long
            var now = DateTime.UtcNow;
            var expiredFailures = _proxyFailureTimes.Where(kvp => now - kvp.Value > _proxyCooldownPeriod).Select(kvp => kvp.Key).ToList();
            foreach (var proxy in expiredFailures)
            {
                _proxyFailureTimes.Remove(proxy);
            }

            // Try up to 3 times to find a working proxy
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (!_proxies.Any()) break;

                // Rotate to next proxy
                _currentProxyIndex = (_currentProxyIndex + 1) % _proxies.Count;
                var candidate = _proxies[_currentProxyIndex];

                // Check if proxy is on cooldown
                if (_proxyFailureTimes.TryGetValue(candidate, out var failureTime))
                {
                    if (now - failureTime < _proxyCooldownPeriod)
                    {
                        continue; // Skip this proxy, it's still on cooldown
                    }
                    else
                    {
                        // Cooldown expired, remove from failure tracking
                        _proxyFailureTimes.Remove(candidate);
                    }
                }

                return candidate;
            }

            // If all attempts failed, return a random proxy as fallback
            return _proxies[_random.Next(_proxies.Count)];
        }
    }

    public void MarkProxyFailure(WebProxy proxy)
    {
        if (proxy != null)
        {
            lock (_proxyLock)
            {
                _proxyFailureTimes[proxy] = DateTime.UtcNow;
                Console.WriteLine($"Marked proxy {proxy.Address} as failed, cooling down for {_proxyCooldownPeriod.TotalMinutes} minutes");
            }
        }
    }

    public void RefreshProxies()
    {
        Task.Run(async () =>
        {
            Console.WriteLine("Refreshing proxy list...");
            var newProxies = await LoadPublicProxies();
            lock (_proxyLock)
            {
                _proxies.Clear();
                _proxies.AddRange(newProxies);
                _currentProxyIndex = 0;
                _proxyFailureTimes.Clear();
            }
            Console.WriteLine($"Proxy list refreshed: {_proxies.Count} proxies available");
        });
    }
}