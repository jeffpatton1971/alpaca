# ADR-003: Versioned JSON and Observation Semantics

## Status

Accepted

## Context

Scripts and AI agents need a stable output shape, while market observations can be delayed, missing, or sourced from feeds with different coverage.

## Decision

Use `System.Text.Json` and a versioned JSON contract. Price means the timestamped latest eligible trade; previous close means the prior completed regular-session raw daily bar. Compute daily changes with decimal arithmetic. Include feed, UTC and Central observation times, retrieval time, market session, age, stale state, and per-symbol errors.

## Consequences

Stage B disposition: [ADR-006](ADR-006-delayed-sip-schema-1-1.md) adds an opt-in schema 1.1 representation with delay metadata. Schema 1.0 and the raw-age meaning of its existing fields remain unchanged.

Breaking output changes require an explicit schema-version change. Missing inputs remain null; stale observations are identified rather than relabeled as current. Raw bars can show corporate-action discontinuities. Feed coverage must not be conflated with freshness.
