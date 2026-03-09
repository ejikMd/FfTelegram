using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

public class GasStationFinder
{
    private readonly IRequestService _requestService;

    public GasStationFinder(IRequestService requestService)
    {
        _requestService = requestService ?? throw new ArgumentNullException(nameof(requestService));
    }

    public async Task<string> FindAsync(string searchGas)
    {
        try
        {
            var allStations = await _requestService.GetDataAsync(searchGas);

            if (allStations == null || allStations.Count == 0)
            {
                return $"⛽ <b>No Gas Stations Found</b>\n\n" +
                       $"🔍 Searching for: <i>{searchGas}</i>\n\n" +
                       $"No stations found. Please try a different location.";
            }

            var sb = new StringBuilder();

            sb.AppendLine("⛽ <b>Gas Stations Found</b>");
            sb.AppendLine($"🔍 Searching for: <i>{searchGas}</i>");
            sb.AppendLine();

            sb.AppendLine("<pre>");
            sb.AppendLine($"{"Name",-25} {"Price",-10} {"Address",-40} {"Distance",-10}");
            sb.AppendLine(new string('-', 87));

            foreach (var station in allStations)
            {
                var name = station.Name.Length > 23 ? station.Name.Substring(0, 20) + "..." : station.Name;
                var price = station.Price > 0 ? $"${station.Price:F2}" : "N/A";
                var address = station.Address.Length > 38 ? station.Address.Substring(0, 35) + "..." : station.Address;
                var distance = station.Distance;
                sb.AppendLine($"{name,-25} {price,-10} {address,-40} {distance:F1}");
            }

            sb.AppendLine("</pre>");
            sb.AppendLine();

            sb.AppendLine($"📊 Found {allStations.Count} total stations");
            sb.AppendLine($"🔄 Prices updated: " + DateTime.Now.ToString("MM/dd/yyyy HH:mm"));

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"⛽ <b>Error</b>\n\n" +
                   $"Sorry, an error occurred while searching for gas stations:\n" +
                   $"<i>{ex.Message}</i>\n\n" +
                   $"Please try again later.";
        }
    }
}