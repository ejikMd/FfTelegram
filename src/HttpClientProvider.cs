using System.Net.Http;

public static class HttpClientProvider
{
    private static readonly Lazy<HttpClient> _instance = new Lazy<HttpClient>(() =>
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    });

    public static HttpClient Instance => _instance.Value;
}
