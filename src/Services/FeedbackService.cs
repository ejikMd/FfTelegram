using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

/// <summary>
/// Forwards user feedback messages to the bot owner's Telegram account.
/// </summary>
public sealed class FeedbackService
{
    private readonly ITelegramBotClient   _botClient;
    private readonly long                 _ownerChatId;
    private readonly ILogger<FeedbackService> _logger;

    /// <param name="botClient">The shared Telegram bot client.</param>
    /// <param name="ownerChatId">
    ///     Your personal Telegram chat ID. Obtain it by messaging @userinfobot
    ///     or reading the chat ID from an incoming update in dev mode.
    /// </param>
    /// <param name="logger">Logger instance.</param>
    public FeedbackService(
        ITelegramBotClient botClient,
        long ownerChatId,
        ILogger<FeedbackService> logger)
    {
        if (ownerChatId == 0)
            throw new ArgumentException("Owner chat ID must be non-zero.", nameof(ownerChatId));

        _botClient   = botClient;
        _ownerChatId = ownerChatId;
        _logger      = logger;
    }

    /// <summary>
    /// Forwards <paramref name="feedbackText"/> to the owner and sends a
    /// confirmation reply to the user who submitted it.
    /// </summary>
    /// <param name="senderChatId">Chat ID of the user submitting feedback.</param>
    /// <param name="senderName">Display name of the sender (username or first name).</param>
    /// <param name="feedbackText">The raw feedback message text.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SubmitAsync(
        long senderChatId,
        string senderName,
        string feedbackText,
        CancellationToken ct)
    {
        var sanitized = feedbackText.Trim();

        // ── Forward to owner ──────────────────────────────────────────────────
        var ownerMessage =
            $"📬 <b>New Feedback</b>\n\n" +
            $"From: <b>{EscapeHtml(senderName)}</b> (<code>{senderChatId}</code>)\n" +
            $"Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n\n" +
            $"<blockquote>{EscapeHtml(sanitized)}</blockquote>";

        try
        {
            await _botClient.SendMessage(
                _ownerChatId,
                ownerMessage,
                parseMode: ParseMode.Html,
                cancellationToken: ct);

            _logger.LogInformation(
                "Feedback forwarded to owner from chat {SenderChatId} ({SenderName}).",
                senderChatId, senderName);
        }
        catch (Exception ex)
        {
            // Don't surface owner-delivery failures to the user — log and continue.
            _logger.LogError(ex,
                "Failed to forward feedback to owner (chat {OwnerChatId}).", _ownerChatId);
        }

        // ── Confirm receipt to the user ───────────────────────────────────────
        await _botClient.SendMessage(
            senderChatId,
            "✅ <b>Thank you for your feedback!</b> It has been sent to the bot owner.",
            parseMode: ParseMode.Html,
            cancellationToken: ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
}