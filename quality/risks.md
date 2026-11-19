# Risks and Limitations

## Data and subscriptions

- An IEX latest trade may be old for a thinly traded symbol; age is surfaced and does not make the venue consolidated.
- SIP requests can fail when the account is not entitled. The CLI reports that failure and does not downgrade to IEX.
- A valid `--feed` overrides only feed selection; it does not establish entitlement, replace missing credentials, or change the home automation pin/settings. Delayed SIP uses nominal 15-minute delay metadata under opt-in schema 1.1; this account's entitlement remains unverified. Doctor's latest-trade probe does not establish historical SIP access.
- Excess age does not identify whether an old trade reflects illiquidity, closed markets, an outage, or ingestion latency. The nominal delay is not a measured delay or a freshness guarantee. Raw-age `isStale` remains unchanged.
- Home accepts only schema 1.0 and IEX/SIP; adopting schema 1.1 or delayed SIP needs separate consumer work and approval. Existing observations must retain their original provenance.
- A missing latest trade or daily bar remains unavailable. No provider, trade, timestamp, or close is synthesized.
- Raw daily bars can differ across corporate actions; daily changes can therefore be discontinuous.
- Session classification depends on authoritative Alpaca clock/calendar access. If that access fails or is ambiguous, session is `unknown`.

## Operational

- Alpaca service availability, rate limits, account entitlements, and data latency are outside the CLI's control.
- Credentials supplied through environment variables can be visible to local process-inspection tools; users should apply workstation-specific protections.
- A local .NET tool requires a compatible runtime unless published as a separately approved self-contained distribution.
- No live Alpaca verification is claimed until it is explicitly performed with owner-provided local configuration.
