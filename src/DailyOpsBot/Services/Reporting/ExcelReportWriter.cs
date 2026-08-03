using ClosedXML.Excel;
using DailyOpsBot.Configuration;
using DailyOpsBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DailyOpsBot.Services.Reporting;

public interface IReportWriter
{
    /// <summary>Writes the report to disk and returns the full file path.</summary>
    string Write(DailyReport report);
}

/// <summary>
/// Builds a formatted Excel workbook with three sheets:
/// Summary (key metrics), Anomalies and Raw Data (sales rows + tickers).
/// </summary>
public sealed class ExcelReportWriter(
    IOptions<AppSettings> settings,
    ILogger<ExcelReportWriter> logger) : IReportWriter
{
    public string Write(DailyReport report)
    {
        var folder = CsvSalesDataLoader.ResolvePath(settings.Value.Reports.OutputFolder);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"dailyops_{report.GeneratedAtUtc:yyyy-MM-dd_HHmmss}.xlsx");

        using var workbook = new XLWorkbook();
        BuildSummarySheet(workbook, report);
        BuildAnomaliesSheet(workbook, report);
        BuildRawDataSheet(workbook, report);
        workbook.SaveAs(path);

        logger.LogInformation("Excel report written to {Path}", path);
        return path;
    }

    private static void BuildSummarySheet(XLWorkbook wb, DailyReport report)
    {
        var ws = wb.Worksheets.Add("Summary");

        ws.Cell(1, 1).Value = "DailyOps Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 18;
        ws.Cell(2, 1).Value = $"Generated (UTC): {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss}";
        ws.Cell(3, 1).Value = $"Anomalies detected: {report.Anomalies.Count}";

        var row = 5;
        ws.Cell(row, 1).Value = "Key metrics";
        ws.Cell(row, 1).Style.Font.Bold = true;
        row++;
        foreach (var (label, value) in new (string, object)[]
        {
            ("Sales files processed", report.Sales.FileCount),
            ("Sales rows", report.Sales.TotalRows),
            ("Units sold", report.Sales.TotalUnits),
            ("Total revenue", report.Sales.TotalRevenue),
            ("Duplicate rows", report.Sales.DuplicateRowCount),
            ("Tickers tracked", report.TopTickers.Count)
        })
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 2).Value = value switch
            {
                decimal d => d,
                int i => i,
                _ => value.ToString()
            };
            if (value is decimal) ws.Cell(row, 2).Style.NumberFormat.Format = "$#,##0.00";
            row++;
        }

        row += 2;
        ws.Cell(row, 1).Value = "Revenue by day";
        ws.Cell(row, 1).Style.Font.Bold = true;
        row++;
        AddHeader(ws, row++, "Date", "Revenue", "Units", "Rows");
        foreach (var day in report.Sales.RevenueByDay)
        {
            ws.Cell(row, 1).Value = day.Date.ToString("yyyy-MM-dd");
            ws.Cell(row, 2).Value = day.Revenue;
            ws.Cell(row, 2).Style.NumberFormat.Format = "$#,##0.00";
            ws.Cell(row, 3).Value = day.Units;
            ws.Cell(row, 4).Value = day.Rows;
            row++;
        }

        row += 2;
        ws.Cell(row, 1).Value = $"Top {report.TopTickers.Count} USDT pairs by volume";
        ws.Cell(row, 1).Style.Font.Bold = true;
        row++;
        AddHeader(ws, row++, "Symbol", "Last price", "24h change %", "Quote volume");
        foreach (var t in report.TopTickers)
        {
            ws.Cell(row, 1).Value = t.Symbol;
            ws.Cell(row, 2).Value = t.LastPrice;
            ws.Cell(row, 3).Value = t.PriceChangePercent;
            ws.Cell(row, 3).Style.NumberFormat.Format = "+0.00;-0.00";
            ws.Cell(row, 4).Value = t.QuoteVolume;
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
            row++;
        }

        ws.Columns().AdjustToContents();
    }

    private static void BuildAnomaliesSheet(XLWorkbook wb, DailyReport report)
    {
        var ws = wb.Worksheets.Add("Anomalies");
        AddHeader(ws, 1, "Type", "Severity", "Description", "Detected at (UTC)");

        var row = 2;
        foreach (var a in report.Anomalies)
        {
            ws.Cell(row, 1).Value = a.Type.ToString();
            ws.Cell(row, 2).Value = a.Severity.ToString();
            ws.Cell(row, 3).Value = a.Description;
            ws.Cell(row, 4).Value = a.DetectedAtUtc.ToString("yyyy-MM-dd HH:mm:ss");

            var fill = a.Severity switch
            {
                AnomalySeverity.Critical => XLColor.FromHtml("#F8CBAD"),
                AnomalySeverity.Warning => XLColor.FromHtml("#FFE699"),
                _ => XLColor.FromHtml("#C6EFCE")
            };
            ws.Range(row, 1, row, 4).Style.Fill.BackgroundColor = fill;
            row++;
        }

        if (row > 2)
            ws.Range(1, 1, row - 1, 4).SetAutoFilter();
        ws.Columns().AdjustToContents();
    }

    private static void BuildRawDataSheet(XLWorkbook wb, DailyReport report)
    {
        var ws = wb.Worksheets.Add("Raw Data");
        AddHeader(ws, 1, "Date", "Product", "Region", "Units", "Revenue", "Source file");

        var row = 2;
        foreach (var r in report.Sales.Records.OrderBy(r => r.Date).ThenBy(r => r.Product))
        {
            ws.Cell(row, 1).Value = r.Date.ToString("yyyy-MM-dd");
            ws.Cell(row, 2).Value = r.Product;
            ws.Cell(row, 3).Value = r.Region;
            ws.Cell(row, 4).Value = r.Units;
            ws.Cell(row, 5).Value = r.Revenue;
            ws.Cell(row, 5).Style.NumberFormat.Format = "$#,##0.00";
            ws.Cell(row, 6).Value = r.SourceFile;
            row++;
        }

        if (row > 2)
            ws.Range(1, 1, row - 1, 6).SetAutoFilter();
        ws.Columns().AdjustToContents();
    }

    private static void AddHeader(IXLWorksheet ws, int row, params string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(row, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E79");
            cell.Style.Font.FontColor = XLColor.White;
        }
    }
}
