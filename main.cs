using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Polling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

class Program
{
    private static ITelegramBotClient _botClient;
    private static CancellationTokenSource _cts;
    private static DateTime _startTime = DateTime.UtcNow;
    private static int _messagesProcessed = 0;
    private static bool _botInitialized = false;
    private static GasStationFinder _gasStationFinder;
    private static IRequestService _requestService;
    private static Task _botTask;
    private static bool _isShuttingDown = false;

    static async Task Main(string[] args)
    {
        // Get bot token from environment variable (set in Replit Secrets)
        var botToken = Environment.GetEnvironmentVariable("BOT_TOKEN");
        var geoapifyApiKey = Environment.GetEnvironmentVariable("geoapify");

        if (string.IsNullOrEmpty(botToken))
        {
            Console.WriteLine("Please set BOT_TOKEN in Replit Secrets");
            return;
        }

        if (string.IsNullOrEmpty(geoapifyApiKey))
        {
            Console.WriteLine("Warning: geoapify not set. Distance calculation will not work.");
            geoapifyApiKey = ""; // Allow bot to run even without API key
        }

        _botClient = new TelegramBotClient(botToken);
        _cts = new CancellationTokenSource();

        // Option 1: Use geocoder.ca (free, may have limitations)
        IGeocoder geocoder = new GeocoderCaService(); // Optionally pass API key

        // Option 2: Use OpenStreetMap (free, no API key, but rate limited)
        // IGeocoder geocoder = new OpenStreetMapGeocoderService();
        
        IStationDetailsService stationDetailsService = new StationDetailsService(geoapifyApiKey);
        IDistanceCalculator distanceCalculator = new GeoapifyDistanceCalculator(geoapifyApiKey);

        // Initialize services
        _requestService = new RequestMapService(geocoder, stationDetailsService, distanceCalculator);
        _gasStationFinder = new GasStationFinder(_requestService);

        // Handle Ctrl+C gracefully
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            _isShuttingDown = true;
            Console.WriteLine("Shutdown signal received. Stopping bot...");
            _cts.Cancel();
        };

        try
        {
            // First, delete any existing webhook
            await _botClient.DeleteWebhook();
            Console.WriteLine("Webhook deleted successfully.");

            // Get and skip all pending updates to clear the queue
            var updates = await _botClient.GetUpdates();
            if (updates.Length > 0)
            {
                var maxOffset = updates.Max(u => u.Id) + 1;
                await _botClient.GetUpdates(offset: maxOffset);
                Console.WriteLine($"Cleared {updates.Length} pending updates (offset: {maxOffset}).");
            }

            // Start the bot in polling mode with a dedicated task
            _botTask = Task.Run(() => StartBot(_cts.Token));

            _botInitialized = true;
            Console.WriteLine("Bot is running...");

            // Start a simple web server to keep the Replit app alive
            await StartWebServer(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize bot: {ex.Message}");
            _botInitialized = false;
            await StartWebServer(args);
        }
    }

    static async Task StartBot(CancellationToken cancellationToken)
    {
        try
        {
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>() // Receive all update types
                // No ThrowPendingUpdates property - it doesn't exist
            };

            _botClient.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: cancellationToken
            );

            Console.WriteLine("Bot polling started successfully.");

            // Keep the bot running until cancelled
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Bot stopped gracefully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in StartBot: {ex.Message}");
        }
        finally
        {
            Console.WriteLine("Bot polling ended.");
        }
    }

    static async Task StartWebServer(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add health checks
        builder.Services.AddHealthChecks()
            .AddCheck("bot_health", () =>
            {
                if (_botInitialized && _botClient != null && !_isShuttingDown)
                {
                    return HealthCheckResult.Healthy("Bot is running");
                }
                return HealthCheckResult.Unhealthy("Bot is not initialized or shutting down");
            })
            .AddCheck("uptime", () =>
            {
                var uptime = DateTime.UtcNow - _startTime;
                return HealthCheckResult.Healthy($"Uptime: {uptime:g}");
            });

        var app = builder.Build();

        // Health check endpoint
        app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var response = new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.Select(e => new
                    {
                        name = e.Key,
                        status = e.Value.Status.ToString(),
                        description = e.Value.Description,
                        duration = e.Value.Duration.ToString()
                    }),
                    totalDuration = report.TotalDuration.ToString(),
                    uptime = DateTime.UtcNow - _startTime,
                    messagesProcessed = _messagesProcessed,
                    botInitialized = _botInitialized,
                    isShuttingDown = _isShuttingDown
                };
                await context.Response.WriteAsJsonAsync(response);
            }
        });

        // Shutdown endpoint for manual cleanup
        app.MapPost("/shutdown", async context =>
        {
            if (!_isShuttingDown)
            {
                _isShuttingDown = true;
                _cts.Cancel();
                await context.Response.WriteAsync("Shutdown signal sent to bot");
            }
            else
            {
                await context.Response.WriteAsync("Bot is already shutting down");
            }
        });

        // Root endpoint with bot info
        app.MapGet("/", async context =>
        {
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync($@"
                <html>
                    <head><title>Telegram Bot Status</title></head>
                    <body style='font-family: Arial, sans-serif; padding: 20px;'>
                        <h1>🤖 Telegram Bot</h1>
                        <p>Status: <strong style='color: {(_botInitialized && !_isShuttingDown ? "green" : "red")}'>{(_botInitialized && !_isShuttingDown ? "Running" : "Stopped")}</strong></p>
                        <p>Uptime: {DateTime.UtcNow - _startTime:g}</p>
                        <p>Messages processed: {_messagesProcessed}</p>
                        <p>Health check: <a href='/health'>/health</a></p>
                        <p>Sample check: <a href='/check'>/check</a></p>
                        <p>Shutdown: <button onclick='fetch(""/shutdown"", {{method: ""POST""}})'>Shutdown Bot</button></p>
                    </body>
                </html>
            ");
        });

        app.MapGet("/check", async context =>
        {
            Console.WriteLine($"Running test on H8N2P7");
            var stations = await _gasStationFinder.FindAsync("H8N2P7");
            
            await context.Response.WriteAsJsonAsync(new
            {
                status = _botInitialized && !_isShuttingDown ? "running" : "stopped",
                uptime = DateTime.UtcNow - _startTime,
                messagesProcessed = _messagesProcessed,
                timestamp = DateTime.UtcNow,
                isShuttingDown = _isShuttingDown,
                result = stations
            });
        });

        
        // Simple status endpoint
        app.MapGet("/status", async context =>
        {
            await context.Response.WriteAsJsonAsync(new
            {
                status = _botInitialized && !_isShuttingDown ? "running" : "stopped",
                uptime = DateTime.UtcNow - _startTime,
                messagesProcessed = _messagesProcessed,
                timestamp = DateTime.UtcNow,
                isShuttingDown = _isShuttingDown
            });
        });

        // Readiness probe
        app.MapGet("/ready", async context =>
        {
            if (_botInitialized && !_isShuttingDown)
            {
                context.Response.StatusCode = 200;
                await context.Response.WriteAsync("Ready");
            }
            else
            {
                context.Response.StatusCode = 503;
                await context.Response.WriteAsync("Not Ready");
            }
        });

        // Liveness probe
        app.MapGet("/live", async context =>
        {
            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("Alive");
        });

        // Use the port Replit provides (default 8080)
        var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
        app.Urls.Add($"http://0.0.0.0:{port}");

        Console.WriteLine($"Web server started on port {port}");
        Console.WriteLine($"Health check available at: http://localhost:{port}/health");

        await app.RunAsync();
    }

    static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (_isShuttingDown) return;

            // Only process text messages
            if (update.Message is not { Text: { } messageText } message)
                return;

            System.Threading.Interlocked.Increment(ref _messagesProcessed);
            var chatId = message.Chat.Id;
            
            // Safely get username or use first name as fallback
            string userIdentifier = message.Chat.Username ?? 
                                    message.Chat.FirstName ?? 
                                    message.Chat.LastName ?? 
                                    $"User {chatId}";
            Console.WriteLine($"Received message: {messageText} from {userIdentifier}");

            // Check if message starts with "/find"
            if (messageText.StartsWith("/find"))
            {
                // Extract the parameter after "/find"
                var searchGas = messageText.Length > 5 ? messageText.Substring(5).Trim() : "";

                if (string.IsNullOrEmpty(searchGas))
                {
                    await botClient.SendMessage(
                        chatId: chatId,
                        text: "⛽ Please provide a location to search. Usage: /find [city, address, or zip code]",
                        cancellationToken: cancellationToken
                    );
                    return;
                }

                // Send "searching" message
                await botClient.SendMessage(
                    chatId: chatId,
                    text: $"🔍 Searching for gas stations near: <i>{searchGas}</i>...",
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken
                );

                // Call the find method from the GasStationFinder class
                string result;
                try
                {
                    result = await _gasStationFinder.FindAsync(searchGas);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    result = "⚠️ <b>Service is temporarily unavailable due to high demand.</b> Please try again in a few minutes.";
                    Console.WriteLine("Stopped processing due to Rate Limit (429)");
                }
                catch (Exception ex)
                {
                    result = "❌ An error occurred while searching for gas stations. Please try again later.";
                    Console.WriteLine($"Search error: {ex.Message}");
                }

                // Send the result back to the user
                await botClient.SendMessage(
                    chatId: chatId,
                    text: result,
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken
                );

                Console.WriteLine($"Responded to /find query for: {searchGas}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in HandleUpdateAsync: {ex.Message}");
        }
    }

    static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        if (exception.Message.Contains("terminated by other getUpdates request"))
        {
            Console.WriteLine("⚠️ Conflict detected - another bot instance is running");
            Console.WriteLine("This instance will stop. Please ensure only one bot runs at a time.");

            // Signal shutdown
            _isShuttingDown = true;
            _cts.Cancel();
        }
        else
        {
            Console.WriteLine($"Error occurred: {exception.Message}");
        }
        return Task.CompletedTask;
    }
}