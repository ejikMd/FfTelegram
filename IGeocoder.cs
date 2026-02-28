using System;
using System.Threading.Tasks;

public interface IGeocoder : IDisposable
{
    Task<(double latitude, double longitude)> GetCoordinatesAsync(string location);
}