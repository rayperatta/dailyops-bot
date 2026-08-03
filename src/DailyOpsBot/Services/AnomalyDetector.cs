using DailyOpsBot.Configuration;
using DailyOpsBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DailyOpsBot.Services;

public interface IAnomalyDetector
{
    /// <summary>Runs all detectors and returns the found anomalies (possibly empty).</summary>
    IReadOnlyList<Anomaly> Detect(IReadOnlyList<TickerInfo> tickers, SalesSummary sales);
}

/// <summary>
/// Rule-based anomaly detection engine:
///  - crypto price moves beyond the configured threshold
///  - duplicate sales rows (same date/product/region/units/revenue)
///  - day-over-day revenue drops beyond the configured threshold
/// </summary>
public sealed class AnomalyDetector(
    IOptions<AppSettings> settings,
    ILogger<AnomalyDetector> logger) : IAnomalyDetector
{
    public IReadOnlyList<Anomaly> Detect(IReadOnlyList<TickerInfo> tickers, SalesSummary sales)
    {
        var anomalies = new List<Anomaly>();
        anomalies.AddRange(DetectPriceMoves(tickers));
        if (settings.Value.Sales.DetectDuplicates)
            anomalies.AddRange(DetectDuplicates(sales));
        anomalies.AddRange(DetectRevenueDrops(sales));

        logger.LogInformation("Anomaly detection finished: {Count} anomaly(ies) found", anomalies.Count);
        return anomalies;
    }

    private IEnumerable<Anomaly> DetectPriceMoves(IReadOnlyList<TickerInfo> tickers)
    {
        var threshold = settings.Value.Binance.PriceChangeThresholdPercent;
        foreach (var t in tickers)
        {
            if (Math.Abs(t.PriceChangePercent) < threshold) continue;

            var isSpike = t.PriceChangePercent > 0;
            yield return new Anomaly
            {
                Type = isSpike ? AnomalyType.PriceSpike : AnomalyType.PriceCrash,
                Severity = Math.Abs(t.PriceChangePercent) >= threshold * 2
                    ? AnomalySeverity.Critical
                    : AnomalySeverity.Warning,
                Description = $"{t.Symbol} moved {t.PriceChangePercent:+0.00;-0.00}% in 24h " +
                              $"(threshold {threshold}%), last price {t.LastPrice:0.########}",
                Value = $"{t.PriceChangePercent:+0.00;-0.00}%"
            };
        }
    }

    private IEnumerable<Anomaly> DetectDuplicates(SalesSummary sales)
    {
        var duplicates = sales.Records
            .GroupBy(r => r.Fingerprint)
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicates.Count == 0) yield break;

        var extraRows = duplicates.Sum(g => g.Count() - 1);
        sales.DuplicateRowCount = extraRows;

        yield return new Anomaly
        {
            Type = AnomalyType.DuplicateSalesRows,
            Severity = extraRows > 10 ? AnomalySeverity.Critical : AnomalySeverity.Warning,
            Description = $"{duplicates.Count} duplicate group(s) found ({extraRows} redundant row(s)), " +
                          $"e.g. {duplicates[0].Key}",
            Value = $"{extraRows} rows"
        };
    }

    private IEnumerable<Anomaly> DetectRevenueDrops(SalesSummary sales)
    {
        var threshold = settings.Value.Sales.RevenueDropThresholdPercent;
        var days = sales.RevenueByDay.OrderBy(d => d.Date).ToList();

        for (var i = 1; i < days.Count; i++)
        {
            var previous = days[i - 1];
            var current = days[i];
            if (previous.Revenue <= 0) continue;

            var changePercent = (current.Revenue - previous.Revenue) / previous.Revenue * 100m;
            if (changePercent >= -threshold) continue;

            yield return new Anomaly
            {
                Type = AnomalyType.RevenueDrop,
                Severity = changePercent <= -threshold * 2 ? AnomalySeverity.Critical : AnomalySeverity.Warning,
                Description = $"Revenue dropped {changePercent:0.0}% on {current.Date:yyyy-MM-dd} " +
                              $"({previous.Revenue:C0} → {current.Revenue:C0}, threshold {threshold}%)",
                Value = $"{changePercent:0.0}%"
            };
        }
    }
}
