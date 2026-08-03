# DailyOps Bot

An operations-report automation tool built with **.NET 8**. Every morning it fetches
market data from a public API, processes a folder of sales CSVs, detects anomalies,
and produces an executive report — hands-free.

Think of it as the bot that replaces the analyst's first hour of the day.

## What it does

1. **Ingests** two data sources (both key-free):
   - [Binance public API](https://api.binance.com/api/v3/ticker/24hr) — top USDT pairs by 24h volume.
   - `data/incoming/*.csv` — sales files with `date,product,region,units,revenue`.
2. **Detects anomalies** with configurable thresholds (`appsettings.json`):
   - Crypto price moves beyond `PriceChangeThresholdPercent` (24h).
   - Duplicate sales rows (same date/product/region/units/revenue).
   - Day-over-day revenue drops beyond `RevenueDropThresholdPercent`.
3. **Reports** a formatted Excel workbook (Summary / Anomalies / Raw Data) plus a
   one-page executive PDF, saved to `data/output/` with timestamped filenames.
4. **Delivers** *(phase 3)* the report by email, on a daily cron schedule.

## Architecture

```
┌────────────────────────────────────────────────────────────┐
│                     Program.cs (Host + DI)                 │
│   Microsoft.Extensions.Hosting · Serilog · Polly           │
└──────────────┬─────────────────────────────┬───────────────┘
               │                             │
      ┌────────▼────────┐          ┌─────────▼─────────┐
      │  BinanceClient  │          │ CsvSalesDataLoader│
      │ (HttpClient +   │          │ (CsvHelper)       │
      │  Polly retries) │          │                   │
      └────────┬────────┘          └─────────┬─────────┘
               │            ┌────────────────▼
               └───────────►│  AnomalyDetector │
                            │ (rule-based)     │
                            └────────┬────────┘
                                     ▼
                            ┌─────────────────┐
                            │  DailyReport    │──► Excel / PDF reports
                            │  (domain model) │──► Email / Quartz (phase 3)
                            └─────────────────┘
```

## Phase roadmap

- [x] **Phase 1 — Core:** data ingestion (Binance + CSV), anomaly detection engine, Serilog logging.
- [x] **Phase 2 — Reporting:** Excel workbook (ClosedXML) + executive PDF (QuestPDF).
- [ ] **Phase 3 — Delivery:** email delivery (MailKit, demo mode without credentials) + Quartz.NET daily scheduler.

## How to run

Requires the **.NET 8 SDK**. No API keys needed — everything works out of the box.

```bash
# Clone and build
git clone https://github.com/gozuray/dailyops-bot.git
cd dailyops-bot
dotnet build

# (Optional) regenerate the synthetic sample data in data/incoming/
dotnet run --project src/DailyOpsBot -- --generate-data

# Run one analysis pass
dotnet run --project src/DailyOpsBot
```

Sample console output:

```
[18:18:50 INF] --- Sales summary ---
[18:18:50 INF]   Files: 3 | Rows: 938 | Units: 5,579 | Revenue: $32,852.85
[18:18:50 WRN] --- 2 anomaly(ies) detected ---
[18:18:50 WRN]   [Warning] DuplicateSalesRows: 5 duplicate group(s) found ...
[18:18:50 WRN]   [Critical] RevenueDrop: Revenue dropped -67.1% on 2026-08-02 ...
```

### Sample output

Each run writes two timestamped files to `data/output/`:

- `dailyops_YYYY-MM-DD_HHmmss.xlsx` — Excel workbook with three styled sheets:
  - **Summary** — key metrics, revenue by day, top crypto pairs.
  - **Anomalies** — every detected anomaly, color-coded by severity, with autofilter.
  - **Raw Data** — all ingested sales rows, with autofilter.
- `dailyops_summary_YYYY-MM-DD_HHmmss.pdf` — one-page executive summary
  (title, date, key-metrics table, revenue by day, anomaly list).

## Configuration

All settings live in `src/DailyOpsBot/appsettings.json` under the `DailyOps` section.
Thresholds, folders and symbols are fully configurable — no recompilation needed.

## Tech stack

.NET 8 · Microsoft.Extensions.Hosting (DI) · Serilog · Polly (HTTP retries) · CsvHelper · ClosedXML · QuestPDF (Community license)
