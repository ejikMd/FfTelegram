using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

/// <summary>
/// Routes incoming Telegram messages and inline keyboard callbacks
/// to the appropriate command handlers.
/// </summary>
public sealed class MessageRouter
{
    // Callback data prefix used to identify format-selection button presses.
    private const string FormatCallbackPrefix = "fmt:";

    private readonly Dictionary<string, Func<ITelegramBotClient, long, string, CancellationToken, Task>> _handlers;
    private readonly ILogger<MessageRouter>  _logger;
    private readonly UserRateLimiter         _rateLimiter;
    private readonly StationFormatterConfig  _config;
    private readonly UserFormatStore         _formatStore;
    private readonly FeedbackService         _feedbackService;

    public MessageRouter(
        GasStationFinder finder,
        UserRateLimiter rateLimiter,
        StationFormatterConfig config,
        UserFormatStore formatStore,
        FeedbackService feedbackService,
        ILogger<MessageRouter> logger)
    {
        _logger          = logger;
        _rateLimiter     = rateLimiter;
        _config          = config;
        _formatStore     = formatStore;
        _feedbackService = feedbackService;

        _handlers = new Dictionary<string, Func<ITelegramBotClient, long, string, CancellationToken, Task>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["/start"]    = HandleStartAsync,
            ["/help"]     = HandleHelpAsync,
            ["/format"]   = HandleFormatMenuAsync,
            ["/find"]     = (bot, chatId, args, ct) => HandleFindAsync(bot, chatId, args, ct, finder),
            ["/feedback"] = HandleFeedbackAsync,
        };
    }

    // ── Text message routing ──────────────────────────────────────────────────

    public async Task RouteAsync(
        ITelegramBotClient bot,
        long chatId,
        string text,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var spaceIndex = text.IndexOf(' ');
        var command    = spaceIndex >= 0 ? text[..spaceIndex] : text;
        var args       = spaceIndex >= 0 ? text[(spaceIndex + 1)..].Trim() : string.Empty;

        if (!_handlers.TryGetValue(command, out var handler))
        {
            await bot.SendMessage(chatId,
                "❓ Unknown command. Type /help to see available commands.",
                cancellationToken: ct);
            return;
        }

        if (!_rateLimiter.TryConsume(chatId))
        {
            await bot.SendMessage(chatId,
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

    // ── Callback query routing (inline keyboard button presses) ──────────────

    public async Task RouteCallbackAsync(
        ITelegramBotClient bot,
        CallbackQuery callback,
        CancellationToken ct)
    {
        var chatId = callback.Message?.Chat.Id;
        var data   = callback.Data ?? string.Empty;

        if (chatId is null || !data.StartsWith(FormatCallbackPrefix))
        {
            // Unknown callback — just acknowledge it silently.
            await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
            return;
        }

        var formatStr = data[FormatCallbackPrefix.Length..];

        if (!Enum.TryParse<OutputFormat>(formatStr, ignoreCase: true, out var chosen))
        {
            await bot.AnswerCallbackQuery(callback.Id, "⚠️ Unknown format.", cancellationToken: ct);
            return;
        }

        _formatStore.Set(chatId.Value, chosen);
        _logger.LogInformation("Chat {ChatId} set format to {Format}.", chatId, chosen);

        // Edit the original menu message to show the confirmed selection.
        await bot.EditMessageText(
            chatId.Value,
            callback.Message!.MessageId,
            FormatMenuText(chatId.Value),
            parseMode: ParseMode.Html,
            replyMarkup: BuildFormatKeyboard(chatId.Value),
            cancellationToken: ct);

        // Toast notification inside Telegram (disappears automatically).
        await bot.AnswerCallbackQuery(
            callback.Id,
            $"✅ Format set to {chosen}",
            cancellationToken: ct);
    }

    // ── Command handlers ──────────────────────────────────────────────────────

    private static Task HandleStartAsync(
        ITelegramBotClient bot, long chatId, string _, CancellationToken ct) =>
        bot.SendMessage(chatId,
            "👋 <b>Welcome to the Gas Station Finder bot!</b>\n\n" +
            "Use <code>/find [location]</code> to search for nearby gas stations.\n\n" +
            "Examples:\n" +
            "• <code>/find H8N2P7</code>\n" +
            "Use /format to choose your preferred output style.\n" +
            "Use /feedback to send us a message.",
            parseMode: ParseMode.Html,
            cancellationToken: ct);

    private static Task HandleHelpAsync(
        ITelegramBotClient bot, long chatId, string _, CancellationToken ct) =>
        bot.SendMessage(chatId,
            "📖 <b>Available commands</b>\n\n" +
            "/find [location] — Search gas stations near a location\n" +
            "/format — Choose output style\n" +
            "/feedback [message] — Send feedback to the bot owner\n" +
            "/start — Welcome message\n" +
            "/help — Show this message",
            parseMode: ParseMode.Html,
            cancellationToken: ct);

    private Task HandleFormatMenuAsync(
        ITelegramBotClient bot, long chatId, string _, CancellationToken ct) =>
        bot.SendMessage(chatId,
            FormatMenuText(chatId),
            parseMode: ParseMode.Html,
            replyMarkup: BuildFormatKeyboard(chatId),
            cancellationToken: ct);

    private async Task HandleFeedbackAsync(
        ITelegramBotClient bot, long chatId, string args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            await bot.SendMessage(chatId,
                "💬 <b>Usage:</b> <code>/feedback [your message]</code>\n\n" +
                "Example: <code>/feedback The search results are missing my local station.</code>",
                parseMode: ParseMode.Html,
                cancellationToken: ct);
            return;
        }

        // Retrieve the sender's display name from the update context via the
        // BotService handler, which passes it through as part of chatId lookup.
        // Fall back to the chat ID string if no name is available.
        var senderName = $"id:{chatId}";

        await _feedbackService.SubmitAsync(chatId, senderName, args, ct);

        _logger.LogInformation("Feedback submitted by chat {ChatId}.", chatId);
    }

    private async Task HandleFindAsync(
        ITelegramBotClient bot,
        long chatId,
        string args,
        CancellationToken ct,
        GasStationFinder finder)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            await bot.SendMessage(chatId,
                _config.MissingArgumentMessage,
                parseMode: ParseMode.Html,
                cancellationToken: ct);
            return;
        }

        await bot.SendMessage(chatId,
            _config.SearchingMessage(args),
            parseMode: ParseMode.Html,
            cancellationToken: ct);

        string result;
        try
        {
            result = await finder.FindAsync(args, chatId);
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

    // ── Format menu helpers ───────────────────────────────────────────────────

    private string FormatMenuText(long chatId)
    {
        var current = _formatStore.Get(chatId);
        return "🖨 <b>Output Format</b>\n\n" +
               $"Current: <b>{current}</b>\n\n" +
               "<b>Compact</b> — one line per station with a 📍 map link\n" +
               "<b>Card</b> — name, price/distance, full address link\n" +
               "<b>Minimal</b> — name and price only\n" +
               "<b>Table</b> — fixed-width table inside <code>pre</code> tags\n\n" +
               "Tap a button to switch:";
    }

    private InlineKeyboardMarkup BuildFormatKeyboard(long chatId)
    {
        var current = _formatStore.Get(chatId);

        // Adds a ✓ checkmark to the currently active format button.
        string Label(OutputFormat fmt) =>
            fmt == current ? $"✓ {fmt}" : fmt.ToString();

        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Label(OutputFormat.Compact), $"{FormatCallbackPrefix}Compact"),
                InlineKeyboardButton.WithCallbackData(Label(OutputFormat.Card),    $"{FormatCallbackPrefix}Card"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Label(OutputFormat.Minimal), $"{FormatCallbackPrefix}Minimal"),
                InlineKeyboardButton.WithCallbackData(Label(OutputFormat.Table),   $"{FormatCallbackPrefix}Table"),
            },
        });
    }
}