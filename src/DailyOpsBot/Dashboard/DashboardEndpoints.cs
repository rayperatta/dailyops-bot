using System.Text.Json;
using System.Text.Json.Nodes;
using DailyOpsBot.Configuration;
using DailyOpsBot.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DailyOpsBot.Dashboard;

/// <summary>
/// Minimal-API endpoints backing the web dashboard. All data is read straight
/// from the JSON run summaries and report files in the configured output folder.
/// </summary>
public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<IOptions<AppSettings>>().Value;
        var outputFolder = Path.GetFullPath(CsvSalesDataLoader.ResolvePath(settings.Reports.OutputFolder));

        app.MapGet("/api/latest", () =>
        {
            var path = Path.Combine(outputFolder, "latest.json");
            return File.Exists(path)
                ? Results.Text(File.ReadAllText(path), "application/json")
                : Results.NotFound(new { error = "No pipeline runs recorded yet." });
        });

        app.MapGet("/api/runs", () =>
        {
            var runs = new List<JsonNode>();
            if (!Directory.Exists(outputFolder))
                return Results.Text("[]", "application/json");

            foreach (var file in Directory.EnumerateFiles(outputFolder, "run-*.json")
                         .OrderByDescending(f => f, StringComparer.Ordinal))
            {
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(file));
                    if (node is not null) runs.Add(node);
                }
                catch (JsonException)
                {
                    // Skip malformed summaries instead of failing the whole list.
                }
            }

            return Results.Text(JsonSerializer.Serialize(runs), "application/json");
        });

        app.MapGet("/api/reports/{filename}", (string filename) =>
        {
            // Path traversal guard: reject anything that is not a bare file name.
            if (string.IsNullOrWhiteSpace(filename) ||
                filename != Path.GetFileName(filename))
                return Results.BadRequest(new { error = "Invalid file name." });

            var extension = Path.GetExtension(filename).ToLowerInvariant();
            var contentType = extension switch
            {
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".pdf" => "application/pdf",
                _ => null
            };
            if (contentType is null)
                return Results.BadRequest(new { error = "Only Excel and PDF reports can be downloaded." });

            var fullPath = Path.GetFullPath(Path.Combine(outputFolder, filename));
            if (!fullPath.StartsWith(outputFolder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Invalid file name." });

            return File.Exists(fullPath)
                ? Results.File(fullPath, contentType, fileDownloadName: filename)
                : Results.NotFound(new { error = "Report not found." });
        });
    }
}
