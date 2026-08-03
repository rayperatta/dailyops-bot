namespace DailyOpsBot.Models;

public enum AnomalyType
{
    PriceSpike,
    PriceCrash,
    DuplicateSalesRows,
    RevenueDrop
}

public enum AnomalySeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>A single detected anomaly in market or sales data.</summary>
public sealed record Anomaly
{
    public required AnomalyType Type { get; init; }
    public required AnomalySeverity Severity { get; init; }
    public required string Description { get; init; }

    /// <summary>Short machine-friendly value for dashboards (e.g. "-67.1%", "3 rows").</summary>
    public string? Value { get; init; }

    public DateTime DetectedAtUtc { get; init; } = DateTime.UtcNow;
}
