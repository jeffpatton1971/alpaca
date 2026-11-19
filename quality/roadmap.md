# Roadmap

## Phase 1 — Market-data CLI

Implement a read-only .NET tool for latest stock/ETF observations, previous completed-session close, daily calculations, feed provenance, freshness/session context, stable JSON, structured errors, tests, and packaging.

Stage A feed selection was merged under [ADR-005](../decisions/ADR-005-invocation-feed-selection.md): optional IEX/SIP invocation overrides with schema 1.0 unchanged. Stage B is approved for implementation and a review PR under [ADR-006](../decisions/ADR-006-delayed-sip-schema-1-1.md): delayed SIP with explicit historical SIP mapping and opt-in schema 1.1. Live entitlement tests, merging Stage B, publication, and home automation adoption remain unapproved.

## Potential Phase 2 — Historical observations

Planning context only; not authorized for implementation. Potentially add scheduled collection, local SQLite storage, historical price comparison, intraday movement analysis, weekly performance summaries, and freshness/completeness reporting.

## Potential Phase 3 — AI-agent integration

Planning context only; not authorized for implementation. Potentially provide an MCP server or adapter and authorized structured access for ChatGPT or other agents. Such an integration must not assume a remote agent can execute commands on the owner's local workstation.
