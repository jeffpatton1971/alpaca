# ADR-001: .NET 10 Standalone CLI

## Status

Accepted

## Context

Phase 1 needs a portable command for humans and automation with a small operational footprint, stable machine-readable output, and no server or persistent data store.

## Decision

Implement `market` as a standalone C# application targeting .NET 10 and package it as a cross-platform .NET tool. Use asynchronous operations and cancellation. The installed command name is `market`.

## Consequences

The user must have a compatible .NET runtime for a framework-dependent tool. The CLI is independently usable by authorized local scripts and does not imply access by ChatGPT or other remote agents. A later integration is a separate phase and approval.
