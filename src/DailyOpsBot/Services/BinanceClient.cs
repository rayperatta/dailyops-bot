using System.Text.Json;
using System.Text.Json.Serialization;
using DailyOpsBot.Configuration;
using DailyOpsBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DailyOpsBot.Services;

public interface IBinanceClient
{
    /// <summary>Fetches 24h tickers and returns the top N symbols by quote volume.</summary>
    Task<IReadOnlyList<TickerInfo>> GetTopTickersAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Thin client over the public Binance REST API (no API key required).
/// The HttpClient is configured with a Polly retry policy in Program.cs.
/// </summary>
public sealed class BinanceClient(
    HttpClient httpClient,
    IOptions<AppSettings> settings,
    ILogger<BinanceClient> logger) : IBinanceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<TickerInfo>> GetTopTickersAsync(CancellationToken cancellationToken = default)
    {
        var options = settings.Value.Binance;
        logger.LogInformation("Fetching 24h tickers from Binance...");

        using var response = await httpClient.GetAsync("/api/v3/ticker/24hr", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var raw = await JsonSerializer.DeserializeAsync<List<BinanceTickerDto>>(stream, JsonOptions, cancellationToken)
                  ?? [];

        var top = raw
            .Where(t => t.Symbol.EndsWith("USDT", StringComparison.Ordinal))
            .Select(t => new TickerInfo
            {
                Symbol = t.Symbol,
                LastPrice = ParseDecimal(t.LastPrice),
                PriceChangePercent = ParseDecimal(t.PriceChangePercent),
                QuoteVolume = ParseDecimal(t.QuoteVolume),
                HighPrice = ParseDecimal(t.HighPrice),
                LowPrice = ParseDecimal(t.LowPrice)
            })
            .OrderByDescending(t => t.QuoteVolume)
            .Take(options.TopSymbols)
            .ToList();

        logger.LogInformation("Fetched {Count} top USDT pairs by quote volume", top.Count);
        return top;
    }

    private static decimal ParseDecimal(string? value) =>
        decimal.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;

    private sealed class BinanceTickerDto
    {
        [JsonPropertyName("symbol")] public string Symbol { get; set; } = string.Empty;
        [JsonPropertyName("lastPrice")] public string? LastPrice { get; set; }
        [JsonPropertyName("priceChangePercent")] public string? PriceChangePercent { get; set; }
        [JsonPropertyName("quoteVolume")] public string? QuoteVolume { get; set; }
        [JsonPropertyName("highPrice")] public string? HighPrice { get; set; }
        [JsonPropertyName("lowPrice")] public string? LowPrice { get; set; }
    }
}
