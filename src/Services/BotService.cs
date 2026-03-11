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
/// Manages the Telegram bot lifecycle: startup, supervised polling with
/// automatic reconnection, and graceful shutdown.
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

    // Reconnection config
    private static readonly TimeSpan InitialDelay   = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxDelay        = TimeSpan.FromMinutes(2);
    private static readonly int MaxRetries           = int.MaxValue; // retry forever

    public bool IsInitialized  => _initialized;
    public bool IsShuttingDown => _shuttingDown;
    public int  MessagesProcessed => _messagesProcessed;
    public TimeSpan Uptime     => DateTime.UtcNow - _startTime;

    public BotService(
        ITelegramBotClient botClient,
        MessageRouter router,
        ILogger<BotService> logger,
        CancellationTokenSource cts)
    {
        _botClient = botClient;
        _router    = router;
        _logger    = logger;
        _cts       = cts;
    }

    /// <summary>
    /// Clears stale updates, then launches a supervised polling loop
    /// that automatically reconnects on failure.
    /// </summary>
    public async Task StartAsync()
    {
        await _botClient.DeleteWebhook(cancellationToken: _cts.Token);
        _logger.LogInformation("Webhook deleted.");

        // Drain any stale updates to avoid replaying old messages on restart.
        var pending = await _botClient.GetUpdates(cancellationToken: _cts.Token);
        if (pending.Length > 0)
        {
            var maxOffset = pending.Max(u => u.Id) + 1;
            await _botClient.GetUpdates(offset: maxOffset, cancellationToken: _cts.Token);
            _logger.LogInformation("Drained {Count} stale updates.", pending.Length);
        }

        _initialized = true;
        _logger.LogInformation("Bot polling starting.");

        // Fire-and-forget supervised loop — keeps running until _cts is cancelled.
        _ = Task.Run(() => SupervisedPollingLoopAsync(_cts.Token), _cts.Token);
    }

    public void RequestShutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _logger.LogInformation("Shutdown requested.");
        _cts.Cancel();
    }

    // -------------------------------------------------------------------------
    // Supervised polling loop
    // -------------------------------------------------------------------------

    private async Task SupervisedPollingLoopAsync(CancellationToken ct)
    {
        var delay   = InitialDelay;
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                attempt++;
                if (attempt > 1)
                    _logger.LogInformation("Reconnection attempt #{Attempt}.", attempt);

                var options = new ReceiverOptions
                {
                    AllowedUpdates = Array.Empty<UpdateType>(),
                    DropPendingUpdates = false
                };

                // ReceiveAsync blocks until ct is cancelled or a fatal error throws.
                await _botClient.ReceiveAsync(
                    HandleUpdateAsync,
                    HandleErrorAsync,
                    receiverOptions: options,
                    cancellationToken: ct);

                // ReceiveAsync returned without exception = cancellation was requested.
                _logger.LogInformation("Polling stopped cleanly.");
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogInformation("Polling cancelled.");
                break;
            }
            catch (Exception ex)
            {
                if (_shuttingDown) break;

                _logger.LogError(ex,
                    "Polling crashed (attempt #{Attempt}). Reconnecting in {Delay}.",
                    attempt, delay);

                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { break; }

                // Exponential backoff up to MaxDelay
                delay = delay < MaxDelay
                    ? TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks))
                    : MaxDelay;
            }
        }

        _logger.LogInformation("Polling supervisor exited.");
    }

    // -------------------------------------------------------------------------
    // Handlers
    // -------------------------------------------------------------------------

    private async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken ct)
    {
        if (_shuttingDown) return;

        // Reset backoff on successful update
        if (update.Message is { Text: { } text } message)
        {
            Interlocked.Increment(ref _messagesProcessed);
            var chatId = message.Chat.Id;
            var user   = message.Chat.Username ?? message.Chat.FirstName ?? $"id:{chatId}";
            _logger.LogInformation("Message from {User}: {Text}", user, text);
            await _router.RouteAsync(botClient, chatId, text, ct);
            return;
        }

        if (update.CallbackQuery is { } callback)
        {
            Interlocked.Increment(ref _messagesProcessed);
            await _router.RouteCallbackAsync(botClient, callback, ct);
        }
    }

    private Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        CancellationToken ct)
    {
        if (exception.Message.Contains("terminated by other getUpdates request"))
        {
            _logger.LogWarning("Conflict: another bot instance detected. Shutting down.");
            RequestShutdown();
            return Task.CompletedTask;
        }

        // All other errors are logged; the supervisor loop handles reconnection.
        _logger.LogWarning(exception, "Telegram polling error (will retry).");
        return Task.CompletedTask;
    }

    public void Dispose() => _cts.Dispose();
}
