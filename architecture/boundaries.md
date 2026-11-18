# Architecture Boundaries

## In scope

The application may read Alpaca market-data observations and the minimum authoritative clock/calendar information needed to describe them. It returns data to its caller without retaining it.

The provider adapter owns SDK-specific configuration and translates provider failures to safe domain error codes. The CLI owns argument handling and output selection. Quote calculations use typed values and are independent of console I/O.

## Out of scope

- Order placement/cancellation or any trading/account/portfolio mutation.
- Arbitrary URLs, generic HTTP commands, or alternate-provider fallback.
- Persistent history, scheduled collection, web APIs, MCP, or direct ChatGPT access.
- Claims of SIP consolidation unless SIP was actually selected and authorized.
- Claims that IEX represents the consolidated US market.

The SDK may expose capabilities beyond the application's boundary. Their presence does not authorize exposing or invoking them.
