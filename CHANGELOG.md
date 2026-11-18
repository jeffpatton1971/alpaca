# Changelog

## 0.1.0 - Unreleased

- Add optional `--feed iex|sip` to quote and doctor with invocation-only CLI, environment, then IEX-default precedence. Preserve schema 1.0 and existing freshness/provider behavior; delayed SIP is not enabled.

- Add a read-only `market` .NET 10 tool for Alpaca US stock and ETF quotes.
- Add versioned JSON output, feed/session/freshness provenance, diagnostics, and mocked tests.
- Establish LlamaRC42 v0.1 project requirements, architecture, decisions, and verification criteria.
- Fix SDK retries, previous-close pagination, partial quotes, timeout and cancellation handling, and malformed-response diagnostics. Correct Unix startup and the v1 extended-hours session strings.
- Anchor daily change to each trade's date rather than the retrieval date, including after-hours and stale observations.

No package has been published and no release has been created.
