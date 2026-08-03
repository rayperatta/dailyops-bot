using System.Diagnostics;
using DailyOpsBot.Models;
using DailyOpsBot.Services.Reporting;
using Microsoft.Extensions.Logging;

namespace DailyOpsBot.Services;

/// <summary>Builds the aggregated <see cref="SalesSummary"/> from raw records.</summary>
public static class SalesAggregator
{
    public static SalesSummary Summarize(IReadOnlyList<SalesRecord> records)
    {
        var byDay = records
            .GroupBy(r => r.Date)
            .Select(g => new DailyRevenue(g.Key, g.Sum(r => r.Revenue), g.Sum(r => r.Units), g.Count()))
            .OrderBy(d => d.Date)
            .ToList();

        return new SalesSummary
        {
            TotalRows = records.Count,
            FileCount = records.Select(r => r.SourceFile).Distinct().Count(),
            TotalRevenue = records.Sum(r => r.Revenue),
            TotalUnits = records.Sum(r => r.Units),
            RevenueByDay = byDay,
            Records = records
        };
    }
}

/// <summary>
/// Orchestrates one full pipeline run:
/// fetch market data → load sales CSVs → aggregate → detect anomalies → (phase 2/3: report &amp; deliver).
/// </summary>
public sealed class DailyOpsRunner(
    IBinanceClient binanceClient,
    ISalesDataLoader salesDataLoader,
    IAnomalyDetector anomalyDetector,
    IEnumerable<IReportWriter> reportWriters,
    ILogger<DailyOpsRunner> logger)
{
    public async Task<DailyReport> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        logger.LogInformation("=== DailyOps pipeline started at {Time:u} ===", DateTime.UtcNow);

        var report = new DailyReport();

        try
        {
            report.TopTickers = await binanceClient.GetTopTickersAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Market data is best-effort: offline runs should still process sales data.
            logger.LogWarning(ex, "Binance fetch failed — continuing with sales data only");
        }

        var records = await salesDataLoader.LoadAsync(cancellationToken);
        report.Sales = SalesAggregator.Summarize(records);

        report.Anomalies.AddRange(anomalyDetector.Detect(report.TopTickers, report.Sales));

        foreach (var writer in reportWriters)
            report.OutputFiles.Add(writer.Write(report));

        report.DurationMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        LogSummary(report);
        logger.LogInformation("=== DailyOps pipeline finished ===");
        return report;
    }

    private void LogSummary(DailyReport report)
    {
        logger.LogInformation("--- Market snapshot (top {Count} USDT pairs by volume) ---", report.TopTickers.Count);
        foreach (var t in report.TopTickers)
            logger.LogInformation("  {Symbol,-12} {Price,14:0.########}  {Change,7:+0.00;-0.00}%  vol {Volume,16:N0}",
                t.Symbol, t.LastPrice, t.PriceChangePercent, t.QuoteVolume);

        logger.LogInformation("--- Sales summary ---");
        logger.LogInformation("  Files: {Files} | Rows: {Rows} | Units: {Units:N0} | Revenue: {Revenue:C2}",
            report.Sales.FileCount, report.Sales.TotalRows, report.Sales.TotalUnits, report.Sales.TotalRevenue);
        foreach (var day in report.Sales.RevenueByDay)
            logger.LogInformation("  {Date:yyyy-MM-dd}: {Revenue,12:C2} ({Rows} rows)", day.Date, day.Revenue, day.Rows);

        if (!report.HasAnomalies)
        {
            logger.LogInformation("--- No anomalies detected. All clear. ---");
            return;
        }

        logger.LogWarning("--- {Count} anomaly(ies) detected ---", report.Anomalies.Count);
        foreach (var a in report.Anomalies)
            logger.LogWarning("  [{Severity}] {Type}: {Description}", a.Severity, a.Type, a.Description);
    }
}
