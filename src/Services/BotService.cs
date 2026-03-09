using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

/// <summary>
/// Manages the Telegram bot lifecycle: startup, polling, and graceful shutdown.
/// </summary>
public sealed class BotService : IDisposable
{
    private readonly ITelegramBotClient _botClient;
    private readonly MessageRouter _router;
    private readonly ILogger<BotService> _logger;
    private readonly CancellationTokenSource _cts;

    private volatile bool _initialized;
    private volatile bool _shuttingDown;
    private int _messagesProcessed;
    private readonly DateTime _startTime = DateTime.UtcNow;

    public bool IsInitialized => _initialized;
    public bool IsShuttingDown => _shuttingDown;
    public int MessagesProcessed => _messagesProcessed;
    public TimeSpan Uptime => DateTime.UtcNow - _startTime;

    public BotService(
        ITelegramBotClient botClient,
        MessageRouter router,
        ILogger<BotService> logger,
        CancellationTokenSource cts)
    {
        _botClient = botClient;
        _router = router;
        _logger = logger;
        _cts = cts;
    }

    /// <summary>
    /// Clears stale updates, then starts long-polling in a background task.
    /// </summary>
    public async Task StartAsync()
    {
        await _botClient.DeleteWebhook(cancellationToken: _cts.Token);
        _logger.LogInformation("Webhook deleted.");

        // Drain any queued updates so we don't process stale messages on restart.
        var pending = await _botClient.GetUpdates(cancellationToken: _cts.Token);
        if (pending.Length > 0)
        {
            var maxOffset = pending.Max(u => u.Id) + 1;
            await _botClient.GetUpdates(offset: maxOffset, cancellationToken: _cts.Token);
            _logger.LogInformation("Drained {Count} stale updates.", pending.Length);
        }

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        _botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: _cts.Token
        );

        _initialized = true;
        _logger.LogInformation("Bot polling started.");

        // Attach a fault observer so silent crashes are surfaced in logs.
        _ = Task.Run(async () =>
        {
            try
            {
                // Keep alive until cancellation
                var tcs = new TaskCompletionSource();
                await using var reg = _cts.Token.Register(() => tcs.TrySetResult());
                await tcs.Task;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Bot background task faulted.");
            }
            finally
            {
                _logger.LogInformation("Bot polling ended.");
            }
        });
    }

    public void RequestShutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _logger.LogInformation("Shutdown requested.");
        _cts.Cancel();
    }

    // -------------------------------------------------------------------------
    // Private handlers
    // -------------------------------------------------------------------------

    private async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken ct)
    {
        if (_shuttingDown) return;
        if (update.Message is not { Text: { } messageText } message) return;

        Interlocked.Increment(ref _messagesProcessed);
        var chatId = message.Chat.Id;
        var user = message.Chat.Username ?? message.Chat.FirstName ?? $"id:{chatId}";

        _logger.LogInformation("Message from {User}: {Text}", user, messageText);

        await _router.RouteAsync(botClient, chatId, messageText, ct);
    }

    private Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        CancellationToken ct)
    {
        if (exception.Message.Contains("terminated by other getUpdates request"))
        {
            _logger.LogWarning("Conflict: another bot instance is running. Shutting down this instance.");
            RequestShutdown();
        }
        else
        {
            _logger.LogError(exception, "Telegram polling error.");
        }
        return Task.CompletedTask;
    }

    public void Dispose() => _cts.Dispose();
}