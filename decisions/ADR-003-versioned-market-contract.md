# ADR-003: Versioned JSON and Observation Semantics

## Status

Accepted

## Context

Scripts and AI agents need a stable output shape, while market observations can be delayed, missing, or sourced from feeds with different coverage.

## Decision

Use `System.Text.Json` and a versioned JSON contract. Price means the timestamped latest eligible trade; previous close means the prior completed regular-session raw daily bar. Compute daily changes with decimal arithmetic. Include feed, UTC and Central observation times, retrieval time, market session, age, stale state, and per-symbol errors.

## Consequences

Breaking output changes require an explicit schema-version change. Missing inputs remain null; stale observations are identified rather than relabeled as current. Raw bars can show corporate-action discontinuities. Feed coverage must not be conflated with freshness.
