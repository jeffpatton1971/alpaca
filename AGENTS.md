# Repository Instructions

Start with [project.md](project.md). LlamaRC42 Documentation Contract v0.1 is authoritative for project documentation; current requirements and accepted decisions are linked there. This file is navigation and contributor guidance, not a substitute for those documents.

## Required workflow

- Update current requirements or decision records before changing behavior. Do not silently override an accepted decision; record evidence and a proposed disposition using the LlamaRC42 history/decision process.
- Run the actual LlamaRC42 Core `DocumentationValidator` API for documentation changes. The upstream CLI validate command is not yet implemented; do not claim a substitute is the framework validator.
- Run .NET 10 restore, Release build, tests, and isolated tool-package verification for code changes. Record actual outcomes in [acceptance and verification](quality/acceptance.md).
- Keep tests mocked and credential-free. Live data checks are opt-in and must use credentials already configured locally; never ask for or print them.
- Preserve the read-only boundary, structured JSON contract, deterministic exit codes, cancellation, and non-interactive execution.
- Do not implement persistent history or AI-agent/MCP integration without separate approval.
- Update the README and changelog for user-visible changes. Do not publish a package, create a release, merge, or claim live verification without explicit authorization/evidence.
