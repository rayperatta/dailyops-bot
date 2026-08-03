namespace DailyOpsBot.Models;

/// <summary>One row of sales data loaded from a CSV file in data/incoming.</summary>
public sealed record SalesRecord
{
    public required DateOnly Date { get; init; }
    public required string Product { get; init; }
    public required string Region { get; init; }
    public int Units { get; init; }
    public decimal Revenue { get; init; }

    /// <summary>Name of the CSV file this row came from (for traceability).</summary>
    public string SourceFile { get; init; } = string.Empty;

    /// <summary>Fingerprint used for duplicate detection across the whole batch.</summary>
    public string Fingerprint => $"{Date:yyyy-MM-dd}|{Product}|{Region}|{Units}|{Revenue}";
}
