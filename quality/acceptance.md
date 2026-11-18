# Acceptance and Verification

## Acceptance criteria

1. The repository conforms to LlamaRC42 Documentation Contract v0.1 and passes the actual upstream Core validator.
2. The .NET 10 solution builds and focused automated tests pass without live Alpaca credentials.
3. `market` installs as a cross-platform .NET tool and runs outside the source directory.
4. Quotes accept arbitrary supported symbols, preserve normalized input order, and batch provider requests where supported.
5. JSON is valid, versioned, locale-independent, and contains honest price semantics, timestamps, feed, session, freshness, summary, and structured errors.
6. Previous close is the prior completed regular-session raw bar; unavailable inputs do not produce fabricated calculations.
7. Secrets are absent from output and errors; the program remains read-only.
8. Implementation, docs, tests, and the recorded verification evidence agree.

## Required checks

- Validate documentation using the actual LlamaRC42 Core `DocumentationValidator` API. The upstream CLI repository currently states that CLI v0.1 is not implemented; do not replace the validator with an invented script or claim a nonexistent command was run.
- Restore and release-build the solution with .NET 10.
- Run all automated tests and JSON contract tests.
- Pack and install the tool into an isolated location; invoke it from outside the repository.
- Do not run live provider checks in ordinary CI. Report live verification only if an authorized request was actually made.
- Test boundary dates and daylight-saving transitions using the Alpaca calendar's Eastern-time offsets and the `America/Chicago` output zone.
- For PR #1, regress the eight implementation findings against the existing contract: zero SDK retries, partial quotes on clock/calendar failure, complete and bounded bar pagination, hard deadlines and caller cancellation, sanitized malformed responses, doctor exit 130, Unix environment-name collisions, and exact v1 session strings. These are implementation corrections, not new requirements or ADR changes.

## Evidence discipline

Record command and outcome for each performed check. Distinguish compile-time, mocked, package-install, and live-provider evidence. A proposed future capability or successful build is not evidence that a live data request works.

## Verification evidence

- LlamaRC42 Core `DocumentationValidator.Validate` API, invoked through a temporary .NET harness against this repository — `llamarc42/v0.1: Valid` (Core source revision `67d0c8edba57baf1bd6d25a9fbd5b1dcae5ae267`). The upstream validation CLI was not used.
- `dotnet test Market.slnx --configuration Release` — passed; 32 tests, 0 failures, no live credentials.
- `dotnet pack src\Market.Cli\Market.Cli.csproj --configuration Release --no-build --output artifacts\packages` — succeeded; package includes `README.md`.
- Installed `Market.Cli` 0.1.0 from the local package to an isolated temporary tool path. From the temporary directory, `market --version` returned `0.1.0` and `market --help` succeeded.
- `dotnet list Market.slnx package --vulnerable --include-transitive` — no vulnerable packages reported by the configured NuGet sources.
- Live Alpaca data retrieval — not performed; no live-access claim is made.

### PR #1 remediation evidence

- Installed `Alpaca.Markets` 8.0.1 identifies source revision `4f39d9f2156fe82ec78a5718f7e4da582492acd5`. Its throttler defaults to five retries, its bar pages expose `NextPageToken` via `Pagination.Token` on requests, and its Newtonsoft response deserializer does not accept a cancellation token. The fixes use those SDK contracts without changing the accepted requirements or v1 wire schema.
- On Windows with .NET SDK 10.0.404, `dotnet restore Market.slnx --locked-mode`, `dotnet build Market.slnx --configuration Release --no-restore`, and `dotnet test Market.slnx --configuration Release --no-build` passed (zero build warnings/errors, 58 test cases). The Unix SIGINT process case runs on Linux and macOS; Windows verifies caller cancellation through the CLI application.
- SDK regression fixtures counted the actual requests on loopback for 429/503 with `Retry-After`, checked both context failures and cancellation, and verified 1,000 prior closes across multiple pages (including an empty page). An unusable continuation or conflicting duplicate fails with `UNEXPECTED_PROVIDER_RESPONSE`; a safety cap limits a batch to 2,048 pages rather than returning an incomplete result as complete.
- CLI regressions checked both commands with blocked tasks, late faults and malformed SDK JSON, all five v1 session values, and help/version with differently cased proxy names. The CLI stops waiting at the command deadline or on cancellation; an SDK body read that ignores cancellation can continue until it finishes or the process exits, but cannot write command output.
- Repacked and installed `Market.Cli` 0.1.0 using a temporary tool path and NuGet cache. Outside the repository, `market --help` and `market --version` succeeded; `market doctor --json` and `market quote RACK TCAI CLOU ARTY DTCR --json` returned valid missing-configuration JSON and exit 3 with credentials deliberately absent.
- Re-ran Core `DocumentationValidator.Validate` at the pinned revision above: `llamarc42/v0.1: Valid`. NuGet's direct/transitive vulnerability check reported no vulnerable packages from the configured sources. No live Alpaca verification was performed.
