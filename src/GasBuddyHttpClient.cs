using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class GasBuddyHttpClient : IDisposable
{
    private readonly HttpClient _client;
    private static int _requestCount = 0;
    private static readonly object _countLock = new object();
    private const int DelayThreshold = 9;
    private const int LongDelayMs = 5000;
    private readonly SemaphoreSlim _requestThrottler = new SemaphoreSlim(1, 1);
    private DateTime _lastRequestTime = DateTime.MinValue;
    private readonly Random _random = new Random();
    private bool _disposed = false;

    public GasBuddyHttpClient()
    {
        _client = GasBuddyHttpClientBuilder.GetClient();
    }

    public static void IncrementRequestCount()
    {
        lock (_countLock)
        {
            _requestCount++;
        }
    }

    public async Task<string> PostJsonAsync(string url, string data, string? referrer = null)
    {
        int maxRetries = 5;
        int retryCount = 0;
        int baseDelayMs = 2000;

        while (retryCount < maxRetries)
        {
            await _requestThrottler.WaitAsync();
            try
            {
                IncrementRequestCount();
                int currentCount;
                lock (_countLock)
                {
                    currentCount = _requestCount;
                }

                if (currentCount % (DelayThreshold + 1) == 0)
                {
                    Console.WriteLine($"Throttling: 9th request reached. Waiting {LongDelayMs}ms");
                    await Task.Delay(LongDelayMs);
                }

                var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
                if (timeSinceLastRequest.TotalSeconds < 5)
                {
                    var delayMs = (int)((5 - timeSinceLastRequest.TotalSeconds) * 1000) + _random.Next(1000, 3000);
                    Console.WriteLine($"Throttling: Waiting {delayMs}ms before next request");
                    await Task.Delay(delayMs);
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(data, Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(referrer))
                {
                    request.Headers.Referrer = new Uri(referrer);
                }

                var response = await _client.SendAsync(request);
                _lastRequestTime = DateTime.UtcNow;

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                if (response.StatusCode == (HttpStatusCode)429)
                {
                    Console.WriteLine("Rate limited (429). Stopping processing as requested.");
                    throw new HttpRequestException("Rate limited (429)", null, HttpStatusCode.TooManyRequests);
                }

                string errorContent = await response.Content.ReadAsStringAsync();
                errorContent = errorContent.Substring(0, Math.Min(errorContent.Length, 200));
                Console.WriteLine($"Error response ({response.StatusCode}): {errorContent}");

                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                {
                    throw new HttpRequestException($"Client error: {response.StatusCode} - {errorContent}");
                }

                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw;
            }
            catch (Exception ex) when (retryCount < maxRetries - 1)
            {
                retryCount++;
                int delayMs = (int)(baseDelayMs * Math.Pow(2, retryCount - 1) * (0.8 + 0.4 * _random.NextDouble()));
                Console.WriteLine($"Request failed: {ex.Message}. Retry {retryCount}/{maxRetries} in {delayMs}ms");
                await Task.Delay(delayMs);
            }
            finally
            {
                _requestThrottler.Release();
            }
        }

        throw new Exception($"Failed after {maxRetries} retries");
    }

    public async Task<string> GetAsync(string url, string? referrer = null)
    {
        int maxRetries = 5;
        int retryCount = 0;
        int baseDelayMs = 2000;

        while (retryCount < maxRetries)
        {
            await _requestThrottler.WaitAsync();
            try
            {
                IncrementRequestCount();
                int currentCount;
                lock (_countLock) { currentCount = _requestCount; }

                if (currentCount % (DelayThreshold + 1) == 0)
                {
                    Console.WriteLine($"Throttling: 9th request reached. Waiting {LongDelayMs}ms");
                    await Task.Delay(LongDelayMs);
                }

                var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
                if (timeSinceLastRequest.TotalSeconds < 5)
                {
                    var delayMs = (int)((5 - timeSinceLastRequest.TotalSeconds) * 1000) + _random.Next(1000, 3000);
                    Console.WriteLine($"Throttling: Waiting {delayMs}ms before next request");
                    await Task.Delay(delayMs);
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(referrer))
                    request.Headers.Referrer = new Uri(referrer);

                var response = await _client.SendAsync(request);
                _lastRequestTime = DateTime.UtcNow;

                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync();

                if (response.StatusCode == (HttpStatusCode)429)
                    throw new HttpRequestException("Rate limited (429)", null, HttpStatusCode.TooManyRequests);

                string errorContent = await response.Content.ReadAsStringAsync();
                errorContent = errorContent[..Math.Min(errorContent.Length, 200)];
                Console.WriteLine($"Error response ({response.StatusCode}): {errorContent}");

                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                    throw new HttpRequestException($"Client error: {response.StatusCode} - {errorContent}");

                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw;
            }
            catch (Exception ex) when (retryCount < maxRetries - 1)
            {
                retryCount++;
                int delayMs = (int)(baseDelayMs * Math.Pow(2, retryCount - 1) * (0.8 + 0.4 * _random.NextDouble()));
                Console.WriteLine($"Request failed: {ex.Message}. Retry {retryCount}/{maxRetries} in {delayMs}ms");
                await Task.Delay(delayMs);
            }
            finally
            {
                _requestThrottler.Release();
            }
        }

        throw new Exception($"Failed after {maxRetries} retries");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _requestThrottler.Dispose();
            _disposed = true;
        }
    }
}