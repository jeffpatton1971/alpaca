# Architecture Overview

## Runtime shape

`market` is a standalone .NET 10 command-line application. Its small internal boundaries are:

- CLI parsing and deterministic exit-code selection;
- environment configuration and endpoint validation;
- a read-only market-data provider adapter over `Alpaca.Markets`;
- symbol normalization and validation;
- quote assembly, decimal calculations, freshness and session presentation;
- versioned `System.Text.Json` output and sanitized errors.

The CLI batches symbols where SDK operations allow and preserves the user's normalized symbol order. Provider calls are asynchronous, cancellable, and bounded by a timeout. No database or background service is part of the application.

## Provider operations

The installed `Alpaca.Markets` 8.0.1 SDK exposes batched `ListLatestTradesAsync` and `ListHistoricalBarsAsync` operations. The adapter explicitly sets `MarketDataFeed.Iex` or `MarketDataFeed.Sip` for both. It requests a daily bar only for the previous completed session, using `Adjustment.Nothing` (the SDK's raw/no-adjustment value), and does not substitute an older bar when that session's bar is missing.

The adapter uses the SDK's `IAlpacaTradingClient.GetClockAsync` and `ListIntervalCalendarAsync` read operations with `Environments.Live`. The calendar's regular trading window selects the prior session; its broader session window classifies pre-market and after-hours. A custom market-data origin is applied through the SDK's `IEnvironment.AlpacaDataApi` property. No paper-trading endpoint or generic HTTP client is used.

## Price interpretation

The price is the latest eligible trade and carries its provider timestamp and `last_trade` type. `previousClose` is the close of the preceding completed regular session, using raw (unadjusted) daily bars. `change` and `changePercent` use decimal arithmetic. Missing or zero previous close makes dependent values null.

The response carries UTC retrieval and observation times, America/Chicago observation time, market-session classification, feed provenance, observation age, and stale state. Feed coverage and observation freshness are not interchangeable.
