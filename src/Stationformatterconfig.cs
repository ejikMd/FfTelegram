/// <summary>
/// Controls how gas station results are rendered in Telegram.
/// Set via environment variables in Replit Secrets.
/// </summary>
public enum OutputFormat
{
    /// <summary>One line per station — name, price, distance, 📍 link. Best for large lists.</summary>
    Compact,

    /// <summary>Three lines per station with labels. Good balance of detail and space.</summary>
    Card,

    /// <summary>Name and price only. Useful when distance/address data is unavailable.</summary>
    Minimal,

    /// <summary>Fixed-width ASCII table inside &lt;pre&gt; tags. Best for desktop/tablet users.</summary>
    Table,
}

/// <summary>
/// Injected into <see cref="GasStationFinder"/> and <see cref="MessageRouter"/>
/// to control all user-facing message formatting.
///
/// Configure via Replit Secrets (no code changes needed):
///
///   OUTPUT_FORMAT      = Compact | Card | Minimal | Table   (default: Compact)
///   MAX_RESULTS        = integer                     (default: 10)
///   SHOW_BEST_PRICE    = true | false                (default: true)
///   SEARCHING_EMOJI    = any emoji/text              (default: 🔍)
///   SHOW_FORMAT_FOOTER = true | false                (default: false)
/// </summary>
public sealed class StationFormatterConfig
{
    /// <summary>How to render each station row.</summary>
    public OutputFormat Format { get; init; } = OutputFormat.Compact;

    /// <summary>Maximum stations to show. 0 = unlimited.</summary>
    public int MaxResults { get; init; } = 10;

    /// <summary>Whether to show the "best price" summary line in the header.</summary>
    public bool ShowBestPriceSummary { get; init; } = true;

    /// <summary>Emoji shown in the "Searching..." interim message.</summary>
    public string SearchingEmoji { get; init; } = "🔍";

    /// <summary>
    /// When true, appends the active format name to the footer so users can
    /// see which mode is active (handy during testing).
    /// </summary>
    public bool ShowFormatFooter { get; init; } = false;

    // ── Derived message templates ─────────────────────────────────────────────

    /// <summary>Interim message sent while the search is running.</summary>
    public string SearchingMessage(string location) =>
        $"{SearchingEmoji} Searching near <i>{location}</i>…";

    /// <summary>Shown when the user calls /find with no argument.</summary>
    public string MissingArgumentMessage =>
        "⛽ Please provide a location.\nUsage: <code>/find [city, address, or postal code]</code>";

    /// <summary>Footer appended to every result when ShowFormatFooter is true.</summary>
    public string FormatFooter => $" · <i>{Format}</i>";

    // ── Factory ───────────────────────────────────────────────────────────────

    public static StationFormatterConfig FromEnvironment()
    {
        var formatStr = Environment.GetEnvironmentVariable("OUTPUT_FORMAT") ?? "Compact";
        var format    = Enum.TryParse<OutputFormat>(formatStr, ignoreCase: true, out var f)
                        ? f : OutputFormat.Compact;

        var maxResults = int.TryParse(Environment.GetEnvironmentVariable("MAX_RESULTS"), out var m) && m > 0
                         ? m : 10;

        var showBest = !string.Equals(
            Environment.GetEnvironmentVariable("SHOW_BEST_PRICE"), "false",
            StringComparison.OrdinalIgnoreCase);

        var searchingEmoji = Environment.GetEnvironmentVariable("SEARCHING_EMOJI") ?? "🔍";

        var showFooter = string.Equals(
            Environment.GetEnvironmentVariable("SHOW_FORMAT_FOOTER"), "true",
            StringComparison.OrdinalIgnoreCase);

        return new StationFormatterConfig
        {
            Format               = format,
            MaxResults           = maxResults,
            ShowBestPriceSummary = showBest,
            SearchingEmoji       = searchingEmoji,
            ShowFormatFooter     = showFooter,
        };
    }
}