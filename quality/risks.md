# Risks and Limitations

## Data and subscriptions

- An IEX latest trade may be old for a thinly traded symbol; age is surfaced and does not make the venue consolidated.
- SIP requests can fail when the account is not entitled. The CLI reports that failure and does not downgrade to IEX.
- A missing latest trade or daily bar remains unavailable. No provider, trade, timestamp, or close is synthesized.
- Raw daily bars can differ across corporate actions; daily changes can therefore be discontinuous.
- Session classification depends on authoritative Alpaca clock/calendar access. If that access fails or is ambiguous, session is `unknown`.

## Operational

- Alpaca service availability, rate limits, account entitlements, and data latency are outside the CLI's control.
- Credentials supplied through environment variables can be visible to local process-inspection tools; users should apply workstation-specific protections.
- A local .NET tool requires a compatible runtime unless published as a separately approved self-contained distribution.
- No live Alpaca verification is claimed until it is explicitly performed with owner-provided local configuration.
