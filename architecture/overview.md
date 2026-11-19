# Architecture Overview

## Runtime shape

`market` is a standalone .NET 10 command-line application. Its small internal boundaries are:

- CLI parsing and deterministic exit-code selection;
- invocation-scoped feed precedence, environment credentials, and endpoint validation;
- a read-only market-data provider adapter over `Alpaca.Markets`;
- symbol normalization and validation;
- quote assembly, decimal calculations, freshness and session presentation;
- versioned `System.Text.Json` output and sanitized errors.

The CLI batches symbols where SDK operations allow and preserves the user's normalized symbol order. Provider calls are asynchronous, cancellable, and bounded by a timeout. No database or background service is part of the application.

Quote and doctor share feed-option and schema-option extraction and an explicit IEX/SIP/delayed-SIP allowlist. CLI parsing removes option/value tokens before symbol validation. Configuration receives an optional validated feed override and resolves it ahead of the environment and IEX default, without writing configuration. Invalid CLI selections and delayed selection without schema 1.1 fail before provider creation. [ADR-005](../decisions/ADR-005-invocation-feed-selection.md) defines the Stage A interface, extended by [ADR-006](../decisions/ADR-006-delayed-sip-schema-1-1.md). One canonical feed-name mapping supplies JSON provenance and diagnostics.

## Provider operations

The installed `Alpaca.Markets` 8.0.1 SDK exposes batched `ListLatestTradesAsync` and `ListHistoricalBarsAsync` operations. IEX/SIP selections use the same feed for both. Delayed selection sends `MarketDataFeed.DelayedSip` on latest requests and `MarketDataFeed.Sip` on historical bars, with no fallback. It groups symbols by their reference session date, pages that date's bars using `Adjustment.Nothing` (the SDK's raw/no-adjustment value), and does not substitute an older bar when that session's bar is missing. Delayed historical windows newer than the provider clock minus 15 minutes are not requested; comparisons remain unavailable.

The adapter uses the SDK's `IAlpacaTradingClient.GetClockAsync` and `ListIntervalCalendarAsync` read operations with `Environments.Live`. Clock and calendar classify the top-level exchange session around retrieval. Separately, the calendar's premarket, regular, and after-hours windows associate each trade with its Eastern trading date, and the calendar's preceding trading date supplies the reference. A custom market-data origin is applied through the SDK's `IEnvironment.AlpacaDataApi` property. No paper-trading endpoint or generic HTTP client is used.

## Price interpretation

The price is the latest eligible trade and carries its provider timestamp and `last_trade` type. `previousClose` is relative to that trade's session date, not the retrieval date. `change` and `changePercent` use decimal arithmetic. Missing or zero previous close makes dependent values null.

The response carries UTC retrieval and observation times, America/Chicago observation time, market-session classification, feed provenance, observation age, and stale state. Feed coverage and observation freshness are not interchangeable.

Quote assembly computes excess age from full-precision timestamps and nominal feed delay. Schema-aware JSON serialization omits all four additional 1.1 fields for 1.0, while retaining null fields in 1.1. This preserves the existing raw-age semantics and default output. Doctor's schema 1.1 metadata describes configured feed policy; only an explicit connection check can establish latest-trade access.
