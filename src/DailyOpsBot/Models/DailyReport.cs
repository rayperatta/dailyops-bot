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

    public bool HasAnomalies => Anomalies.Count > 0;
}
