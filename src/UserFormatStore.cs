using System.Collections.Concurrent;

/// <summary>
/// Thread-safe in-memory store for each user's chosen OutputFormat.
/// Falls back to the global default from StationFormatterConfig when
/// no per-user preference has been set.
/// </summary>
public sealed class UserFormatStore
{
    private readonly ConcurrentDictionary<long, OutputFormat> _preferences = new();
    private readonly OutputFormat _defaultFormat;

    public UserFormatStore(StationFormatterConfig config)
    {
        _defaultFormat = config.Format;
    }

    /// <summary>Returns the user's preferred format, or the global default.</summary>
    public OutputFormat Get(long chatId) =>
        _preferences.TryGetValue(chatId, out var fmt) ? fmt : _defaultFormat;

    /// <summary>Saves the user's format choice.</summary>
    public void Set(long chatId, OutputFormat format) =>
        _preferences[chatId] = format;

    /// <summary>Removes the user's override, reverting to the global default.</summary>
    public void Reset(long chatId) =>
        _preferences.TryRemove(chatId, out _);
}