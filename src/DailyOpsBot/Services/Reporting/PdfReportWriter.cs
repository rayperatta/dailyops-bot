using DailyOpsBot.Configuration;
using DailyOpsBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DailyOpsBot.Services.Reporting;

/// <summary>
/// Generates a one-page executive PDF summary with key metrics,
/// revenue-by-day and the anomaly list. QuestPDF Community license.
/// </summary>
public sealed class PdfReportWriter(
    IOptions<AppSettings> settings,
    ILogger<PdfReportWriter> logger) : IReportWriter
{
    static PdfReportWriter() => QuestPDF.Settings.License = LicenseType.Community;

    public string Write(DailyReport report)
    {
        var folder = CsvSalesDataLoader.ResolvePath(settings.Value.Reports.OutputFolder);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"dailyops_summary_{report.GeneratedAtUtc:yyyy-MM-dd_HHmmss}.pdf");

        Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Column(col =>
            {
                col.Item().Text("DailyOps — Executive Summary")
                    .FontSize(20).Bold().FontColor(Colors.Blue.Darken3);
                col.Item().Text($"Generated {report.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC")
                    .FontColor(Colors.Grey.Darken1);
                col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            });

            page.Content().PaddingTop(10).Column(col =>
            {
                col.Spacing(12);

                // Key metrics table.
                col.Item().Text("Key metrics").FontSize(13).Bold();
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.RelativeColumn();
                    });

                    void Row(string label, string value)
                    {
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                            .Text(label).Bold();
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4)
                            .Text(value);
                    }

                    Row("Sales rows processed", $"{report.Sales.TotalRows:N0} ({report.Sales.FileCount} files)");
                    Row("Total revenue", $"{report.Sales.TotalRevenue:C2}");
                    Row("Units sold", $"{report.Sales.TotalUnits:N0}");
                    Row("Duplicate rows", $"{report.Sales.DuplicateRowCount}");
                    Row("Tickers tracked", $"{report.TopTickers.Count}");
                    Row("Anomalies", report.HasAnomalies
                        ? $"{report.Anomalies.Count} detected"
                        : "None — all clear");
                });

                // Revenue by day.
                if (report.Sales.RevenueByDay.Count > 0)
                {
                    col.Item().Text("Revenue by day").FontSize(13).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });
                        table.Header(h =>
                        {
                            h.Cell().Element(HeaderStyle).Text("Date");
                            h.Cell().Element(HeaderStyle).Text("Revenue");
                            h.Cell().Element(HeaderStyle).Text("Units");
                        });
                        foreach (var d in report.Sales.RevenueByDay)
                        {
                            table.Cell().Padding(3).Text(d.Date.ToString("yyyy-MM-dd"));
                            table.Cell().Padding(3).Text($"{d.Revenue:C2}");
                            table.Cell().Padding(3).Text($"{d.Units:N0}");
                        }
                    });
                }

                // Anomaly list.
                col.Item().Text(report.HasAnomalies ? "Anomalies" : "Anomalies — none detected")
                    .FontSize(13).Bold();
                if (report.HasAnomalies)
                {
                    foreach (var a in report.Anomalies)
                    {
                        var color = a.Severity == AnomalySeverity.Critical
                            ? Colors.Red.Darken2
                            : Colors.Orange.Darken2;
                        col.Item().Row(r =>
                        {
                            r.AutoItem().Width(70).Text($"[{a.Severity}]").Bold().FontColor(color);
                            r.RelativeItem().Text(a.Description);
                        });
                    }
                }
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("DailyOps Bot · automated operations report · page ");
                t.CurrentPageNumber();
            });
        })).GeneratePdf(path);

        logger.LogInformation("PDF report written to {Path}", path);
        return path;
    }

    private static IContainer HeaderStyle(IContainer container) =>
        container.Background(Colors.Blue.Darken3).Padding(4)
            .DefaultTextStyle(x => x.FontColor(Colors.White).Bold());
}
