# Constraints

## Security and operating boundary

- Use the official `Alpaca.Markets` SDK for typed, explicitly scoped operations; do not add an unrestricted HTTP client.
- Read API credentials only from `ALPACA_API_KEY` and `ALPACA_API_SECRET`. Never prompt for them, persist them, commit them, or include their values in output, diagnostics, logs, or exceptions.
- Require HTTPS for any custom market-data endpoint. Never silently substitute an endpoint, feed, or provider.
- The optional `--feed` is an invocation-only IEX/SIP/delayed-SIP override; never mutate environment variables or persistent settings. Credentials have no command-line form. Delayed SIP requires explicit schema 1.1 under [ADR-006](decisions/ADR-006-delayed-sip-schema-1-1.md); home adoption and live entitlement tests are excluded.
- Use only read operations necessary for market data and session context. Do not expose trading, account, portfolio, funds, margin, or options operations.
- Tests, fixtures, and CI use synthetic data and mocked provider interactions, never real credentials.

## Data integrity

- A quote price is a provider-backed latest eligible stock trade, never an unlabeled quote side, midpoint, or bar close.
- A previous close comes from the regular session immediately preceding that observation's trading date. If the date or exact reference bar is unavailable, dependent calculations are null.
- Use raw, unadjusted daily bars consistently; do not silently compare adjusted and unadjusted values. Corporate actions can cause discontinuities.
- Record provider observation time separately from retrieval time and identify the selected feed in every quote response.
- Staleness, session state, and feed coverage are distinct. An old observation may be returned with an explicit stale indication; it is not represented as a current regular-session price.
- Market-session state is `unknown` whenever the provider's authoritative clock/calendar data cannot establish it reliably.

## Delivery

- Target .NET 10 and support Windows, Linux, and macOS through .NET tool packaging.
- `quote` defaults to JSON. In JSON mode stdout contains only one JSON document; diagnostics go to stderr.
- There is no persistent historical storage in Phase 1. Future phases require separate approval.
