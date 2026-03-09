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
            //List<FuelStation> allStations = new List<FuelStation>(3);
            //allStations.Add(new FuelStation("Costco GasStation", "9430 Boulevard Taschereau, Brossard, QC J4X 2T7", 0, 0, 153.90m));
            //allStations.Add(new FuelStation("Shell", "4900 Grande Allee, Longueuil, QC J4V 3K9, Canada", 0, 0, 172.90m));           
            //allStations[0].Distance = 1.2m;
            //allStations[1].Distance = 11.2m;
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
            sb.AppendLine($"{"Name",-13} {"Price",-7} {"Address",-60} {"Dist",-5}");
            sb.AppendLine(new string('-', 88));

            foreach (var station in allStations)
            {
                var name = station.Name.Length > 13 ? station.Name.Substring(0, 10) + "..." : station.Name;
                var price = station.Price > 0 ? $"${station.Price:F2}" : "N/A";
                var address = station.Address.Length > 57 ? station.Address.Substring(0, 57) + "..." : station.Address;
                var distance = station.Distance;
                sb.AppendLine($"{name,-13} {price,-7} {address,-60}|{distance:F1}");
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