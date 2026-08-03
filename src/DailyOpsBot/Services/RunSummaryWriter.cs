using System.Text.Json;
using DailyOpsBot.Configuration;
using DailyOpsBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DailyOpsBot.Services;

public interface IRunSummaryWriter
{
    /// <summary>
    /// Persists a machine-readable JSON summary of the run as
    /// <c>run-&lt;timestamp&gt;.json</c> and refreshes <c>latest.json</c> in the output folder.
    /// </summary>
    void Write(DailyReport report);
}

/// <summary>Serializes each pipeline run to JSON for the web dashboard.</summary>
public sealed class RunSummaryWriter(
    IOptions<AppSettings> settings,
    ILogger<RunSummaryWriter> logger) : IRunSummaryWriter
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void Write(DailyReport report)
    {
        var folder = CsvSalesDataLoader.ResolvePath(settings.Value.Reports.OutputFolder);
        Directory.CreateDirectory(folder);

        var summary = ToSummary(report);
        var json = JsonSerializer.Serialize(summary, JsonOptions);

        var runPath = Path.Combine(folder, $"run-{report.GeneratedAtUtc:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(runPath, json);

        // latest.json is the dashboard's single source of truth for the most recent run.
        File.WriteAllText(Path.Combine(folder, "latest.json"), json);

        logger.LogInformation("Run summary written to {Path}", runPath);
    }

    internal static RunSummary ToSummary(DailyReport report) => new()
    {
        RunAtUtc = report.GeneratedAtUtc,
        DurationMs = report.DurationMs,
        Metrics = new RunMetrics
        {
            RowsProcessed = report.Sales.TotalRows,
            FilesProcessed = report.Sales.FileCount,
            SymbolsFetched = report.TopTickers.Count,
            TotalRevenue = report.Sales.TotalRevenue,
            TotalUnits = report.Sales.TotalUnits,
            AnomalyCount = report.Anomalies.Count
        },
        Anomalies = report.Anomalies
            .Select(a => new RunAnomaly(a.Type.ToString(), a.Severity.ToString(), a.Description, a.Value))
            .ToList(),
        Reports = report.OutputFiles.Select(Path.GetFileName).OfType<string>().ToList(),
        EmailStatus = report.EmailStatus
    };
}
