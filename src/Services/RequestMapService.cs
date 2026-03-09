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
    private readonly GasBuddyHttpClient _gasBuddyClient;
    private readonly Random _random = new Random();
    private bool _disposed = false;

    public RequestMapService(
        IGeocoder geocoder,
        IStationDetailsService stationDetailsService,
        IDistanceCalculator distanceCalculator)
    {
        _geocoder = geocoder ?? throw new ArgumentNullException(nameof(geocoder));
        _stationDetailsService = stationDetailsService ?? throw new ArgumentNullException(nameof(stationDetailsService));
        _distanceCalculator = distanceCalculator ?? throw new ArgumentNullException(nameof(distanceCalculator));
        _gasBuddyClient = new GasBuddyHttpClient();
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
        // Add random delay before starting (3-7 seconds)
        await Task.Delay(3000 + _random.Next(4000));

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
            string referrer = "https://www.gasbuddy.com/gaspricemap?fuel=1&z=14&lat=45.4580767734426&lng=-73.4545422846292";

            Console.WriteLine($"Requesting gas stations in bounding box: {minLat},{minLng} to {maxLat},{maxLng}");

            string response = await _gasBuddyClient.PostJsonAsync(url, jsonPayload, referrer);

            if (string.IsNullOrEmpty(response))
            {
                return new List<FuelStation>();
            }

            // Parse the response
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var mapResponse = JsonSerializer.Deserialize<GasPriceMapResponse>(response, options);
            if (mapResponse == null)
            {
                return new List<FuelStation>();
            }

            var result = new List<FuelStation>();
            var stationIds = new HashSet<int>(); // To avoid duplicates

            // Process primary stations (closest/most relevant)
            if (mapResponse.primaryStations != null)
            {
                Console.WriteLine($"Received {mapResponse.primaryStations.Count} primary stations ");
                //int i = 0;
                foreach (var station in mapResponse.primaryStations)
                {
                    // Skip stations without valid price
                    if (station.price == "--" || string.IsNullOrEmpty(station.price))
                        continue;

                    if (decimal.TryParse(station.price, out decimal price) && !stationIds.Contains(station.id))
                    {
                        stationIds.Add(station.id);
                        //i++;
                        //Console.WriteLine($"Processing station {i} - {station.lat}, {station.lng}");
                        // Get detailed station information
                        var details = await _stationDetailsService.GetStationDetailsAsync(station.lat, station.lng);

                        result.Add(new FuelStation(
                            details.Name,
                            details.Address,
                            station.lat,
                            station.lng,
                            price // Keep original price (not divided by 100)
                        ));

                        // Small delay between detail requests to avoid rate limiting
                        //await Task.Delay(500 + _random.Next(500));
                    }
                }
            }

            // Also add secondary stations if we need more results
            if (result.Count < 15 && mapResponse.secondaryStations != null)
            {
                Console.WriteLine($"Received {mapResponse.secondaryStations.Count} secondary stations ");
                foreach (var station in mapResponse.secondaryStations)
                {
                    if (station.price == "--" || string.IsNullOrEmpty(station.price))
                        continue;

                    if (decimal.TryParse(station.price, out decimal price) && !stationIds.Contains(station.id))
                    {
                        stationIds.Add(station.id);

                        // Get detailed station information
                        var details = await _stationDetailsService.GetStationDetailsAsync(station.lat, station.lng);

                        result.Add(new FuelStation(
                            details.Name,
                            details.Address,
                            station.lat,
                            station.lng,
                            price // Keep original price (not divided by 100)
                        ));

                        // Small delay between detail requests
                        //await Task.Delay(500 + _random.Next(500));

                        if (result.Count >= 20) break; // Limit total results
                    }
                }
            }

            Console.WriteLine($"Found {result.Count} stations with prices");

            // Calculate distances for all stations
            foreach (var station in result)
            {
                station.Distance = await _distanceCalculator.CalculateDrivingDistanceAsync(latitude, longitude, station.Latitude, station.Longitude);
            }

            return result.OrderBy(s => s.Price).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetGasStationsByCoordinatesAsync: {ex.Message}");
            return new List<FuelStation>();
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
                _gasBuddyClient?.Dispose();
                _geocoder?.Dispose();
                _stationDetailsService?.Dispose();
                _distanceCalculator?.Dispose();
            }
            _disposed = true;
        }
    }
}