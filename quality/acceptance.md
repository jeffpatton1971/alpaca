# Acceptance and Verification

## Acceptance criteria

1. The repository conforms to LlamaRC42 Documentation Contract v0.1 and passes the actual upstream Core validator.
2. The .NET 10 solution builds and focused automated tests pass without live Alpaca credentials.
3. `market` installs as a cross-platform .NET tool and runs outside the source directory.
4. Quotes accept arbitrary supported symbols, preserve normalized input order, and batch provider requests where supported.
5. JSON is valid, versioned, locale-independent, and contains honest price semantics, timestamps, feed, session, freshness, summary, and structured errors.
6. Previous close is the raw regular-session bar immediately preceding each observation's trading date; unavailable inputs do not produce fabricated calculations.
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
- Regress per-observation comparison dates, including mixed-date batches, early closes, daylight saving, non-trading days, and absent calendar/bar data. The original implementation selected Tuesday's close for a Tuesday after-hours trade and Friday's close for a Saturday retrieval of a Friday trade. The approved disposition clarifies the reference date under ADR-003 without changing its decision to use the prior regular-session raw close or changing the v1 JSON shape. The top-level session keeps its existing retrieval-clock behavior.

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

### Observation-relative daily change

- Synthetic SDK-backed tests reproduced the wrong reference-date requests on the reviewed head, including Tuesday after-hours, weekend and holiday retrieval of Friday trades, and stale trades. The corrected Tuesday example reports a $100 prior close, $6 change and 6% change for a $106 trade. Coverage includes regular and extended hours, early close, daylight saving, missing reference data, invalid timestamps, and paginated batches with different reference dates. The v1 response shape and retrieval-clock `marketSession` are unchanged.
- Locked restore, Release build and all 75 tests passed locally on Windows with .NET SDK 10.0.404; build reported zero warnings and errors. The isolated package installed and ran outside the repository. NuGet reported no vulnerable direct or transitive packages from the configured sources.
- Ran the pinned LlamaRC42 Core `DocumentationValidator.Validate` API after the documentation update: `llamarc42/v0.1: Valid`.
- Live verification was blocked: `ALPACA_API_KEY` and `ALPACA_API_SECRET` were absent from the process environment. `market doctor --json` identified both missing variables; the five-symbol quote returned all five symbols in its configuration-error JSON and exited 3 without contacting Alpaca. The default IEX setting shown by doctor is not evidence of subscription authorization. No live prices, per-symbol freshness, prior closes, response latency, or hourly-monitoring suitability were established for RACK, TCAI, CLOU, ARTY, or DTCR. IEX is venue-limited, and SIP access cannot be inferred from configuration alone.

### Owner-run live verification

- On November 18, 2026, the owner ran `doctor --check-connection --json` and `quote RACK TCAI CLOU ARTY DTCR --json` from the configured PowerShell session. Doctor confirmed authenticated IEX access. The v1 quote response contained five successful results, with a trade timestamp and a comparison close for each symbol. No credentials appeared in the supplied output.
- The retrieval was premarket; all five observations were from the prior regular session and correctly marked stale against the 15-minute threshold. Recalculation matched the reported ages (16h 20m 50s to 19h 29m 27s) and all five changes and percentages. For those November 17 observations, the expected reference session is November 16. The v1 response does not expose bar dates, and the reference bars were not independently queried during this check.
- This confirms live connectivity and result completeness for one premarket sample, not freshness during regular hours. IEX is venue-limited, SIP was not tested, and the transcript does not establish exact request latency. Assess hourly monitoring with repeated samples during regular market hours before relying on the feed for that use.
