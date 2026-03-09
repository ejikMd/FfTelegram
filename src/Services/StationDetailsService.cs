using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

public interface IStationDetailsService : IDisposable
{
    Task<StationDetails> GetStationDetailsAsync(int stationId);
    Task<StationDetails> GetStationDetailsAsync(double latitude, double longitude);
}

public class StationDetails
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Brand { get; set; } = string.Empty;
}

public class StationDetailsService : IStationDetailsService
{
    private readonly IReverseGeocoder _reverseGeocoder;
    private bool _disposed = false;

    public StationDetailsService(string apiKey)
    {
        //_reverseGeocoder = new OpenStreetMapReverseGeocoder();
        _reverseGeocoder = new GeoapifyDistanceCalculator(apiKey);
    }

    public async Task<StationDetails> GetStationDetailsAsync(int stationId)
    {
        try
        {
            // TODO: Implement actual API call to get station details
            // This might be a different endpoint or GraphQL query

            // For now, return mock data
            await Task.Delay(100); // Simulate API call

            return new StationDetails
            {
                Id = stationId,
                Name = $"Gas Station {stationId}",
                Address = "123 Main Street",
                Brand = "Unknown"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting station details: {ex.Message}");
            return new StationDetails
            {
                Id = stationId,
                Name = $"Station {stationId}",
                Address = "Address unavailable",
                Brand = "Unknown"
            };
        }
    }

    public async Task<StationDetails> GetStationDetailsAsync(double latitude, double longitude)
    {
        try
        {
            var geocodeInfo = await _reverseGeocoder.GetAddressAsync(latitude, longitude);

            return new StationDetails
            {
                Id = 0,
                Name = geocodeInfo.Name,
                Address = geocodeInfo.Address,
                Latitude = latitude,
                Longitude = longitude,
                Brand = "Unknown"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting station details from coordinates: {ex.Message}");
            return new StationDetails
            {
                Id = 0,
                Name = "Unknown",
                Address = $"{latitude}, {longitude}",
                Latitude = latitude,
                Longitude = longitude,
                Brand = "Unknown"
            };
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
                _reverseGeocoder?.Dispose();
            }
            _disposed = true;
        }
    }
}