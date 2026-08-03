namespace DailyOpsBot.Configuration;

/// <summary>Strongly-typed application settings bound from appsettings.json.</summary>
public sealed class AppSettings
{
    public BinanceOptions Binance { get; set; } = new();
    public SalesOptions Sales { get; set; } = new();
    public ReportOptions Reports { get; set; } = new();
    public EmailOptions Email { get; set; } = new();
    public SchedulerOptions Scheduler { get; set; } = new();
}

public sealed class BinanceOptions
{
    public string BaseUrl { get; set; } = "https://api.binance.com";
    public int TopSymbols { get; set; } = 10;
    public decimal PriceChangeThresholdPercent { get; set; } = 5m;
    public int RequestTimeoutSeconds { get; set; } = 30;
}

public sealed class SalesOptions
{
    public string IncomingFolder { get; set; } = "data/incoming";
    public decimal RevenueDropThresholdPercent { get; set; } = 20m;
    public bool DetectDuplicates { get; set; } = true;
}

public sealed class ReportOptions
{
    public string OutputFolder { get; set; } = "data/output";
}

public sealed class EmailOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = "dailyops-bot@localhost";
    public string To { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;

    /// <summary>When no credentials are configured the bot runs in demo mode and only logs the email.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(To);
}

public sealed class SchedulerOptions
{
    /// <summary>Quartz cron expression. Default: every day at 07:30.</summary>
    public string CronExpression { get; set; } = "0 30 7 * * ?";
}
