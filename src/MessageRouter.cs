using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

/// <summary>
/// Routes incoming Telegram messages to registered command handlers.
/// Add new commands here without touching BotService or Program.
/// </summary>
public sealed class MessageRouter
{
    private readonly Dictionary<string, Func<ITelegramBotClient, long, string, CancellationToken, Task>> _handlers;
    private readonly ILogger<MessageRouter> _logger;
    private readonly UserRateLimiter _rateLimiter;

    public MessageRouter(
        GasStationFinder gasStationFinder,
        UserRateLimiter rateLimiter,
        ILogger<MessageRouter> logger)
    {
        _logger = logger;
        _rateLimiter = rateLimiter;

        // Register commands — add new entries here to extend the bot.
        _handlers = new Dictionary<string, Func<ITelegramBotClient, long, string, CancellationToken, Task>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["/start"] = HandleStartAsync,
            ["/help"]  = HandleHelpAsync,
            ["/find"]  = (bot, chatId, args, ct) => HandleFindAsync(bot, chatId, args, ct, gasStationFinder),
        };
    }

    public async Task RouteAsync(
        ITelegramBotClient bot,
        long chatId,
        string text,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // Split command prefix from arguments: "/find H8N2P7" → ("/find", "H8N2P7")
        var spaceIndex = text.IndexOf(' ');
        var command = spaceIndex >= 0 ? text[..spaceIndex] : text;
        var args    = spaceIndex >= 0 ? text[(spaceIndex + 1)..].Trim() : string.Empty;

        if (!_handlers.TryGetValue(command, out var handler))
        {
            await bot.SendMessage(
                chatId,
                "❓ Unknown command. Type /help to see available commands.",
                cancellationToken: ct);
            return;
        }

        // Per-user rate limiting
        if (!_rateLimiter.TryConsume(chatId))
        {
            await bot.SendMessage(
                chatId,
                "⏳ You're sending commands too quickly. Please wait a moment.",
                cancellationToken: ct);
            return;
        }

        try
        {
            await handler(bot, chatId, args, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in command {Command} for chat {ChatId}.", command, chatId);
            await bot.SendMessage(chatId, "❌ An unexpected error occurred. Please try again later.",
                cancellationToken: ct);
        }
        finally
        {
            _logger.LogInformation("Command {Command} handled in {ElapsedMs}ms.", command, sw.ElapsedMilliseconds);
        }
    }

    // -------------------------------------------------------------------------
    // Command handlers
    // -------------------------------------------------------------------------

    private static Task HandleStartAsync(
        ITelegramBotClient bot, long chatId, string _, CancellationToken ct) =>
        bot.SendMessage(
            chatId,
            "👋 <b>Welcome to the Gas Station Finder bot!</b>\n\n" +
            "Use <code>/find [location]</code> to search for nearby gas stations.\n\n" +
            "Examples:\n" +
            "• <code>/find H8N2P7</code>\n" +
            "• <code>/find Montreal, QC</code>\n" +
            "• <code>/find 123 Main St, Toronto</code>",
            parseMode: ParseMode.Html,
            cancellationToken: ct);

    private static Task HandleHelpAsync(
        ITelegramBotClient bot, long chatId, string _, CancellationToken ct) =>
        bot.SendMessage(
            chatId,
            "📖 <b>Available commands</b>\n\n" +
            "/start — Welcome message\n" +
            "/find [location] — Search gas stations near a location\n" +
            "/help — Show this message",
            parseMode: ParseMode.Html,
            cancellationToken: ct);

    private async Task HandleFindAsync(
        ITelegramBotClient bot,
        long chatId,
        string args,
        CancellationToken ct,
        GasStationFinder finder)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            await bot.SendMessage(
                chatId,
                "⛽ Please provide a location.\nUsage: <code>/find [city, address, or postal code]</code>",
                parseMode: ParseMode.Html,
                cancellationToken: ct);
            return;
        }

        await bot.SendMessage(
            chatId,
            $"🔍 Searching for gas stations near: <i>{args}</i>...",
            parseMode: ParseMode.Html,
            cancellationToken: ct);

        string result;
        try
        {
            result = await finder.FindAsync(args);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            result = "⚠️ <b>Service temporarily unavailable due to high demand.</b> Please try again in a few minutes.";
            _logger.LogWarning("Rate-limited (429) while searching for: {Location}", args);
        }
        catch (Exception ex)
        {
            result = "❌ An error occurred while searching. Please try again later.";
            _logger.LogError(ex, "Search failed for location: {Location}", args);
        }

        await bot.SendMessage(chatId, result, parseMode: ParseMode.Html, cancellationToken: ct);
        _logger.LogInformation("Responded to /find for: {Location}", args);
    }
}
