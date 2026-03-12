using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

public class GasStationFinder
{
    private readonly IRequestService        _requestService;
    private readonly StationFormatterConfig _config;
    private readonly UserFormatStore        _formatStore;

    private static readonly string[] Medals = { "🥇", "🥈", "🥉" };

    public GasStationFinder(IRequestService requestService, StationFormatterConfig config, UserFormatStore formatStore)
    {
        _requestService = requestService ?? throw new ArgumentNullException(nameof(requestService));
        _config         = config         ?? throw new ArgumentNullException(nameof(config));
        _formatStore    = formatStore    ?? throw new ArgumentNullException(nameof(formatStore));
    }

    public async Task<string> FindAsync(string searchGas, long chatId = 0)
    {
        try
        {
            //List<FuelStation> stations = new List<FuelStation>(3);
            //stations.Add(new FuelStation("Costco GasStation", "9430 Boulevard Taschereau, Brossard, QC J4X 2T7", 0, 0, 153.90m));
            //stations.Add(new FuelStation("Shell", "4900 Grande Allee, Longueuil, QC J4V 3K9, Canada", 0, 0, 172.90m));        
            //stations[0].Distance = 1.2m;
            //stations[1].Distance = 11.2m;

            var stations = await _requestService.GetDataAsync(searchGas);

            if (stations == null || stations.Count == 0)
                return "⛽ <b>No Gas Stations Found</b>\n" +
                       $"📍 <i>{searchGas}</i>\n\n" +
                       "No stations found nearby. Try a different address or postal code.";

            // Sort: cheapest first, unknowns last, distance as tiebreaker.
            var sorted = stations
                .OrderBy(s => s.Price <= 0 ? decimal.MaxValue : s.Price)
                .ThenBy(s => s.Distance)
                .Take(_config.MaxResults > 0 ? _config.MaxResults : int.MaxValue)
                .ToList();

            var cheapest = sorted.FirstOrDefault(s => s.Price > 0);
            var sb       = new StringBuilder();

            // Header
            sb.Append("⛽ <b>Gas Stations</b> · ");
            sb.AppendLine($"<i>{EscapeHtml(searchGas)}</i>");

            if (_config.ShowBestPriceSummary && cheapest != null)
                sb.AppendLine($"💰 Best: <b>{cheapest.Price:F1}¢/L</b> · {EscapeHtml(cheapest.Name)}");

            sb.AppendLine();

            // Station rows
            var activeFormat = _formatStore.Get(chatId);
            if (activeFormat == OutputFormat.Table)
                sb.Append(RenderTable(sorted));
            else
                for (int i = 0; i < sorted.Count; i++)
                    sb.Append(RenderStation(sorted[i], i, activeFormat));

            // Footer
            sb.Append($"<i>🔄 {DateTime.Now:MMM d, HH:mm} · {stations.Count} station{(stations.Count == 1 ? "" : "s")}</i>");
            if (_config.ShowFormatFooter)
                sb.Append($" · <i>{activeFormat}</i>");

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return "⛽ <b>Search Error</b>\n" +
                   $"<i>{EscapeHtml(ex.Message)}</i>\n\n" +
                   "Please try again later.";
        }
    }

    // ── Formatters ────────────────────────────────────────────────────────────

    private static string RenderStation(FuelStation s, int index, OutputFormat format) => format switch
    {
        OutputFormat.Compact => RenderCompact(s, index),
        OutputFormat.Card    => RenderCard(s, index),
        OutputFormat.Minimal => RenderMinimal(s, index),
        _                    => RenderCompact(s, index),
    };

    /// Compact — one line, address as a linked icon.
    /// 🥇 Costco · 153.9¢/L · 1.2km · 📍
    private static string RenderCompact(FuelStation s, int index)
    {
        var medal   = index < Medals.Length ? Medals[index] : $"{index + 1}.";
        var price   = s.Price > 0    ? $"<b>{s.Price:F1}¢/L</b>" : "<i>N/A</i>";
        var dist    = s.Distance > 0 ? $"{s.Distance:F1}km"       : "?km";
        var mapLink = MapsLink(s.Address, "📍");
        return $"{medal} {EscapeHtml(s.Name)} · {price} · {dist} · {mapLink}\n";
    }

    /// Card — three tight lines, address as clickable text.
    /// 🥇 Costco GasStation
    ///    💵 153.9¢/L  📏 1.2 km
    ///    📌 9430 Boulevard Taschereau...
    private static string RenderCard(FuelStation s, int index)
    {
        var medal   = index < Medals.Length ? Medals[index] : $"{index + 1}.";
        var price   = s.Price > 0    ? $"<b>{s.Price:F1}¢/L</b>"  : "<i>N/A</i>";
        var dist    = s.Distance > 0 ? $"  📏 {s.Distance:F1} km" : "";
        var mapLink = MapsLink(s.Address, EscapeHtml(s.Address));

        return $"{medal} <b>{EscapeHtml(s.Name)}</b>\n" +
               $"   💵 {price}{dist}\n" +
               $"   📌 {mapLink}\n";
    }

    /// Minimal — name and price only.
    /// 🥇 Costco — 153.9¢/L
    private static string RenderMinimal(FuelStation s, int index)
    {
        var medal = index < Medals.Length ? Medals[index] : $"{index + 1}.";
        var price = s.Price > 0 ? $"<b>{s.Price:F1}¢/L</b>" : "<i>N/A</i>";
        return $"{medal} {EscapeHtml(s.Name)} — {price}\n";
    }

    // ── Table renderer (wraps all rows in <pre>) ─────────────────────────────

    // Column widths (characters). Keep total <= 40 for comfortable mobile display.
    private const int ColName  = 15;
    private const int ColPrice =  7;
    private const int ColDist  =  6;

    /// Renders all stations as a fixed-width ASCII table inside &lt;pre&gt; tags.
    ///
    /// <pre>
    /// # Name            Price   Dist
    /// ─────────────────────────────────
    /// 1 Costco Gas...   153.9   1.2km
    ///   9430 Boul Tasch...
    /// 2 Shell            172.9  11.2km
    ///   4900 Grande Allee...
    /// </pre>
    private static string RenderTable(List<FuelStation> stations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<pre>");

        // Header row
        sb.AppendLine(
            $"{"#",-2} {"Name".PadRight(ColName)} {"Price".PadRight(ColPrice)} {"Dist".PadRight(ColDist)}");
        sb.AppendLine(new string('─', 2 + 1 + ColName + 1 + ColPrice + 1 + ColDist));

        for (int i = 0; i < stations.Count; i++)
        {
            var s     = stations[i];
            var num   = $"{i + 1}".PadRight(2);
            var name  = Truncate(s.Name, ColName).PadRight(ColName);
            var price = (s.Price > 0 ? $"{s.Price:F1}c" : "N/A").PadRight(ColPrice);
            var dist  = (s.Distance > 0 ? $"{s.Distance:F1}km" : "?").PadRight(ColDist);

            sb.AppendLine($"{num} {name} {price} {dist}");

            // Address on a second indented line — truncated to fit <pre> width.
            var addr = Truncate(s.Address, 2 + 1 + ColName + 1 + ColPrice + 1 + ColDist);
            sb.AppendLine($"   {addr}");
        }

        sb.AppendLine("</pre>");
        return sb.ToString();
    }

    /// Stub so the switch compiles — actual table rendering is done by the bulk overload above.
    private static string RenderTable(FuelStation s, int index) => string.Empty;

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string MapsLink(string address, string label)
    {
        var url = $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(address)}";
        return $"<a href=\"{url}\">{label}</a>";
    }

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
}