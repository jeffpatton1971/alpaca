# ADR-004: Environment-Based Credentials

## Status

Accepted

## Context

The CLI needs authenticated market-data access without receiving secrets in conversation, source control, or project files.

## Decision

Read `ALPACA_API_KEY` and `ALPACA_API_SECRET` from the process environment. Never prompt for, persist, log, serialize, or include these values in exceptions. Validate optional custom endpoint configuration as HTTPS and use it only if the official SDK supports it.

## Consequences

The user configures credentials in their own shell or deployment environment. Tests and ordinary CI use no real credentials. Unsupported endpoint customization fails explicitly rather than bypassing SDK validation.
