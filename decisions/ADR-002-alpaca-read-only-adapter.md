# ADR-002: Alpaca Read-Only Provider Adapter

## Status

Accepted

## Context

Market observations must come from an authorized provider feed and retain clear provenance. The Alpaca SDK also exposes capabilities outside the approved use case.

## Decision

Use the official `Alpaca.Markets` NuGet SDK behind a narrow, read-only adapter for stock trades, raw daily bars, and only the live clock/calendar reads needed for session context. Select IEX or SIP explicitly on both data requests and never fall back silently. Apply a validated custom data origin via the SDK's `IEnvironment` configuration, not a generic HTTP client.

## Consequences

Stage B disposition: [ADR-006](ADR-006-delayed-sip-schema-1-1.md) extends the feed rule for delayed latest data with explicit historical SIP. The original IEX/SIP behavior and read-only/no-fallback constraints remain in effect.

Feed availability depends on the user's Alpaca subscription and permissions. IEX has venue-limited coverage; SIP is consolidated only when authorized. Session or prior-close data that cannot be established is reported as unknown or null rather than guessed. The implemented calls were checked against the installed `Alpaca.Markets` 8.0.1 package; the only trading-client operations used are `GetClockAsync` and `ListIntervalCalendarAsync`, against `Environments.Live`.
