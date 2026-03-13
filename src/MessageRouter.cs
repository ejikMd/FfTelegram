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
    private const string FormatCallbackPrefix = "fmt:";

    // Command constants for better maintainability
    private static class Commands
    {
        public const string Start = "/start";
        public const string Help = "/help";
        public const string Format = "/format";
        public const string Find = "/find";
        public const string Feedback = "/feedback";
    }

    // Pre-compiled response messages
    private static class Responses
    {
        public const string UnknownCommand = "❓ Unknown command. Type /help to see available commands.";
        public const string RateLimited = "⏳ You're sending commands too quickly. Please wait a moment.";
        public const string UnexpectedError = "❌ An unexpected error occurred. Please try again later.";
        public const string ServiceUnavailable = "⚠️ <b>Service temporarily unavailable due to high demand.</b> Please try again in a few minutes.";
        public const string SearchFailed = "❌ An error occurred while searching. Please try again later.";

        public static readonly string Start = 
            "👋 <b>Welcome to the Gas Station Finder bot!</b>\n\n" +
            "Use <code>/find [location]</code> to search for nearby gas stations.\n\n" +
            "Examples:\n" +
            "• <code>/find H8N2P7</code>\n" +
            "Use /format to choose your preferred output style.\n" +
            "Use /feedback to send us a message.";

        public static readonly string Help = 
            "📖 <b>Available commands</b>\n\n" +
            "/find [location] — Search gas stations near a location\n" +
            "/format — Choose output style\n" +
            "/feedback [message] — Send feedback to the bot owner\n" +
            "/start — Welcome message\n" +
            "/help — Show this message";

        public static readonly string FeedbackUsage = 
            "💬 <b>Usage:</b> <code>/feedback [your message]</code>\n\n" +
            "Example: <code>/feedback The search results are missing my local station.</code>";
    }

    private readonly Dictionary<string, CommandHandler> _handlers;
    private readonly ILogger<MessageRouter> _logger;
    private readonly UserRateLimiter _rateLimiter;
    private readonly StationFormatterConfig _config;
    private readonly UserFormatStore _formatStore;
    private readonly FeedbackService _feedbackService;
    private readonly GasStationFinder _finder;

    // Delegate for command handlers to reduce dictionary value size
    private delegate Task CommandHandler(
        ITelegramBotClient bot, 
        Message message, 
        string args, 
        CancellationToken ct);

    public MessageRouter(
        GasStationFinder finder,
        UserRateLimiter rateLimiter,
        StationFormatterConfig config,
        UserFormatStore formatStore,
        FeedbackService feedbackService,
        ILogger<MessageRouter> logger)
    {
        _finder = finder ?? throw new ArgumentNullException(nameof(finder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _formatStore = formatStore ?? throw new ArgumentNullException(nameof(formatStore));
        _feedbackService = feedbackService ?? throw new ArgumentNullException(nameof(feedbackService));

        _handlers = new Dictionary<string, CommandHandler>(StringComparer.OrdinalIgnoreCase)
        {
            [Commands.Start] = HandleStartAsync,
            [Commands.Help] = HandleHelpAsync,
            [Commands.Format] = HandleFormatMenuAsync,
            [Commands.Find] = HandleFindAsync,
            [Commands.Feedback] = HandleFeedbackAsync,
        };
    }

    public async Task RouteAsync(
        ITelegramBotClient bot,
        Message message,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var chatId = message.Chat.Id;
        var text = message.Text ?? string.Empty;

        var (command, args) = ParseCommand(text);

        if (!_handlers.TryGetValue(command, out var handler))
        {
            await bot.SendMessage(chatId, Responses.UnknownCommand, cancellationToken: ct);
            return;
        }

        if (!_rateLimiter.TryConsume(chatId))
        {
            await bot.SendMessage(chatId, Responses.RateLimited, cancellationToken: ct);
            return;
        }

        try
        {
            await handler(bot, message, args, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in command {Command} for chat {ChatId}.", command, chatId);
            await bot.SendMessage(chatId, Responses.UnexpectedError, cancellationToken: ct);
        }
        finally
        {
            _logger.LogInformation("Command {Command} handled in {ElapsedMs}ms.", command, sw.ElapsedMilliseconds);
        }
    }

    public async Task RouteCallbackAsync(
        ITelegramBotClient bot,
        CallbackQuery callback,
        CancellationToken ct)
    {
        var chatId = callback.Message?.Chat.Id;
        var data = callback.Data ?? string.Empty;

        if (chatId is null || !data.StartsWith(FormatCallbackPrefix))
        {
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

        await bot.EditMessageText(
            chatId.Value,
            callback.Message!.MessageId,
            FormatMenuText(chosen),
            parseMode: ParseMode.Html,
            replyMarkup: BuildFormatKeyboard(chosen),
            cancellationToken: ct);

        await bot.AnswerCallbackQuery(
            callback.Id,
            $"✅ Format set to {chosen}",
            cancellationToken: ct);
    }

    // Optimized command parsing
    private static (string command, string args) ParseCommand(string text)
    {
        if (string.IsNullOrEmpty(text))
            return (string.Empty, string.Empty);

        var spaceIndex = text.IndexOf(' ');
        return spaceIndex >= 0 
            ? (text[..spaceIndex], text[(spaceIndex + 1)..].Trim()) 
            : (text, string.Empty);
    }

    private static Task HandleStartAsync(
        ITelegramBotClient bot, Message message, string args, CancellationToken ct) =>
        bot.SendMessage(message.Chat.Id, Responses.Start, ParseMode.Html, cancellationToken: ct);

    private static Task HandleHelpAsync(
        ITelegramBotClient bot, Message message, string args, CancellationToken ct) =>
        bot.SendMessage(message.Chat.Id, Responses.Help, ParseMode.Html, cancellationToken: ct);

    private Task HandleFormatMenuAsync(
        ITelegramBotClient bot, Message message, string args, CancellationToken ct)
    {
        var currentFormat = _formatStore.Get(message.Chat.Id);
        return bot.SendMessage(message.Chat.Id,
            FormatMenuText(currentFormat),
            parseMode: ParseMode.Html,
            replyMarkup: BuildFormatKeyboard(currentFormat),
            cancellationToken: ct);
    }

    private async Task HandleFeedbackAsync(
        ITelegramBotClient bot, Message message, string args, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        if (string.IsNullOrWhiteSpace(args))
        {
            await bot.SendMessage(chatId, Responses.FeedbackUsage, ParseMode.Html, cancellationToken: ct);
            return;
        }

        var senderName = message.Chat.Username is { } u
            ? $"@{u}"
            : message.Chat.FirstName ?? $"id:{chatId}";

        await _feedbackService.SubmitAsync(chatId, senderName, message.MessageId, args, ct);
        _logger.LogInformation("Feedback submitted by chat {ChatId}.", chatId);
    }

    private async Task HandleFindAsync(
        ITelegramBotClient bot, Message message, string args, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        if (string.IsNullOrWhiteSpace(args))
        {
            await bot.SendMessage(chatId, _config.MissingArgumentMessage, ParseMode.Html, cancellationToken: ct);
            return;
        }

        await bot.SendMessage(chatId, _config.SearchingMessage(args), ParseMode.Html, cancellationToken: ct);

        string result;
        try
        {
            result = await _finder.FindAsync(args, chatId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            result = Responses.ServiceUnavailable;
            _logger.LogWarning("Rate-limited (429) while searching for: {Location}", args);
        }
        catch (Exception ex)
        {
            result = Responses.SearchFailed;
            _logger.LogError(ex, "Search failed for location: {Location}", args);
        }

        await bot.SendMessage(chatId, result, ParseMode.Html, cancellationToken: ct);
        _logger.LogInformation("Responded to /find for: {Location}", args);
    }

    private static string FormatMenuText(OutputFormat current)
    {
        return "🖨 <b>Output Format</b>\n\n" +
               $"Current: <b>{current}</b>\n\n" +
               "<b>Compact</b> — one line per station with a 📍 map link\n" +
               "<b>Card</b> — name, price/distance, full address link\n" +
               "<b>Minimal</b> — name and price only\n" +
               "<b>Table</b> — fixed-width table inside <code>pre</code> tags\n\n" +
               "Tap a button to switch:";
    }

    private static InlineKeyboardMarkup BuildFormatKeyboard(OutputFormat current)
    {
        string Label(OutputFormat fmt) => fmt == current ? $"✓ {fmt}" : fmt.ToString();

        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Label(OutputFormat.Compact), $"{FormatCallbackPrefix}Compact"),
                InlineKeyboardButton.WithCallbackData(Label(OutputFormat.Card), $"{FormatCallbackPrefix}Card"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Label(OutputFormat.Minimal), $"{FormatCallbackPrefix}Minimal"),
                InlineKeyboardButton.WithCallbackData(Label(OutputFormat.Table), $"{FormatCallbackPrefix}Table"),
            },
        });
    }
}