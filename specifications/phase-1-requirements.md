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
market quote RACK --feed sip --json
market doctor --feed iex --check-connection --json
market --help
market --version
```

`quote` accepts one or more symbols, normalizes them to uppercase, removes duplicates while preserving first-seen order, validates them before requests, and uses batching where supported. JSON is its default output. `doctor` checks configuration and endpoint/feed settings without prompting; a separately selected connectivity check may make a bounded authenticated read.

Ticker syntax is one to ten ASCII letters/digits, with optional interior `.` or `-` separators (for example `BRK.B` and `3M`). Requests are limited to 1,000 distinct symbols to bound provider work and match the SDK's batched request size. Duplicate input symbols count once.

`market doctor --check-connection --json` optionally tests authenticated market-data access by requesting the latest trade for SPY on the configured feed. It reports access only; the probe price is not emitted or stored.

### Optional feed selection (Stage A)

Both commands accept one optional two-token `--feed VALUE` after the command name, before, between, or after other command arguments. Stage A introduced `iex` and `sip`, with case-insensitive values and canonical lowercase JSON output; Stage B's extension is below. The option name is case-sensitive. Feed values are not symbols and never appear in symbol results.

Resolve the feed in this order: explicit `--feed`, then `ALPACA_DATA_FEED`, then `iex`. An absent or whitespace-only environment value selects the default. A valid CLI value overrides even an invalid environment feed; other configuration still requires validation. The override affects only this invocation and never writes to the environment or persistent settings.

Missing, empty, whitespace-padded, or unsupported CLI values, duplicate `--feed` options (even identical values), `--feed=VALUE`, and root-level placement fail with `INVALID_ARGUMENTS` and exit `2`. An invalid CLI selection never falls back to the environment. An invalid effective environment feed remains `INVALID_CONFIGURATION`, exit `3`, subject to the existing missing-credential diagnostic ordering. Malformed CLI arguments take precedence over configuration failures and never create a provider. Existing help and version handling is unchanged, including help taking precedence when present.

Quote failures retain the v1 quote envelope and default JSON output. An invalid or ambiguous CLI feed has `feed: null`; a valid selection is preserved in configuration and provider failures. Doctor usage errors keep their existing v1 usage-error shape in JSON mode; normal configuration/access responses retain their selected feed and diagnostics. Messages do not echo rejected option values. The existing JSON properties, freshness meaning, exit codes, timeout, cancellation, and no-fallback behavior are unchanged.

Option-like tokens beginning with `--`, including malformed feed values and assignments, never become quote symbols or contribute to summary counts. Keep these tokens available for option validation so they still fail explicitly; recognized options such as `--json` retain their behavior. This clarifies the existing symbol/error contract without changing schema 1.0.

Stage B extends these rules as specified below; other SDK feeds, aliases, and numeric enum values remain unsupported.

### Delayed SIP and schema selection (Stage B)

Under accepted [ADR-006](../decisions/ADR-006-delayed-sip-schema-1-1.md), both commands additionally accept case-insensitive `delayed_sip` from CLI or environment, with canonical lowercase/underscore output. Existing precedence and validation remain unchanged. A valid IEX/SIP CLI override of delayed environment selection still works with default schema 1.0.

Accept one optional two-token `--schema-version 1.0|1.1` anywhere after the command name, in either command, independently of feed-option order. No environment schema setting exists. Default is 1.0. Missing, empty, padded, unsupported, duplicate, equals-form, and root-level options are usage errors (exit 2), with no rejected values echoed or serialized as symbols. Invalid/ambiguous schema options report schema 1.0; a valid 1.1 selection also applies to other command usage/configuration/provider failures. Root-level unknown-command errors retain schema 1.0. Help/version behavior is unchanged.

An effective delayed SIP selection requires explicit schema 1.1, including offline doctor. Otherwise return `INVALID_ARGUMENTS`, exit 2, before provider creation, even if credentials are missing. A quote response to this incompatible combination retains schema 1.0 with `feed: null` so the old schema never claims a supported delayed selection. A valid 1.1 selection preserves delayed provenance on configuration/provider failures. Malformed feed input leaves feed metadata null. Other configuration validation and exit codes remain unchanged.

For delayed SIP, latest-trade requests and doctor SPY probes use `delayed_sip`; historical prior-session bars explicitly use `sip` with raw adjustment. Retain the observation-relative reference session, batching, and bounded pagination. Only send historical windows ending at least 15 minutes before the provider clock; if that condition cannot be established, leave the comparison unavailable without shifting its date. IEX/SIP mapping is unchanged. No feed fallback follows any error. Offline doctor does not establish entitlement; the connectivity probe establishes only latest-trade access, not historical-bar access.

Schema 1.1 preserves every existing field's meaning and adds:

| Location | Field | Meaning |
| --- | --- | --- |
| Quote and normal doctor envelopes | `previousCloseFeed` | Selected historical request policy: `sip` for delayed SIP, otherwise the selected feed; null if unknown. |
| Quote and normal doctor envelopes | `expectedDelaySeconds` | Nominal delay, 900 for delayed SIP or 0 for IEX/SIP; null if unknown. This is not measured latency or proof of access. |
| Each quote symbol | `excessAgeSeconds` | Floor of max(0, actual trade age minus nominal delay), or null for unavailable/invalid timestamps. |
| Each quote symbol | `isStaleBeyondExpectedDelay` | Full-precision excess age strictly exceeds `MARKET_STALE_AFTER_MINUTES`; null with no valid timestamp. |

Keep raw `quoteAgeSeconds` and `isStale` unchanged. At the default threshold a delayed 901-second-old trade is raw-stale, has one excess second, and is not stale beyond the expected delay. Exactly 1,800 seconds is not beyond the threshold; 1,800 seconds plus one tick is. Neither age measure changes success/partial/error status. Delay-adjusted age is not a provider-health diagnosis; closed-market observations may remain old. Metadata fields remain present with null values where required in schema 1.1 and are entirely absent from schema 1.0. Normal doctor includes feed-policy metadata; its usage-error envelope retains only schema/provider/error.

Home remains pinned/configured separately and is not upgraded to consume schema 1.1 or delayed SIP by this work. Live entitlement verification remains separately authorized.

## 3. Authentication and endpoint

`ALPACA_API_KEY` and `ALPACA_API_SECRET` are required for authenticated data calls. The optional `--feed` overrides `ALPACA_DATA_FEED`; both select `iex`, `sip`, or `delayed_sip`, with `iex` as the default and delayed SIP gated on schema 1.1. The response identifies the selected feed when valid for its schema. An unauthorized feed is a structured failure, not a reason to downgrade.

`ALPACA_DATA_URL` is optional. The SDK's documented default is used when unset. A configured endpoint must be a validated HTTPS URL and must be applied through a supported SDK configuration mechanism. Invalid or unsupported configuration fails explicitly. Credentials are never requested interactively or persisted.

## 4. Observation and calculation semantics

- `price` is the latest eligible provider stock trade. It is not a bid, ask, midpoint, or daily-bar close.
- `priceType` is `last_trade`; the actual trade timestamp is authoritative.
- `previousClose` is the raw close of the regular trading session immediately before the observation's trading date. Associate each trade with its own date and regular or extended-hours window in Alpaca's Eastern-time calendar. Premarket, regular, and after-hours trades on the same trading date use the same reference close, even if retrieved on a later day. If the trade cannot be associated with a trading date or its reference bar is unavailable, the comparison fields are null.
- Daily bars use the `raw` adjustment basis. This preserves price units consistently but does not remove split/dividend discontinuities.
- `change = price - previousClose`.
- `changePercent = (change / previousClose) * 100`.
- Calculations use decimal arithmetic. If previous close is absent or zero, both calculated values are null.
- Missing observations and prior bars are represented as null with symbol-level status and a structured error; no price or timestamp is fabricated.

The configured feed is explicit and is never silently switched. IEX reflects a limited venue and may have infrequent trades for thinly traded ETFs. SIP is consolidated only when the account's plan authorizes it. Neither feed is described as equivalent to the other.

## 5. Time, session, and freshness

All instants are UTC; the user-facing observation time is also rendered in `America/Chicago`, including daylight-saving offsets. `retrievedAtUtc` is distinct from the provider observation timestamp.

Top-level `marketSession` describes the exchange around retrieval, using Alpaca's clock and calendar, and is one of `regular`, `pre_market`, `after_hours`, `closed`, or `unknown`. If that information cannot be established reliably, use `unknown`. It does not describe any individual observation or determine its trading date. The v1 response does not expose a per-symbol session or reference date.

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
