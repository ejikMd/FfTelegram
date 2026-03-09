using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

/// <summary>
/// Entry point. Responsible only for reading config, wiring dependencies,
/// and handing off to BotService and WebServer.
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        // ── Logging ──────────────────────────────────────────────────────────
        using var loggerFactory = LoggerFactory.Create(b =>
            b.AddSimpleConsole(o =>
            {
                o.IncludeScopes   = true;
                o.TimestampFormat = "HH:mm:ss ";
            })
            .SetMinimumLevel(LogLevel.Information));

        var logger = loggerFactory.CreateLogger<Program>();

        // ── Configuration ────────────────────────────────────────────────────
        var botToken      = Environment.GetEnvironmentVariable("BOT_TOKEN");
        var geoapifyKey   = Environment.GetEnvironmentVariable("geoapify") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(botToken))
        {
            logger.LogCritical("BOT_TOKEN environment variable is not set. Exiting.");
            return;
        }

        if (string.IsNullOrWhiteSpace(geoapifyKey))
        {
            // Fail loudly — silent empty string leads to mysterious errors later.
            logger.LogWarning("geoapify environment variable is not set. Distance features will be disabled.");
        }

        // ── Service wiring ───────────────────────────────────────────────────
        var cts        = new CancellationTokenSource();
        var botClient  = new TelegramBotClient(botToken);

        IGeocoder              geocoder              = new GeocoderCaService();
        IStationDetailsService stationDetailsService = new StationDetailsService(geoapifyKey);
        IDistanceCalculator    distanceCalculator    = new GeoapifyDistanceCalculator(geoapifyKey);
        IRequestService        requestService        = new RequestMapService(geocoder, stationDetailsService, distanceCalculator);

        var gasStationFinder = new GasStationFinder(requestService);
        var rateLimiter      = new UserRateLimiter(cooldown: TimeSpan.FromSeconds(3));
        var router           = new MessageRouter(
                                    gasStationFinder,
                                    rateLimiter,
                                    loggerFactory.CreateLogger<MessageRouter>());

        using var botService = new BotService(
            botClient,
            router,
            loggerFactory.CreateLogger<BotService>(),
            cts);

        // ── Graceful shutdown on Ctrl+C ──────────────────────────────────────
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            logger.LogInformation("Ctrl+C received.");
            botService.RequestShutdown();
        };

        // ── Start bot ────────────────────────────────────────────────────────
        try
        {
            await botService.StartAsync();
            logger.LogInformation("Bot started successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bot failed to start. Web server will still run.");
        }

        // ── Start web server (blocks until process exits) ────────────────────
        await WebServer.RunAsync(args, botService, gasStationFinder);
    }
}
