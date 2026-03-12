using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

public class RequestMapService : IRequestService
{
    private readonly IGeocoder _geocoder;
    private readonly IStationDetailsService _stationDetailsService;
    private readonly IDistanceCalculator _distanceCalculator;
    private readonly StationFormatterConfig _config;
    private readonly GasBuddyHttpClient _gasBuddyClient;
    private readonly StationCacheService _stationCache;
    private readonly Random _random = new Random();
    private bool _disposed = false;

    public RequestMapService(
        IGeocoder geocoder,
        IStationDetailsService stationDetailsService,
        IDistanceCalculator distanceCalculator,
        StationFormatterConfig config,
        GasBuddyHttpClient gasBuddyClient,
        StationCacheService stationCache)
    {
        _geocoder = geocoder ?? throw new ArgumentNullException(nameof(geocoder));
        _stationDetailsService = stationDetailsService ?? throw new ArgumentNullException(nameof(stationDetailsService));
        _distanceCalculator = distanceCalculator ?? throw new ArgumentNullException(nameof(distanceCalculator));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _gasBuddyClient = gasBuddyClient ?? throw new ArgumentNullException(nameof(gasBuddyClient));
        _stationCache = stationCache ?? throw new ArgumentNullException(nameof(stationCache));
    }

    public async Task<List<FuelStation>> GetDataAsync(string startAddress)
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
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw; // Propagate rate limit error
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
            var mapResponse = await FetchMapResponseAsync(latitude, longitude);
            if (mapResponse == null)
                return new List<FuelStation>();

            return await ProcessMapResponseAsync(mapResponse, latitude, longitude);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetGasStationsByCoordinatesAsync: {ex.Message}");
            return new List<FuelStation>();
        }
    }

    private async Task<GasPriceMapResponse?> FetchMapResponseAsync(double latitude, double longitude)
    {
        double latOffset = 0.045; // Roughly 5km in latitude
        double lngOffset = 0.06;  // Roughly 5km in longitude (adjusted for latitude)

        double minLat = latitude - latOffset;
        double maxLat = latitude + latOffset;
        double minLng = longitude - lngOffset;
        double maxLng = longitude + lngOffset;

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
        string referrer = "";//"https://www.gasbuddy.com/gaspricemap?fuel=1&z=14&lat=45.4580767734426&lng=-73.4545422846292";

        Console.WriteLine($"Requesting gas stations in bounding box: {minLat},{minLng} to {maxLat},{maxLng}");

        string response = await _gasBuddyClient.PostJsonAsync(url, jsonPayload, referrer);

        if (string.IsNullOrEmpty(response))
            return null;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<GasPriceMapResponse>(response, options);
    }

    private async Task<List<FuelStation>> ProcessMapResponseAsync(GasPriceMapResponse mapResponse, double latitude, double longitude)
    {
        var stationIds = new HashSet<int>(); // To avoid duplicates

        // Parse price once per station — used for filtering, ordering, and FuelStation construction
        var allCandidates = (mapResponse.primaryStations ?? new List<GasStationMapItem>())
            .Concat(mapResponse.secondaryStations ?? new List<GasStationMapItem>())
            .Select(s => (Station: s, HasPrice: decimal.TryParse(s.price, out decimal parsed), Price: parsed))
            .Where(x => x.HasPrice)
            .OrderBy(x => x.Price)
            .ToList();

        var maxResults = _config.MaxResults > 0 ? _config.MaxResults : allCandidates.Count;
        Console.WriteLine($"Limiting to {maxResults} results (found {allCandidates.Count} total candidates)");

        var limitedCandidates = allCandidates
            .Where(x => stationIds.Add(x.Station.id)) // deduplicate while iterating
            .Take(maxResults)
            .ToList();

        // Process sorted and limited stations, fetching details only for the ones we'll use
        var result = new List<FuelStation>();
        foreach (var (station, _, price) in limitedCandidates)
        {
            // Try cache first — fall back to live service on miss.
            var details = await _stationCache.GetAsync(station.id);
            if (details == null)
            {
                details = await _stationDetailsService.GetStationDetailsAsync(station.lat, station.lng);
                await _stationCache.SetAsync(station.id, details);
            }

            result.Add(new FuelStation(
                details.Name,
                details.Address,
                station.lat,
                station.lng,
                price
            ));
        }

        // Calculate distances for selected stations only
        foreach (var station in result)
        {
            station.Distance = await _distanceCalculator.CalculateDrivingDistanceAsync(latitude, longitude, station.Latitude, station.Longitude);
        }

        return result;
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
                _gasBuddyClient?.Dispose();
                _geocoder?.Dispose();
                _stationDetailsService?.Dispose();
                _distanceCalculator?.Dispose();
            }
            _disposed = true;
        }
    }
}