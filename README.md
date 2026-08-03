# DailyOps Bot

An operations-report automation tool built with **.NET 8**. Every morning it fetches
market data from a public API, processes a folder of sales CSVs, detects anomalies,
generates an Excel report + executive PDF, and emails it — hands-free.

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
4. **Delivers** the report by email (MailKit/SMTP) on a daily cron schedule (Quartz.NET).
   Without SMTP credentials it runs in **demo mode** and logs the email instead.
5. **Visualizes** every run in a minimalist, macOS-style **web dashboard**
   (ASP.NET Core minimal APIs + vanilla HTML/CSS/JS, no build step).

## Architecture

```
┌────────────────────────────────────────────────────────────┐
│                     Program.cs (Host + DI)                 │
│   Microsoft.Extensions.Hosting · Serilog · Polly · Quartz  │
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
                            │  DailyReport    │──► Excel (ClosedXML)
                            │  (domain model) │──► PDF (QuestPDF)
                            └─────────────────┘──► Email (MailKit) / demo mode
                                     ▲
                            ┌────────┴────────┐
                            │  Quartz DailyOpsJob  (cron 07:30)
                            └─────────────────┘
```

## Phase roadmap

- [x] **Phase 1 — Core:** data ingestion (Binance + CSV), anomaly detection engine, Serilog logging.
- [x] **Phase 2 — Reporting:** Excel workbook (ClosedXML) + executive PDF (QuestPDF).
- [x] **Phase 3 — Delivery:** email delivery (MailKit, demo mode without credentials) + Quartz.NET daily scheduler.
- [x] **Phase 4 — Web dashboard:** macOS-style dashboard (Kestrel + minimal APIs), JSON run summaries.

## How to run

Requires the **.NET 8 SDK**. No API keys needed — everything works out of the box.

```bash
git clone https://github.com/rayperatta/dailyops-bot.git
cd dailyops-bot
dotnet build

# (Optional) regenerate the synthetic sample data in data/incoming/
dotnet run --project src/DailyOpsBot -- --generate-data

# Run the full pipeline once (analysis + reports + email/demo mode), then exit
dotnet run --project src/DailyOpsBot -- --now

# Start the web dashboard on http://localhost:5080 (scheduler stays active)
dotnet run --project src/DailyOpsBot -- --serve

# Run as a long-lived service: executes the pipeline daily at 07:30 (configurable cron)
dotnet run --project src/DailyOpsBot
```

## Web dashboard

`--serve` starts a Kestrel-hosted dashboard on **http://localhost:5080**
(port configurable via `DailyOps:Dashboard:Port` in `appsettings.json`).

> **Screenshot:** _placeholder — drop a capture of the dashboard here (e.g. `docs/dashboard.png`)._

Every pipeline run also writes a machine-readable summary to `data/output/`:

- `run-YYYYMMDD-HHmmss.json` — one file per run (timestamp, duration, metrics,
  anomalies with severity, report file names, email status).
- `latest.json` — always points to the most recent run; this is what the dashboard reads.

The dashboard itself is dependency-free: light macOS-inspired theme (frosted-glass
top bar, traffic-light window chrome, soft cards), KPI row, anomaly list with
severity badges, one-click Excel/PDF downloads and a compact run-history table.
It auto-refreshes every 30 seconds against these endpoints:

| Endpoint | Description |
|---|---|
| `GET /` | The dashboard (static HTML/CSS/JS from `wwwroot/`). |
| `GET /api/latest` | Contents of `latest.json` (404 before the first run). |
| `GET /api/runs` | All recorded run summaries, newest first. |
| `GET /api/reports/{filename}` | Downloads a generated Excel/PDF (path traversal rejected). |

The Quartz scheduler keeps running in `--serve` mode, so scheduled runs show up
on the dashboard automatically.

### Sample output

Each run writes two timestamped files to `data/output/`:

- `dailyops_YYYY-MM-DD_HHmmss.xlsx` — Excel workbook with three styled sheets:
  - **Summary** — key metrics, revenue by day, top crypto pairs.
  - **Anomalies** — every detected anomaly, color-coded by severity, with autofilter.
  - **Raw Data** — all ingested sales rows, with autofilter.
- `dailyops_summary_YYYY-MM-DD_HHmmss.pdf` — one-page executive summary
  (title, date, key-metrics table, revenue by day, anomaly list).
- `run-YYYYMMDD-HHmmss.json` + `latest.json` — machine-readable run summary
  consumed by the web dashboard (see above).

Console (demo mode):

```
[18:25:21 WRN] SMTP not configured — DEMO MODE: email NOT sent.
[18:25:21 INF] Subject: DailyOps Report 2026-08-03 — 2 anomaly(ies) detected
[18:25:21 INF]   [Critical] RevenueDrop: Revenue dropped -67.1% on 2026-08-02 ...
[18:25:21 INF] Attachments: .../dailyops_2026-08-03_172518.xlsx, .../dailyops_summary_2026-08-03_172518.pdf
```

## Configuration

All settings live in `src/DailyOpsBot/appsettings.json` under the `DailyOps` section.

| Key | Default | Description |
|---|---|---|
| `Binance:BaseUrl` | `https://api.binance.com` | Binance REST API base URL (public, no key). |
| `Binance:TopSymbols` | `10` | Number of top USDT pairs (by quote volume) to track. |
| `Binance:PriceChangeThresholdPercent` | `5` | 24h price move (%) that triggers a spike/crash anomaly. |
| `Binance:RequestTimeoutSeconds` | `30` | HTTP timeout for the Binance call. |
| `Sales:IncomingFolder` | `data/incoming` | Folder scanned for `*.csv` sales files. |
| `Sales:RevenueDropThresholdPercent` | `20` | Day-over-day revenue drop (%) that triggers an anomaly. |
| `Sales:DetectDuplicates` | `true` | Enable duplicate-row detection. |
| `Reports:OutputFolder` | `data/output` | Destination for generated Excel/PDF reports. |
| `Email:Host` / `Port` / `User` / `Password` | *(empty)* | SMTP settings. **Empty password → demo mode.** |
| `Email:From` / `To` / `EnableSsl` | — | Sender, recipient, STARTTLS toggle. |
| `Scheduler:CronExpression` | `0 30 7 * * ?` | Quartz cron — daily at 07:30 by default. |
| `Dashboard:Port` | `5080` | Port for the `--serve` web dashboard. |

### Demo mode & secrets

The repository contains **no credentials**. If `Email:Password` (or `Host`/`To`) is
empty, the bot skips SMTP entirely and logs the full email body plus attachment paths.
To enable real delivery locally, create `src/DailyOpsBot/appsettings.Local.json`
(already in `.gitignore`) with your SMTP settings — it is never committed.

## What I'd add next

- **SAP/ERP connectors** — replace the CSV drop folder with direct OData/IDoc pulls
  from SAP, Dynamics or Odoo, behind the same `ISalesDataLoader` interface.
- **LLM-written executive summaries** — feed the `DailyReport` JSON to an LLM to
  produce a natural-language narrative for the PDF/email ("revenue fell because the
  West region stopped ordering sandwiches...").
- **Persistence** — store each run in SQLite/Postgres for trend analysis and
  week-over-week comparisons.
- **Docker + health checks** — container image and liveness endpoint for the scheduler mode.

## Tech stack

.NET 8 · Microsoft.Extensions.Hosting (DI) · ASP.NET Core minimal APIs (Kestrel) · Serilog ·
Polly (HTTP retries) · CsvHelper · ClosedXML · QuestPDF (Community license) · MailKit · Quartz.NET

## License

MIT — see below. QuestPDF is used under its free **Community license**;
commercial use of QuestPDF beyond the community tier requires a paid license from QuestPDF.

```
MIT License — Copyright (c) 2026 gozuray
Permission is hereby granted, free of charge, to any person obtaining a copy of this
software and associated documentation files (the "Software"), to deal in the Software
without restriction, including without limitation the rights to use, copy, modify,
merge, publish, distribute, sublicense, and/or sell copies of the Software.
```
