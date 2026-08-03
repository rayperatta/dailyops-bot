using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using DailyOpsBot.Configuration;
using DailyOpsBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DailyOpsBot.Services;

public interface ISalesDataLoader
{
    /// <summary>Loads every *.csv file in the configured incoming folder.</summary>
    Task<IReadOnlyList<SalesRecord>> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads sales CSV files (date, product, region, units, revenue) from disk.</summary>
public sealed class CsvSalesDataLoader(
    IOptions<AppSettings> settings,
    ILogger<CsvSalesDataLoader> logger) : ISalesDataLoader
{
    public Task<IReadOnlyList<SalesRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var folder = ResolvePath(settings.Value.Sales.IncomingFolder);
        var records = new List<SalesRecord>();

        if (!Directory.Exists(folder))
        {
            logger.LogWarning("Incoming folder {Folder} does not exist — no sales data loaded", folder);
            return Task.FromResult<IReadOnlyList<SalesRecord>>(records);
        }

        var files = Directory.GetFiles(folder, "*.csv");
        logger.LogInformation("Loading {Count} CSV file(s) from {Folder}", files.Length, folder);

        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            HeaderValidated = null,
            PrepareHeaderForMatch = args => args.Header.ToLowerInvariant(),
            BadDataFound = ctx => logger.LogWarning("Bad CSV row skipped: {Raw}", ctx.RawRecord)
        };

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);
            using var reader = new StreamReader(file);
            using var csv = new CsvReader(reader, csvConfig);

            var count = 0;
            foreach (var row in csv.GetRecords<SalesCsvRow>())
            {
                if (!DateOnly.TryParse(row.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    logger.LogWarning("Skipping row with invalid date '{Date}' in {File}", row.Date, fileName);
                    continue;
                }

                records.Add(new SalesRecord
                {
                    Date = date,
                    Product = row.Product ?? "UNKNOWN",
                    Region = row.Region ?? "UNKNOWN",
                    Units = row.Units,
                    Revenue = row.Revenue,
                    SourceFile = fileName
                });
                count++;
            }

            logger.LogInformation("Loaded {Count} rows from {File}", count, fileName);
        }

        logger.LogInformation("Total sales rows loaded: {Count}", records.Count);
        return Task.FromResult<IReadOnlyList<SalesRecord>>(records);
    }

    internal static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(path, AppContext.BaseDirectory);

    private sealed class SalesCsvRow
    {
        public string? Date { get; set; }
        public string? Product { get; set; }
        public string? Region { get; set; }
        public int Units { get; set; }
        public decimal Revenue { get; set; }
    }
}
