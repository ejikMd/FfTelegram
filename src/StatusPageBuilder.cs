using System;
using System.Text;

/// <summary>
/// Builder class for the status page HTML using StringBuilder for better performance
/// </summary>
public class StatusPageBuilder
{
    private BotService _botService;
    private bool _isRunning;

    public StatusPageBuilder WithBotService(BotService botService)
    {
        _botService = botService ?? throw new ArgumentNullException(nameof(botService));
        return this;
    }

    public StatusPageBuilder WithIsRunning(bool isRunning)
    {
        _isRunning = isRunning;
        return this;
    }

    public string Build()
    {
        if (_botService == null)
            throw new InvalidOperationException("BotService must be set before building");

        var status = GetStatusText();
        var color = GetStatusColor();

        var sb = new StringBuilder(2048); // Pre-allocate reasonable capacity

        AppendHtmlHeader(sb, color);
        AppendBodyContent(sb, status);
        AppendHtmlFooter(sb);

        return sb.ToString();
    }

    private void AppendHtmlHeader(StringBuilder sb, string color)
    {
        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang='en'>\n");
        sb.Append("<head>\n");
        sb.Append("  <meta charset='utf-8'>\n");
        sb.Append("  <title>Telegram Bot Status</title>\n");
        sb.Append("  <style>\n");
        sb.Append("    body { font-family: Arial, sans-serif; padding: 2rem; max-width: 600px; margin: auto; }\n");
        sb.Append("    .badge { color: ").Append(color).Append("; font-weight: bold; }\n");
        sb.Append("    .controls { display: flex; gap: .75rem; margin-top: 1rem; flex-wrap: wrap; }\n");
        sb.Append("    button { padding: .5rem 1.2rem; cursor: pointer; border: 1px solid #ccc; border-radius: 4px; font-size: .95rem; }\n");
        sb.Append("    button:disabled { opacity: .45; cursor: not-allowed; }\n");
        sb.Append("    .btn-start { background: #d4edda; }\n");
        sb.Append("    .btn-shutdown { background: #f8d7da; }\n");
        sb.Append("  </style>\n");
        sb.Append("</head>\n");
        sb.Append("<body>\n");
    }

    private void AppendBodyContent(StringBuilder sb, string status)
    {
        // Header section
        sb.Append("  <h1>🤖 Telegram Bot</h1>\n");
        sb.Append("  <p>Status: <span class='badge'>").Append(status).Append("</span></p>\n");
        sb.Append("  <p>Uptime: ").Append(_botService.Uptime.ToString("g")).Append("</p>\n");
        sb.Append("  <p>Messages processed: ").Append(_botService.MessagesProcessed).Append("</p>\n");

        // Navigation links
        sb.Append("  <p>\n");
        sb.Append("    <a href='/health'>Health</a> ·\n");
        sb.Append("    <a href='/status'>Status JSON</a> ·\n");
        sb.Append("    <a href='/ready'>Ready</a> ·\n");
        sb.Append("    <a href='/live'>Live</a> ·\n");
        sb.Append("    <a href='/check'>Check</a>\n");
        sb.Append("  </p>\n");

        // Control buttons
        sb.Append("  <div class='controls'>\n");
        AppendStartButton(sb);
        AppendStopButton(sb);
        sb.Append("  </div>\n");

        // Sync section
        sb.Append("  <h2 style='margin-top:1.5rem'>🔄 Station Cache Sync</h2>\n");
        sb.Append("  <div class='controls'>\n");
        AppendSyncButtons(sb);
        sb.Append("  </div>\n");
    }

    private void AppendStartButton(StringBuilder sb)
    {
        var isDisabled = _isRunning || _botService.IsShuttingDown;

        sb.Append("    <button class='btn-start'");
        if (isDisabled)
            sb.Append(" disabled");
        sb.Append(" onclick=\"fetch('/start',{method:'POST'}).then(r=>r.text()).then(t=>{alert(t);location.reload();})\">\n");
        sb.Append("      Start Bot\n");
        sb.Append("    </button>\n");
    }

    private void AppendStopButton(StringBuilder sb)
    {
        var isDisabled = !_isRunning || _botService.IsShuttingDown;

        sb.Append("    <button class='btn-shutdown'");
        if (isDisabled)
            sb.Append(" disabled");
        sb.Append(" onclick=\"fetch('/shutdown',{method:'POST'}).then(r=>r.text()).then(t=>{alert(t);location.reload();})\">\n");
        sb.Append("      Stop Bot\n");
        sb.Append("    </button>\n");
    }

    private void AppendSyncButtons(StringBuilder sb)
    {
        // Sync Unknown button
        sb.Append("    <button onclick=\"fetch('/sync/unknown',{method:'POST'}).then(r=>r.json()).then(t=>{alert(JSON.stringify(t,null,2));})\">\n");
        sb.Append("      Sync Unknown\n");
        sb.Append("    </button>\n");

        // Sync All button
        sb.Append("    <button onclick=\"fetch('/sync/all',{method:'POST'}).then(r=>r.json()).then(t=>{alert(JSON.stringify(t,null,2));})\">\n");
        sb.Append("      Sync All\n");
        sb.Append("    </button>\n");
    }

    private void AppendHtmlFooter(StringBuilder sb)
    {
        sb.Append("</body>\n");
        sb.Append("</html>");
    }

    private string GetStatusText()
    {
        if (_botService.IsShuttingDown)
            return "Shutting Down";
        if (_botService.IsStopped)
            return "Stopped";
        return "Running";
    }

    private string GetStatusColor()
    {
        return _isRunning ? "green" : "red";
    }
}