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
    public DateTime DetectedAtUtc { get; init; } = DateTime.UtcNow;
}
