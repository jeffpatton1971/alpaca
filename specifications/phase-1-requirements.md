# Phase 1 Requirements and JSON Contract

Status: Accepted

## 1. Scope and success criteria

The `market` command retrieves provider-backed current and recent US stock/ETF observations through Alpaca. It supports arbitrary valid symbols, batched requests, a read-only operating boundary, deterministic machine-readable output, and cross-platform .NET tool packaging.

Phase 1 succeeds when the documented behavior builds and passes credential-free automated tests, the installed command works outside the source directory, the JSON contract is stable and versioned, and failures disclose no secrets. Live data access is claimed only after a real authorized request has succeeded.

## 2. CLI behavior

Supported forms are:

```text
market quote RACK TCAI CLOU ARTY DTCR --json
market quote NVDA AMD AVGO --json
market quote RACK --json
market doctor --json
market --help
market --version
```

`quote` accepts one or more symbols, normalizes them to uppercase, removes duplicates while preserving first-seen order, validates them before requests, and uses batching where supported. JSON is its default output. `doctor` checks configuration and endpoint/feed settings without prompting; a separately selected connectivity check may make a bounded authenticated read.

Ticker syntax is one to ten ASCII letters/digits, with optional interior `.` or `-` separators (for example `BRK.B` and `3M`). Requests are limited to 1,000 distinct symbols to bound provider work and match the SDK's batched request size. Duplicate input symbols count once.

`market doctor --check-connection --json` optionally tests authenticated market-data access by requesting the latest trade for SPY on the configured feed. It reports access only; the probe price is not emitted or stored.

## 3. Authentication and endpoint

`ALPACA_API_KEY` and `ALPACA_API_SECRET` are required for authenticated data calls. `ALPACA_DATA_FEED` explicitly selects `iex` or `sip`; the default is `iex`. The response always identifies the selected feed. An unauthorized feed is a structured failure, not a reason to downgrade.

`ALPACA_DATA_URL` is optional. The SDK's documented default is used when unset. A configured endpoint must be a validated HTTPS URL and must be applied through a supported SDK configuration mechanism. Invalid or unsupported configuration fails explicitly. Credentials are never requested interactively or persisted.

## 4. Observation and calculation semantics

- `price` is the latest eligible provider stock trade. It is not a bid, ask, midpoint, or daily-bar close.
- `priceType` is `last_trade`; the actual trade timestamp is authoritative.
- `previousClose` is the close of the previous completed regular trading session, not necessarily the previous calendar date.
- Daily bars use the `raw` adjustment basis. This preserves price units consistently but does not remove split/dividend discontinuities.
- `change = price - previousClose`.
- `changePercent = (change / previousClose) * 100`.
- Calculations use decimal arithmetic. If previous close is absent or zero, both calculated values are null.
- Missing observations and prior bars are represented as null with symbol-level status and a structured error; no price or timestamp is fabricated.

The configured feed is explicit and is never silently switched. IEX reflects a limited venue and may have infrequent trades for thinly traded ETFs. SIP is consolidated only when the account's plan authorizes it. Neither feed is described as equivalent to the other.

## 5. Time, session, and freshness

All instants are UTC; the user-facing observation time is also rendered in `America/Chicago`, including daylight-saving offsets. `retrievedAtUtc` is distinct from the provider observation timestamp.

The session is one of `regular`, `pre_market`, `after_hours`, `closed`, or `unknown`. Prefer Alpaca's clock/calendar and its session schedule, including holidays and early closures. If that information cannot be established reliably, use `unknown`; request time is not a substitute for observation session.

The initial freshness threshold is 15 minutes and is configurable through `MARKET_STALE_AFTER_MINUTES`. `quoteAgeSeconds` is the elapsed UTC duration from the provider trade timestamp to retrieval time, truncated to whole seconds; `isStale` is true only when the exact elapsed duration exceeds the configured threshold. A provider observation timestamp later than retrieval is rejected. Staleness says nothing by itself about whether a feed is consolidated or a market is open. An out-of-session observation may be returned with its timestamp and freshness state.

## 6. Version 1 JSON response

`schemaVersion` is `"1.0"`. Property names and meanings are stable within this version; breaking changes require a versioned contract change. Unavailable values are JSON `null`; formatting is locale-independent.

```json
{
  "schemaVersion": "1.0",
  "provider": "alpaca",
  "feed": "iex",
  "retrievedAtUtc": "2026-11-17T19:30:00Z",
  "timeZone": "America/Chicago",
  "marketSession": "regular",
  "symbols": [
    {
      "symbol": "NVDA",
      "currency": "USD",
      "price": 183.25,
      "previousClose": 182.10,
      "change": 1.15,
      "changePercent": 0.6315,
      "priceType": "last_trade",
      "priceTimestampUtc": "2026-11-17T19:29:55Z",
      "priceTimestampCentral": "2026-11-17T13:29:55-06:00",
      "quoteAgeSeconds": 5,
      "isStale": false,
      "status": "ok",
      "error": null
    }
  ],
  "summary": {
    "requested": 1,
    "succeeded": 1,
    "failed": 0
  },
  "error": null
}
```

The values are illustrative, not actual market observations. At the top level, every response identifies schema version, provider, selected feed when valid, retrieval time, time zone, session, symbol results, and summary. A request-level failure uses a structured `error`; a symbol-level failure uses `status` and structured `error` on that symbol. `ok` means both latest trade and previous close are available; `partial` means at least one is available; `error` means neither is available or the symbol is invalid. `summary.succeeded` counts `ok` and `partial` symbols, and `failed` counts `error` symbols. Partial batches retain successful results.

## 7. Failures and process behavior

Machine-readable errors cover missing configuration, invalid symbols, authentication, unauthorized feeds, rate limits, timeouts, unavailable provider, missing market data, and unexpected responses. Messages are sanitized and never contain credential values or raw provider payloads.

JSON mode writes exactly one valid JSON document to stdout; diagnostics go to stderr. Provider operations have a 30-second command-level timeout and do not retry. The SDK's `IEnvironment` configuration applies a configured HTTPS data origin; the default is `Environments.Live.AlpacaDataApi`. The separate clock/calendar reads use `Environments.Live`, never the paper-trading endpoint. No alternate provider or feed is used as fallback.

Exit codes are deterministic: `0` complete success, `1` partial or symbol-level failure, `2` usage/invalid input, `3` configuration failure, `4` provider/authentication/transport failure, and `130` cancellation. Help and version return `0`.

## 8. Verification requirements

Automated tests use fake provider interactions and no live secrets. Cover symbol normalization/deduplication/validation; configuration and endpoint validation; JSON shape; positive/negative/zero/missing-close arithmetic; UTC/Central conversion and daylight-saving transitions; session and freshness behavior; authentication/feed/network failures; partial results; error sanitization; and non-interactive JSON behavior.

Ordinary CI does not contact Alpaca. Any optional live check is explicitly invoked by the owner using local environment configuration. Packaging verification installs the tool into an isolated path/cache and exercises it from another working directory.
