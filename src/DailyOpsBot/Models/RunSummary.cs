namespace DailyOpsBot.Models;

/// <summary>Machine-readable JSON summary of one pipeline run (dashboard feed).</summary>
public sealed class RunSummary
{
    public DateTime RunAtUtc { get; set; }
    public long DurationMs { get; set; }
    public RunMetrics Metrics { get; set; } = new();
    public List<RunAnomaly> Anomalies { get; set; } = [];

    /// <summary>File names (not paths) of the generated Excel/PDF reports.</summary>
    public List<string> Reports { get; set; } = [];

    /// <summary>"sent", "demo" or "not-sent".</summary>
    public string EmailStatus { get; set; } = "not-sent";
}

public sealed class RunMetrics
{
    public int RowsProcessed { get; set; }
    public int FilesProcessed { get; set; }
    public int SymbolsFetched { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalUnits { get; set; }
    public int AnomalyCount { get; set; }
}

public sealed record RunAnomaly(string Type, string Severity, string Description, string? Value);
