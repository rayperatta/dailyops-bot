namespace DailyOpsBot.Models;

/// <summary>24h ticker statistics for a single Binance symbol.</summary>
public sealed record TickerInfo
{
    public required string Symbol { get; init; }
    public decimal LastPrice { get; init; }
    public decimal PriceChangePercent { get; init; }
    public decimal QuoteVolume { get; init; }
    public decimal HighPrice { get; init; }
    public decimal LowPrice { get; init; }
}
