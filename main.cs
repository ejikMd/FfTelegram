using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

/// <summary>
/// Entry point. Builds the Generic Host, registers all services in the DI
/// container, then starts the bot and web server.
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        // ── Global exception guards (prevent silent crashes) ──────────────────
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.Error.WriteLine($"[FATAL] Unhandled exception: {e.ExceptionObject}");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.Error.WriteLine($"[WARN] Unobserved task exception: {e.Exception}");
            e.SetObserved();
        };

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(config =>
            {
                // Environment variables are the primary config source.
                // Add appsettings.json as an optional supplement.
                config.AddEnvironmentVariables();
                config.AddJsonFile("appsettings.json", optional: true);
            })
            .ConfigureLogging((ctx, logging) =>
            {
                logging.ClearProviders();
                logging.AddSimpleConsole(o =>
                {
                    o.IncludeScopes   = true;
                    o.TimestampFormat = "HH:mm:ss ";
                    o.SingleLine      = true;
                });
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices((ctx, services) =>
            {
                var config = ctx.Configuration;

                // ── Validate required config eagerly ──────────────────────────
                var botToken = config["BOT_TOKEN"];
                if (string.IsNullOrWhiteSpace(botToken))
                    throw new InvalidOperationException("BOT_TOKEN environment variable is not set.");

                var geoapifyKey = config["GEOAPIFY_KEY"] ?? string.Empty;

                // ── Process-level CancellationTokenSource ─────────────────────
                // Registered as a singleton so BotService and the shutdown handler
                // share the same instance.
                var processCts = new CancellationTokenSource();
                services.AddSingleton(processCts);

                // ── Telegram bot client ───────────────────────────────────────
                services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));

                // ── Infrastructure / external services ────────────────────────
                services.AddSingleton<IGeocoder, GeocoderCaService>();

                // GeoapifyLocationService implements both IReverseGeocoder and
                // IDistanceCalculator — register one instance, expose via both interfaces.
                services.AddSingleton<GeoapifyLocationService>(sp => new GeoapifyLocationService(geoapifyKey, sp.GetRequiredService<ILogger<GeoapifyLocationService>>()));
                services.AddSingleton<IReverseGeocoder>(sp => sp.GetRequiredService<GeoapifyLocationService>());
                services.AddSingleton<IDistanceCalculator>(sp => sp.GetRequiredService<GeoapifyLocationService>());

                services.AddSingleton<GasBuddyHttpClient>();
                services.AddSingleton<IStationDetailsService, StationDetailsService>();

                // ── App-layer config / stores ─────────────────────────────────
                services.AddSingleton(_ => StationFormatterConfig.FromEnvironment());
                services.AddSingleton<UserFormatStore>();
                services.AddSingleton<UserRateLimiter>(_ => new UserRateLimiter(cooldown: TimeSpan.FromSeconds(3)));

                // ── Domain services ───────────────────────────────────────────
                services.AddSingleton<IRequestService, RequestMapService>();
                services.AddSingleton<GasStationFinder>();

                // ── Feedback ──────────────────────────────────────────────────
                // Always registers — FeedbackService logs and swallows delivery
                // errors gracefully when ownerChatId is 0 (missing/invalid env var).
                long.TryParse(config["OWNER_CHAT_ID"], out var ownerChatId);
                services.AddSingleton(sp => new FeedbackService(
                    sp.GetRequiredService<ITelegramBotClient>(),
                    ownerChatId,
                    sp.GetRequiredService<ILogger<FeedbackService>>()));

                // ── Routing & bot lifecycle ───────────────────────────────────
                services.AddSingleton<MessageRouter>();
                services.AddSingleton<BotService>();
            })
            .Build();

        // ── Post-build startup logging ────────────────────────────────────────
        var logger           = host.Services.GetRequiredService<ILogger<Program>>();
        var configuration    = host.Services.GetRequiredService<IConfiguration>();
        var formatterConfig  = host.Services.GetRequiredService<StationFormatterConfig>();

        logger.LogInformation("Output format: {Format}, MaxResults: {Max}",
            formatterConfig.Format, formatterConfig.MaxResults);

        if (string.IsNullOrWhiteSpace(configuration["geoapify"]))
            logger.LogWarning("geoapify environment variable is not set. Distance features will be disabled.");

        if (!long.TryParse(configuration["OWNER_CHAT_ID"], out _))
            logger.LogWarning(
                "OWNER_CHAT_ID is not set or invalid. /feedback will not forward messages to the owner.");

        // ── Resolve top-level singletons ──────────────────────────────────────
        var botService       = host.Services.GetRequiredService<BotService>();
        var gasStationFinder = host.Services.GetRequiredService<GasStationFinder>();

        // ── Graceful shutdown on Ctrl+C ───────────────────────────────────────
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            logger.LogInformation("Ctrl+C received.");
            botService.RequestShutdown();
        };

        // ── Start bot ─────────────────────────────────────────────────────────
        try
        {
            await botService.StartAsync();
            logger.LogInformation("Bot started successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bot failed to start. Web server will still run.");
        }

        // ── Start web server (blocks until process exits) ─────────────────────
        await WebServer.RunAsync(args, botService, gasStationFinder);
    }
}