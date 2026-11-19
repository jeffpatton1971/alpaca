# Alpaca Market CLI

Documentation contract: llamarc42/v0.1

## Purpose

`market` is a standalone, read-only .NET command-line tool that retrieves current and recent US stock and ETF market data from Alpaca and emits accurate, versioned JSON for people and automation.

## Documentation

- [Project context](context.md)
- [Constraints](constraints.md)
- [Requirements and JSON contract](specifications/phase-1-requirements.md)
- [Architecture overview](architecture/overview.md)
- [Architecture boundaries](architecture/boundaries.md)
- [ADR-001: .NET CLI](decisions/ADR-001-dotnet-cli.md)
- [ADR-002: Alpaca read-only adapter](decisions/ADR-002-alpaca-read-only-adapter.md)
- [ADR-003: versioned JSON](decisions/ADR-003-versioned-market-contract.md)
- [ADR-004: environment credentials](decisions/ADR-004-environment-credentials.md)
- [ADR-005: invocation-scoped feed selection](decisions/ADR-005-invocation-feed-selection.md)
- [ADR-006: delayed SIP and opt-in schema 1.1](decisions/ADR-006-delayed-sip-schema-1-1.md)
- [Acceptance and verification](quality/acceptance.md)
- [Risks and limitations](quality/risks.md)
- [Roadmap](quality/roadmap.md)

The [README](README.md) and [AGENTS.md](AGENTS.md) are navigation and usage entry points, not sources of engineering authority.
