using System.Globalization;
using System.Text;
using DailyOpsBot.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DailyOpsBot.Services;

/// <summary>
/// Generates deterministic synthetic sales CSVs in data/incoming so the bot
/// works out of the box. Two full days plus a partial third day with an
/// intentional revenue drop and a handful of duplicated rows.
/// </summary>
public sealed class SampleDataGenerator(
    IOptions<AppSettings> settings,
    ILogger<SampleDataGenerator> logger)
{
    private static readonly string[] Products =
        ["Espresso", "Latte", "Cappuccino", "Croissant", "Muffin", "Sandwich", "Salad", "Smoothie"];

    private static readonly string[] Regions = ["North", "South", "East", "West", "Central"];

    public void Generate(int rowsPerDay = 400, int days = 3)
    {
        var folder = CsvSalesDataLoader.ResolvePath(settings.Value.Sales.IncomingFolder);
        Directory.CreateDirectory(folder);

        var rng = new Random(42); // deterministic for reproducible demos
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-days));

        for (var d = 0; d < days; d++)
        {
            var date = startDate.AddDays(d);
            var isDropDay = d == days - 1; // last day simulates a revenue drop
            var path = Path.Combine(folder, $"sales_{date:yyyy-MM-dd}.csv");
            var sb = new StringBuilder("date,product,region,units,revenue\n");

            var rows = isDropDay ? rowsPerDay / 3 : rowsPerDay;
            for (var i = 0; i < rows; i++)
            {
                var units = rng.Next(1, 12);
                var price = Math.Round((decimal)(rng.NextDouble() * 8 + 2), 2);
                var revenue = Math.Round(units * price, 2);
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"{date:yyyy-MM-dd},{Products[rng.Next(Products.Length)]},{Regions[rng.Next(Regions.Length)]},{units},{revenue}"));
            }

            // Inject a few exact duplicates on the middle day to exercise the detector.
            if (d == 1 && rows > 10)
            {
                var lines = sb.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < 5; i++)
                    sb.AppendLine(lines[rng.Next(1, lines.Length)]);
            }

            File.WriteAllText(path, sb.ToString());
            logger.LogInformation("Generated sample file {File} ({Rows} rows)", path, rows);
        }
    }
}
