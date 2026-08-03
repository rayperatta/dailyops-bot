namespace DailyOpsBot.Models;

/// <summary>Aggregated view of the sales batch used by the detectors and reports.</summary>
public sealed class SalesSummary
{
    public int TotalRows { get; set; }
    public int FileCount { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalUnits { get; set; }
    public int DuplicateRowCount { get; set; }
    public IReadOnlyList<DailyRevenue> RevenueByDay { get; set; } = [];
    public IReadOnlyList<SalesRecord> Records { get; set; } = [];
}

public sealed record DailyRevenue(DateOnly Date, decimal Revenue, int Units, int Rows);

/// <summary>Full result of one DailyOps pipeline run.</summary>
public sealed class DailyReport
{
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    public IReadOnlyList<TickerInfo> TopTickers { get; set; } = [];
    public SalesSummary Sales { get; set; } = new();
    public List<Anomaly> Anomalies { get; } = [];

    /// <summary>Paths of generated report files (Excel, PDF) — used as email attachments.</summary>
    public List<string> OutputFiles { get; } = [];

    /// <summary>Wall-clock duration of the pipeline run, set by <see cref="DailyOpsBot.Services.DailyOpsRunner"/>.</summary>
    public long DurationMs { get; set; }

    /// <summary>Email delivery outcome: "sent", "demo" or "not-sent" (set by the email sender).</summary>
    public string EmailStatus { get; set; } = "not-sent";

    public bool HasAnomalies => Anomalies.Count > 0;
}
