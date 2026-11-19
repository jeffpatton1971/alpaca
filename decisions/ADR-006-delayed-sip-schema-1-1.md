# ADR-006: Delayed SIP and Opt-In Schema 1.1

## Status

Accepted

## Context

On November 19, 2026, the owner approved Stage B implementation and a review PR from merged main `cec1cad83a23dcbc1a6a9281da70f6d0ea96e89f`. Live entitlement tests, merging, and home automation changes remain excluded. Alpaca.Markets 8.0.1 already exposes `DelayedSip`; the latest-trades API accepts `delayed_sip`, while historical bars require `sip`. Current schema 1.0 measures raw age and cannot separately describe the expected delay or historical feed.

## Decision

Extend CLI/environment selection to `iex`, `sip`, and `delayed_sip`, with unchanged CLI/environment/IEX-default precedence. Use explicit canonical names rather than lowercasing SDK enum names. Delayed latest trades and doctor probes use `DelayedSip`; prior-session raw bars use `Sip`. This mapping is fixed before requests, not an authorization fallback. Keep the existing reference date and pagination. For delayed selection, skip a historical window whose end is newer than the provider clock minus 15 minutes; never shift to another reference session or return a fabricated close.

Add one optional two-token `--schema-version 1.0|1.1` after either command. Default to 1.0; require explicit 1.1 for delayed SIP. Invalid schema selection or an incompatible delayed/schema combination is a usage error before provider creation. Schema 1.0 preserves the existing IEX/SIP output shape, values, nulls, and raw-age semantics.

Schema 1.1 adds `previousCloseFeed` and `expectedDelaySeconds` to quote and normal doctor envelopes, and `excessAgeSeconds` and `isStaleBeyondExpectedDelay` to quote symbols. Feed metadata describes the selected request policy, not evidence that a request succeeded. Unknown/invalid selections have null metadata. Doctor usage errors retain the usage-error shape with the selected valid schema version.

Expected delay is nominal: 900 seconds for delayed SIP and 0 for IEX/SIP. Compute excess age as max(0, actual age minus expected delay), floor seconds only for display, and compare full-precision excess age strictly greater than the configured stale threshold. Missing/invalid timestamps have null age-derived fields. Preserve raw `quoteAgeSeconds` and `isStale`; neither raw nor delay-adjusted stale state changes status/exit codes. These are age measures, not diagnoses of outages or guarantees of activity during closed markets.

## Consequences

Existing consumers continue using 1.0. Home currently rejects other schema versions and delayed feeds and remains unchanged. The API capability and nominal delay are documented; account entitlement is not verified. Keep existing authorization failures, no fallback, credential sanitization, deadlines, cancellation, and offline doctor behavior. No SDK update, generic HTTP client, streaming, background service, package publication, or home adoption is included.

## Related decisions and evidence

This extends the IEX/SIP-only selection in [ADR-005](ADR-005-invocation-feed-selection.md) and the same-feed request rule in [ADR-002](ADR-002-alpaca-read-only-adapter.md) specifically for delayed latest data with historical SIP. It adds an opt-in representation under [ADR-003](ADR-003-versioned-market-contract.md), without redefining 1.0. Their original decisions remain recorded as historical context.

- [Alpaca latest trades](https://docs.alpaca.markets/us/reference/stocklatesttrades-1): delayed SIP is a supported 15-minute-delayed feed.
- [Historical bars](https://docs.alpaca.markets/us/reference/stockbars) and [market-data FAQ](https://docs.alpaca.markets/us/docs/market-data-faq): use SIP and an end at least 15 minutes old without recent-data access.
- [SDK 8.0.1 enum](https://github.com/alpacahq/alpaca-trade-api-csharp/blob/4f39d9f2156fe82ec78a5718f7e4da582492acd5/Alpaca.Markets/Enums/MarketDataFeed.cs): canonical `delayed_sip` and endpoint restrictions.
- [Subscription documentation](https://docs.alpaca.markets/us/docs/about-market-data-api): plan descriptions do not establish this account's entitlement.
