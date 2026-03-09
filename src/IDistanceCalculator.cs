using System;
using System.Threading.Tasks;

public interface IDistanceCalculator : IDisposable
{
    Task<string> CalculateDrivingDistanceAsync(string startAddress, string endAddress);
}
