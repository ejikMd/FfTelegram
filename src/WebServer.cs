using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Configures and runs the ASP.NET Core web server.
/// Keeping this separate from Program.cs makes each concern independently testable.
/// </summary>
public static class WebServer
{
    public static async Task RunAsync(string[] args, BotService botService, GasStationFinder gasStationFinder, StationSyncService stationSyncService)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddHealthChecks()
            .AddCheck("bot", () => botService.IsInitialized && !botService.IsShuttingDown
                ? HealthCheckResult.Healthy("Bot is running")
                : HealthCheckResult.Unhealthy("Bot is not running"))
            .AddCheck("uptime", () =>
                HealthCheckResult.Healthy($"Uptime: {botService.Uptime:g}"));

        var app = builder.Build();

        // --- Health / probe endpoints ---

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (ctx, report) =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsJsonAsync(new
                {
                    status           = report.Status.ToString(),
                    checks           = report.Entries.Select(e => new
                    {
                        name        = e.Key,
                        status      = e.Value.Status.ToString(),
                        description = e.Value.Description,
                        duration    = e.Value.Duration.ToString()
                    }),
                    totalDuration      = report.TotalDuration.ToString(),
                    uptime             = botService.Uptime,
                    messagesProcessed  = botService.MessagesProcessed,
                    botInitialized     = botService.IsInitialized,
                    isShuttingDown     = botService.IsShuttingDown
                });
            }
        });

        // /ready — GET returns body; HEAD returns only status code (200/503).
        app.MapMethods("/ready", new[] { "GET", "HEAD" }, async ctx =>
        {
            var isReady = botService.IsInitialized && !botService.IsShuttingDown;
            ctx.Response.StatusCode = isReady ? 200 : 503;
            if (ctx.Request.Method != HttpMethods.Head)
                await ctx.Response.WriteAsync(isReady ? "Ready" : "Not Ready");
        });

        // /live — always 200; HEAD is a cheap liveness ping for UptimeRobot.
        app.MapMethods("/live", new[] { "GET", "HEAD" }, async ctx =>
        {
            ctx.Response.StatusCode = 200;
            if (ctx.Request.Method != HttpMethods.Head)
                await ctx.Response.WriteAsync("Alive");
        });

        // /status — HEAD returns 200 (running) or 503 (stopped) with no body,
        // which lets UptimeRobot alert on bot failure without parsing JSON.
        app.MapMethods("/status", new[] { "GET", "HEAD" }, async ctx =>
        {
            var isRunning = botService.IsInitialized && !botService.IsShuttingDown;
            ctx.Response.StatusCode = isRunning ? 200 : 503;

            if (ctx.Request.Method != HttpMethods.Head)
                await ctx.Response.WriteAsJsonAsync(new
                {
                    status            = isRunning ? "running" : "stopped",
                    uptime            = botService.Uptime,
                    messagesProcessed = botService.MessagesProcessed,
                    timestamp         = DateTime.UtcNow,
                    isStopped         = botService.IsStopped,
                    isShuttingDown    = botService.IsShuttingDown
                });
        });

        // --- Control endpoints ---

        app.MapPost("/shutdown", async ctx =>
        {
            if (!botService.IsStopped && !botService.IsShuttingDown)
            {
                botService.RequestStop();
                await ctx.Response.WriteAsync("Bot stopped.");
            }
            else
            {
                await ctx.Response.WriteAsync("Bot is already stopped.");
            }
        });

        app.MapPost("/start", async ctx =>
        {
            if (botService.IsStopped && !botService.IsShuttingDown)
            {
                await botService.RestartAsync();
                await ctx.Response.WriteAsync("Bot started.");
            }
            else if (botService.IsShuttingDown)
            {
                ctx.Response.StatusCode = 503;
                await ctx.Response.WriteAsync("Process is shutting down. Restart the server.");
            }
            else
            {
                await ctx.Response.WriteAsync("Bot is already running.");
            }
        });

        app.MapGet("/check", async context =>
        {
            Console.WriteLine($"Running test on J4W2N3");
            var stations = await gasStationFinder.FindAsync("J4W2N3");

            await context.Response.WriteAsJsonAsync(new
            {
                result = stations
            });
        });

        app.MapPost("/sync/unknown", async ctx =>
        {
            var result = await stationSyncService.SyncUnknownAsync();
            ctx.Response.StatusCode = result.Skipped ? 409 : 200;
            await ctx.Response.WriteAsJsonAsync(result);
        });

        app.MapPost("/sync/all", async ctx =>
        {
            var result = await stationSyncService.SyncAllAsync();
            ctx.Response.StatusCode = result.Skipped ? 409 : 200;
            await ctx.Response.WriteAsJsonAsync(result);
        });

        // --- UI ---
        app.MapMethods("/", new[] { "GET", "HEAD" }, async ctx =>
        {
            var isRunning = botService.IsInitialized && !botService.IsStopped && !botService.IsShuttingDown;
            ctx.Response.StatusCode = isRunning ? 200 : 503;

            if (ctx.Request.Method == HttpMethods.Head) return;

            ctx.Response.ContentType = "text/html";

            var page = new StatusPageBuilder()
                .WithBotService(botService)
                .WithIsRunning(isRunning)
                .Build();

            await ctx.Response.WriteAsync(page);
        });

        var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
        app.Urls.Add($"http://0.0.0.0:{port}");

        Console.WriteLine($"Web server listening on port {port}");
        await app.RunAsync();
    }
}