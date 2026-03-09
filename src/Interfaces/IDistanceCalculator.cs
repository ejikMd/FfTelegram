using System;
using System.Threading.Tasks;

public interface IDistanceCalculator : IDisposable
{
    Task<decimal> CalculateDrivingDistanceAsync(double startLatitude, double startLongitude, double endLatitude, double endLongitude);
}
