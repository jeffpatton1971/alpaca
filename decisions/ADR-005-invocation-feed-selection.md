# ADR-005: Invocation-Scoped Feed Selection

## Status

Accepted

## Context

Environment-only feed selection requires shell changes to compare existing IEX and SIP access. On November 18, 2026, the owner explicitly approved Stage A only: document and implement an optional feed argument with CLI, environment, then default precedence while preserving schema 1.0. Delayed SIP, merging, and home automation changes were not approved.

## Decision

Accept one optional two-token `--feed VALUE` on `quote` and `doctor`, anywhere after the command name. Allow only case-insensitive `iex` and `sip`; retain IEX as the default. A valid CLI selection overrides the environment feed before its validation, without changing the process environment. Invalid CLI input is a usage error and never triggers fallback. Use the validation and error-envelope rules in the current requirements.

Keep schema 1.0, raw-age freshness, the existing SDK and provider calls, credential configuration, and all process/error contracts. Credentials remain environment-only. Delayed SIP and additional feeds are not enabled by the SDK enum's broader capabilities.

## Consequences

Stage B disposition: [ADR-006](ADR-006-delayed-sip-schema-1-1.md) extends selection to delayed SIP only with explicit schema 1.1. The Stage A decision above records the original approval; its precedence and invocation-isolation rules remain unchanged.

Existing commands and the pinned home consumer continue to work. Users can select a feed for one invocation, but selection does not establish entitlement. Doctor configuration checks remain offline unless connection checking is explicitly requested. No package publication, release, merge, live provider test, or automation change is authorized by this decision.

## Related decisions

- [ADR-002: read-only provider adapter](ADR-002-alpaca-read-only-adapter.md) retains the same explicit IEX/SIP requests and no-fallback policy.
- [ADR-003: versioned market contract](ADR-003-versioned-market-contract.md) retains the existing schema and observation semantics.
- [ADR-004: environment credentials](ADR-004-environment-credentials.md) retains exclusive environment-based secret input.

This decision adds feed-selection precedence without amending those decisions' existing rules.
