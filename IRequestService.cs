using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IRequestService : IDisposable
{
    Task<List<FuelStation>> GetDataAsync(string startAddress);
}