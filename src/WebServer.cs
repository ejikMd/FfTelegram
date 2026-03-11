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
    public static async Task RunAsync(string[] args, BotService botService, GasStationFinder gasStationFinder)
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
            Console.WriteLine($"Running test on H3V1H3");
            var stations = await gasStationFinder.FindAsync("H3V1H3");

            await context.Response.WriteAsJsonAsync(new
            {
                result = stations
            });
        });


        // --- UI ---

        // HEAD on / lets UptimeRobot monitor the root URL with zero HTML overhead.
        // Returns 200 when the bot is healthy, 503 when stopped.
        app.MapMethods("/", new[] { "GET", "HEAD" }, async ctx =>
        {
            var isRunning = botService.IsInitialized && !botService.IsStopped && !botService.IsShuttingDown;
            ctx.Response.StatusCode = isRunning ? 200 : 503;

            if (ctx.Request.Method == HttpMethods.Head) return;

            var color  = isRunning ? "green" : "red";
            var status = botService.IsShuttingDown ? "Shutting Down"
                       : botService.IsStopped      ? "Stopped"
                                                   : "Running";

            ctx.Response.ContentType = "text/html";
            await ctx.Response.WriteAsync($@"
<!DOCTYPE html>
<html lang='en'>
<head>
  <meta charset='utf-8'>
  <title>Telegram Bot Status</title>
  <style>
    body {{ font-family: Arial, sans-serif; padding: 2rem; max-width: 600px; margin: auto; }}
    .badge {{ color: {color}; font-weight: bold; }}
    .controls {{ display: flex; gap: .75rem; margin-top: 1rem; flex-wrap: wrap; }}
    button {{ padding: .5rem 1.2rem; cursor: pointer; border: 1px solid #ccc; border-radius: 4px; font-size: .95rem; }}
    button:disabled {{ opacity: .45; cursor: not-allowed; }}
    .btn-start    {{ background: #d4edda; }}
    .btn-shutdown {{ background: #f8d7da; }}
  </style>
</head>
<body>
  <h1>🤖 Telegram Bot</h1>
  <p>Status: <span class='badge'>{status}</span></p>
  <p>Uptime: {botService.Uptime:g}</p>
  <p>Messages processed: {botService.MessagesProcessed}</p>
  <p>
    <a href='/health'>Health</a> ·
    <a href='/status'>Status JSON</a> ·
    <a href='/ready'>Ready</a> ·
    <a href='/live'>Live</a> ·
    <a href='/check'>Check</a>
  </p>
  <div class='controls'>
    <button class='btn-start'
            {(isRunning || botService.IsShuttingDown ? "disabled" : "")}
            onclick=""fetch('/start',{{method:'POST'}}).then(r=>r.text()).then(t=>{{alert(t);location.reload();}})"">
      Start Bot
    </button>
    <button class='btn-shutdown'
            {(!isRunning || botService.IsShuttingDown ? "disabled" : "")}
            onclick=""fetch('/shutdown',{{method:'POST'}}).then(r=>r.text()).then(t=>{{alert(t);location.reload();}})"">
      Stop Bot
    </button>
  </div>
</body>
</html>");
        });

        var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
        app.Urls.Add($"http://0.0.0.0:{port}");

        Console.WriteLine($"Web server listening on port {port}");
        await app.RunAsync();
    }
}
