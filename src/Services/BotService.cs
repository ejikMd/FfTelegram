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
/// automatic reconnection, graceful stop, and restart.
///
/// Two-level cancellation:
///   _processCts  – cancelled only on full process shutdown (Ctrl+C).
///   _pollCts     – cancelled to stop polling; recreated on each restart.
/// </summary>
public sealed class BotService : IDisposable
{
    private readonly ITelegramBotClient  _botClient;
    private readonly MessageRouter       _router;
    private readonly ILogger<BotService> _logger;

    // Process-level token — cancelled only by Ctrl+C / RequestShutdown().
    private readonly CancellationTokenSource _processCts;

    // Polling-level token — cancelled by RequestStop() and recreated by RestartAsync().
    private CancellationTokenSource _pollCts;

    private volatile bool _initialized;
    private volatile bool _stopped;       // polling stopped (but process alive)
    private volatile bool _shuttingDown;  // full process shutdown requested
    private int _messagesProcessed;
    private readonly DateTime _startTime = DateTime.UtcNow;

    // Reconnection config
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxDelay      = TimeSpan.FromMinutes(2);

    public bool IsInitialized  => _initialized;
    public bool IsShuttingDown => _shuttingDown;
    public bool IsStopped      => _stopped;
    public int  MessagesProcessed => _messagesProcessed;
    public TimeSpan Uptime     => DateTime.UtcNow - _startTime;

    public BotService(
        ITelegramBotClient botClient,
        MessageRouter router,
        ILogger<BotService> logger,
        CancellationTokenSource processCts)
    {
        _botClient   = botClient;
        _router      = router;
        _logger      = logger;
        _processCts  = processCts;
        _pollCts     = CancellationTokenSource.CreateLinkedTokenSource(processCts.Token);
    }

    // -------------------------------------------------------------------------
    // Public lifecycle API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Drains stale updates then launches the supervised polling loop.
    /// </summary>
    public async Task StartAsync()
    {
        await _botClient.DeleteWebhook(cancellationToken: _processCts.Token);
        _logger.LogInformation("Webhook deleted.");

        var pending = await _botClient.GetUpdates(cancellationToken: _processCts.Token);
        if (pending.Length > 0)
        {
            var maxOffset = pending.Max(u => u.Id) + 1;
            await _botClient.GetUpdates(offset: maxOffset, cancellationToken: _processCts.Token);
            _logger.LogInformation("Drained {Count} stale updates.", pending.Length);
        }

        _initialized = true;
        _stopped     = false;
        _logger.LogInformation("Bot polling starting.");
        _ = Task.Run(() => SupervisedPollingLoopAsync(_pollCts.Token));
    }

    /// <summary>
    /// Stops polling without killing the process. Allows <see cref="RestartAsync"/> afterwards.
    /// </summary>
    public void RequestStop()
    {
        if (_stopped || _shuttingDown) return;
        _stopped = true;
        _logger.LogInformation("Bot stop requested.");
        _pollCts.Cancel();
    }

    /// <summary>
    /// Restarts polling after a <see cref="RequestStop"/>.
    /// No-op if already running or if the process is shutting down.
    /// </summary>
    public async Task RestartAsync()
    {
        if (!_stopped || _shuttingDown) return;

        _logger.LogInformation("Bot restart requested.");

        // Dispose the old poll CTS and create a fresh one linked to the process token.
        _pollCts.Dispose();
        _pollCts = CancellationTokenSource.CreateLinkedTokenSource(_processCts.Token);

        _stopped = false;
        _logger.LogInformation("Bot polling restarting.");
        await Task.Run(() => SupervisedPollingLoopAsync(_pollCts.Token));
    }

    /// <summary>
    /// Stops polling AND signals the process to exit (Ctrl+C path).
    /// </summary>
    public void RequestShutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _stopped      = true;
        _logger.LogInformation("Full shutdown requested.");
        _pollCts.Cancel();
        _processCts.Cancel();
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
                    AllowedUpdates     = Array.Empty<UpdateType>(),
                    DropPendingUpdates = false
                };

                await _botClient.ReceiveAsync(
                    HandleUpdateAsync,
                    HandleErrorAsync,
                    receiverOptions: options,
                    cancellationToken: ct);

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
                if (_stopped || _shuttingDown) break;

                _logger.LogError(ex,
                    "Polling crashed (attempt #{Attempt}). Reconnecting in {Delay}.",
                    attempt, delay);

                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { break; }

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
        if (_stopped || _shuttingDown) return;

        if (update.Message is { Text: { } text } message)
        {
            Interlocked.Increment(ref _messagesProcessed);
            var chatId = message.Chat.Id;
            var user   = message.Chat.Username ?? message.Chat.FirstName ?? $"id:{chatId}";
            _logger.LogInformation("Message from {User}: {Text}", user, text);
            await _router.RouteAsync(botClient, message, ct);
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
            _logger.LogWarning("Conflict: another bot instance detected. Stopping polling.");
            RequestStop();
            return Task.CompletedTask;
        }

        _logger.LogWarning(exception, "Telegram polling error (will retry).");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _pollCts.Dispose();
    }
}