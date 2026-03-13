using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

public class GasStationFinder
{
    private readonly IRequestService _requestService;
    private readonly StationFormatterConfig _config;
    private readonly UserFormatStore _formatStore;
    private readonly IStationFormatter _formatter;

    private static readonly string[] Medals = { "🥇", "🥈", "🥉" };

    private static readonly Dictionary<char, string> HtmlEscapeMap = new Dictionary<char, string>
    {
        { '&', "&amp;" },
        { '<', "&lt;" },
        { '>', "&gt;" }
    };

    public GasStationFinder(
        IRequestService requestService, 
        StationFormatterConfig config, 
        UserFormatStore formatStore,
        IStationFormatter formatter = null)
    {
        _requestService = requestService ?? throw new ArgumentNullException(nameof(requestService));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _formatStore = formatStore ?? throw new ArgumentNullException(nameof(formatStore));
        _formatter = formatter ?? new StationFormatter(Medals);
    }

    public async Task<string> FindAsync(string searchGas, long chatId = 0)
    {
        try
        {
            var stations = await _requestService.GetDataAsync(searchGas);

            if (stations == null || stations.Count == 0)
            {
                // FIX: Escape searchGas to prevent HTML injection
                return "⛽ <b>No Gas Stations Found</b>\n" +
                       $"📍 <i>{EscapeHtml(searchGas)}</i>\n\n" +
                       "No stations found nearby. Try a different address or postal code.";
            }

            // Sort: cheapest first, unknowns last, distance as tiebreaker.
            var sorted = stations
                .OrderBy(s => s.Price <= 0 ? decimal.MaxValue : s.Price)
                .ThenBy(s => s.Distance)
                .Take(_config.MaxResults > 0 ? _config.MaxResults : int.MaxValue)
                .ToList();

            var cheapest = sorted.FirstOrDefault(s => s.Price > 0);
            var sb = new StringBuilder();

            // Header - Escape searchGas here too for consistency
            sb.Append("⛽ <b>Gas Stations</b> · ");
            sb.AppendLine($"<i>{EscapeHtml(searchGas)}</i>");

            if (_config.ShowBestPriceSummary && cheapest != null)
                sb.AppendLine($"💰 Best: <b>{cheapest.Price:F1}¢∕L</b> · {EscapeHtml(cheapest.Name)}");

            sb.AppendLine();

            // Station rows using formatter service
            var activeFormat = _formatStore.Get(chatId);
            sb.Append(_formatter.FormatStations(sorted, activeFormat));

            // Footer
            sb.Append($"<i>🔄 {DateTime.Now:MMM d, HH:mm}</i>");
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

    // Optimized HTML escaping using StringBuilder
    private static string EscapeHtml(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sb = new StringBuilder(text.Length * 2);
        foreach (char c in text)
        {
            if (HtmlEscapeMap.TryGetValue(c, out var replacement))
                sb.Append(replacement);
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}

// New formatter service interface
public interface IStationFormatter
{
    string FormatStations(List<FuelStation> stations, OutputFormat format);
}

// Dedicated formatter implementation
public class StationFormatter : IStationFormatter
{
    private readonly string[] _medals;

    private static class TableLayout
    {
        public const int Number = 2;
        public const int Name = 15;
        public const int Price = 7;
        public const int Distance = 6;
        public const int Indent = 3;

        public static int TotalWidth => Number + 1 + Name + 1 + Price + 1 + Distance;
    }

    public StationFormatter(string[] medals)
    {
        _medals = medals;
    }

    public string FormatStations(List<FuelStation> stations, OutputFormat format)
    {
        if (format == OutputFormat.Table)
            return RenderTable(stations);

        var sb = new StringBuilder(stations.Count * 100);
        for (int i = 0; i < stations.Count; i++)
        {
            sb.Append(RenderStation(stations[i], i, format));
        }
        return sb.ToString();
    }

    private string RenderStation(FuelStation s, int index, OutputFormat format) => format switch
    {
        OutputFormat.Compact => RenderCompact(s, index),
        OutputFormat.Card => RenderCard(s, index),
        OutputFormat.Minimal => RenderMinimal(s, index),
        _ => RenderCompact(s, index),
    };

    private string RenderCompact(FuelStation s, int index)
    {
        var medal = index < _medals.Length ? _medals[index] : $"{index + 1}.";
        var price = s.Price > 0 ? $"<b>{s.Price:F1}¢∕L</b>" : "<i>N/A</i>";
        var dist = s.Distance > 0 ? $"{s.Distance:F1}km" : "?km";
        var mapLink = MapsLink(s.Latitude, s.Longitude, "📍");
        return $"{medal} {EscapeHtml(s.Name)} · {price} · {dist} · {mapLink}\n";
    }

    private string RenderCard(FuelStation s, int index)
    {
        var medal = index < _medals.Length ? _medals[index] : $"{index + 1}.";
        var price = s.Price > 0 ? $"<b>{s.Price:F1}¢∕L</b>" : "<i>N/A</i>";
        var dist = s.Distance > 0 ? $"  📏 {s.Distance:F1} km" : "";
        var mapLink = MapsLink(s.Latitude, s.Longitude, EscapeHtml(s.Address));

        return $"{medal} <b>{EscapeHtml(s.Name)}</b>\n" +
               $"   💵 {price}{dist}\n" +
               $"   📌 {mapLink}\n";
    }

    private string RenderMinimal(FuelStation s, int index)
    {
        var medal = index < _medals.Length ? _medals[index] : $"{index + 1}.";
        var price = s.Price > 0 ? $"<b>{s.Price:F1}¢∕L</b>" : "<i>N/A</i>";
        return $"{medal} {EscapeHtml(s.Name)} — {price}\n";
    }

    private string RenderTable(List<FuelStation> stations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<pre>");

        sb.AppendLine(
            $"{"#",-TableLayout.Number} " +
            $"{"Name".PadRight(TableLayout.Name)} " +
            $"{"Price".PadRight(TableLayout.Price)} " +
            $"{"Dist".PadRight(TableLayout.Distance)}");

        sb.AppendLine(new string('─', TableLayout.TotalWidth));

        for (int i = 0; i < stations.Count; i++)
        {
            var s = stations[i];

            var num = $"{i + 1}".PadRight(TableLayout.Number);
            var name = Truncate(EscapeHtml(s.Name), TableLayout.Name).PadRight(TableLayout.Name);
            var price = (s.Price > 0 ? $"{s.Price:F1}c" : "N/A").PadRight(TableLayout.Price);
            var dist = (s.Distance > 0 ? $"{s.Distance:F1}km" : "?").PadRight(TableLayout.Distance);

            sb.AppendLine($"{num} {name} {price} {dist}");

            var addrMaxLength = TableLayout.TotalWidth - TableLayout.Indent;
            var addr = Truncate(EscapeHtml(s.Address), addrMaxLength);
            var mapLink = MapsLink(s.Latitude, s.Longitude, addr);
            sb.AppendLine($"{new string(' ', TableLayout.Indent)}{mapLink}");
        }

        sb.AppendLine("</pre>");
        return sb.ToString();
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text ?? string.Empty;
        return text[..(max - 1)] + "…";
    }

    private static string MapsLink(double latitude, double longitude, string label)
    {
        var url = $"https://www.google.com/maps/search/?api=1&query={latitude},{longitude}";
        return $"<a href=\"{url}\">{label}</a>";
    }

    private static readonly Dictionary<char, string> HtmlEscapeMap = new Dictionary<char, string>
    {
        { '&', "&amp;" },
        { '<', "&lt;" },
        { '>', "&gt;" }
    };

    private static string EscapeHtml(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sb = new StringBuilder(text.Length * 2);
        foreach (char c in text)
        {
            if (HtmlEscapeMap.TryGetValue(c, out var replacement))
                sb.Append(replacement);
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}