using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

public class GasStationFinder
{
    private readonly IRequestService _requestService;

    // Constructor accepting the interface instead of concrete implementation
    public GasStationFinder(IRequestService requestService)
    {
        _requestService = requestService ?? throw new ArgumentNullException(nameof(requestService));
    }

    public async Task<string> FindAsync(string searchGas)
    {
        try
        {
            // Get data from IRequestService which now handles pagination internally
            var allStations = await _requestService.GetDataAsync(searchGas);

            if (allStations == null || allStations.Count == 0)
            {
                return $"⛽ <b>No Gas Stations Found</b>\n\n" +
                       $"🔍 Searching for: <i>{searchGas}</i>\n\n" +
                       $"😕 No stations found. Please try a different location.";
            }

            var sb = new StringBuilder();
            int expectedPerPage = 5;
            int cursorStep = 5;

            // Add header
            sb.AppendLine("⛽ <b>Gas Stations Found</b>");
            sb.AppendLine($"🔍 Searching for: <i>{searchGas}</i>");
            sb.AppendLine();

            // Create table header
            sb.AppendLine("<pre>");
            sb.AppendLine($"{"Name",-25} {"Price",-10} {"Address",-40}");
            sb.AppendLine(new string('-', 77));

            // Add up to expectedPerPage stations from the results (show first page worth)
            int displayCount = Math.Min(allStations.Count, expectedPerPage);
            for (int i = 0; i < displayCount; i++)
            {
                var station = allStations[i];
                var name = station.Name.Length > 23 ? station.Name.Substring(0, 20) + "..." : station.Name;
                var price = station.Price > 0 ? $"${station.Price:F2}" : "N/A";
                var address = station.Address.Length > 38 ? station.Address.Substring(0, 35) + "..." : station.Address;

                sb.AppendLine($"{name,-25} {price,-10} {address,-40}");
            }

            sb.AppendLine("</pre>");
            sb.AppendLine();

            // Add summary information
            sb.AppendLine($"📊 Found {allStations.Count} total stations");
            sb.AppendLine($"📋 Showing first {displayCount} stations");

            if (allStations.Count > displayCount)
            {
                int remainingPages = (int)Math.Ceiling((double)(allStations.Count - displayCount) / expectedPerPage);
                sb.AppendLine($"➕ {allStations.Count - displayCount} more stations available (about {remainingPages} more page{(remainingPages > 1 ? "s" : "")})");
            }

            // Calculate pagination info
            int pagesLoaded = (int)Math.Ceiling((double)allStations.Count / expectedPerPage);
            int maxCursor = Math.Max(0, (pagesLoaded - 1) * cursorStep);

            sb.AppendLine($"🔄 Prices updated: " + DateTime.Now.ToString("MM/dd/yyyy HH:mm"));
            sb.AppendLine($"📡 Pages loaded: {pagesLoaded} (cursor steps: 0 to {maxCursor})");
            sb.AppendLine($"⚙️ Cursor step size: {cursorStep}");
            sb.AppendLine($"📄 Stations per page: {expectedPerPage}");

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