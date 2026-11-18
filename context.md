# Project Context

## Purpose and users

`market` retrieves provider-backed prices for arbitrary supported US stock and ETF symbols through Alpaca's market-data API. It is intended for its owner, scripts, VS Code/Codex, and other authorized AI-agent workflows that can execute a local command.

The CLI returns structured data; it does not grant remote agents access to a workstation or credentials. A future integration requires separate authorization and is outside this phase.

## Phase 1 scope

Phase 1 includes batched latest-trade retrieval, the prior completed regular-session close, daily change calculations, feed and timestamp provenance, market-session and freshness context, versioned JSON, sanitized structured failures, diagnostics, tests, and .NET tool packaging.

The tool is strictly read-only. Phase 1 does not place or cancel orders, modify accounts or portfolios, persist observations, expose a service, or implement an MCP server.

## Success

Success means the documented contract is implemented and tested, the package installs as `market`, and results distinguish actual observations from missing or stale data without inventing values. Live Alpaca verification is reported only when it was actually performed with locally configured credentials.
