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

### Stage A feed selection checks

- Verify default/environment/CLI precedence for both commands, including a valid CLI override of an invalid environment feed, case handling, and unchanged environment values across invocations.
- Reject missing, empty, whitespace-padded, duplicate, malformed, unsupported, and root-level feed options before creating a provider. Keep invalid CLI and environment exit codes distinct and exclude rejected feed values from output.
- Verify selected-feed provenance on success and configuration/provider failures, both quote/doctor SDK requests, no fallback, and offline doctor behavior. Preserve all v1 properties, nulls, raw-age freshness, symbol order/limit, deadlines, cancellation, and sanitization.
- Run existing home v1 validation against synthetic outputs without modifying home, its pins, or its settings. Run locked restore, Release build, all tests, actual Core documentation validation, and isolated package installation from outside the repository.

## Evidence discipline

Record command and outcome for each performed check. Distinguish compile-time, mocked, package-install, and live-provider evidence. A proposed future capability or successful build is not evidence that a live data request works.

## Verification evidence

### PR #2 flag-like feed value correction — November 19, 2026

- Reproduced the P2 finding before the production correction: the expanded invalid-feed regression matrix had 4 failures and 46 passes because flag-like tokens became quote symbols. Quote symbol extraction now excludes tokens starting with `--` while retaining them for option validation. This preserves explicit usage errors, CLI/environment/default precedence, and schema 1.0; no accepted decision changed.
- Added quote/doctor cases for flag-like feed values, flag-like assignments, equals-form assignments, and recognized options after a missing feed value. Assertions cover exit 2, `INVALID_ARGUMENTS`, no provider creation, no rejected-value disclosure, and only the requested symbol/counts. Separate cases prove unknown assignments still fail both with and without a valid CLI override.
- With .NET SDK 10.0.404 on Windows, `dotnet restore Market.slnx --locked-mode --configfile <isolated NuGet.org config>`, `dotnet build Market.slnx --configuration Release --no-restore`, and `dotnet test Market.slnx --configuration Release --no-build --no-restore` passed: zero build warnings/errors and all 174 tests passed.
- The actual pinned LlamaRC42 Core `DocumentationValidator.Validate` API (`67d0c8edba57baf1bd6d25a9fbd5b1dcae5ae267`), run through the isolated validation host, returned `llamarc42/v0.1: Valid` with no diagnostics/tool errors, including after this evidence update. `git diff --check` passed.
- `dotnet pack src/Market.Cli/Market.Cli.csproj --configuration Release --no-build --no-restore` and a fresh isolated local-source installation passed. Nine checks outside the repository passed: help, version, offline doctor with synthetic credentials and a CLI override, credential-free doctor/quote, rejected delayed SIP, and three malformed flag-like value/assignment cases. The malformed quote responses retained schema 1.0 and only the requested symbols/counts without leaking rejected values.
- Re-ran the external synthetic CLI harness and home's unchanged `Read-Observation` validator for environment-selected, CLI-overridden, stale, partial, and provider-error responses. All five passed with expected complete/degraded classifications; the validator blob still matched `03b31c72ca689d284b4752775caf39025519e568`. No live requests, home changes, automation changes, publication, or merge were performed.

### Stage A optional feed selection — November 18, 2026

- Owner approval covers Stage A only. Requirements and accepted ADR-005 were updated before implementation; IEX/SIP remain the only supported feeds and schema 1.0 retains its existing properties and raw-age freshness meaning.
- On Windows with .NET SDK 10.0.404: `dotnet restore Market.slnx --locked-mode --configfile <isolated NuGet.org config>`, `dotnet build Market.slnx --configuration Release --no-restore`, and `dotnet test Market.slnx --configuration Release --no-build` passed: zero build warnings/errors and 160 test cases. Tests include both commands' precedence, invalid overridden environment feeds, ordering, duplicates, rejected values, configuration/provider error provenance, no fallback, no environment mutation, invocation isolation, SDK query parameters, symbol limits, and deadlines/cancellation with an override.
- The first test run found two incorrect synthetic single-symbol bar response shapes in the new SDK fixtures. Those fixtures were corrected to the SDK's single-symbol response contract; no provider behavior change was needed. The subsequent full suite passed. Local Windows runs do not exercise the Unix-only SIGINT path; the existing Linux/macOS CI matrix remains responsible for it.
- Ran the actual pinned LlamaRC42 Core `DocumentationValidator.Validate` API (`67d0c8edba57baf1bd6d25a9fbd5b1dcae5ae267`) through an isolated .NET host: `llamarc42/v0.1: Valid`, no diagnostics/tool errors. Initial dependency checks encountered restricted network access and an unrelated unavailable machine-level Azure package source; an isolated configuration using NuGet.org for packages and audit data resolved them without changing repository configuration or disabling audit.
- `dotnet pack src/Market.Cli/Market.Cli.csproj --configuration Release --no-build --no-restore` succeeded. Installed Market.Cli 0.1.0 from only the resulting local source into an isolated tool path/cache. Outside the repository, help/version passed; offline doctor with synthetic credentials and `--feed SIP` over an invalid environment feed returned schema 1.0, feed `sip`, exit 0. Credential-free doctor and five-symbol quote checks retained the selected feed and exited 3. `--feed delayed_sip` exited 2. No authenticated requests were made.
- An external synthetic harness exercised the actual CLI for the five home symbols and passed its generated environment-selected, CLI-overridden, stale, partial, and unauthorized-feed responses through home's unchanged `Read-Observation` validator. All five were accepted with the expected complete/degraded classifications. The validator blob was checked against `03b31c72ca689d284b4752775caf39025519e568` from home commit `7f724991edd1b9839279a3804112d13b8d2e2057`. No home files, pins, observations, or automation settings were changed.
- The SDK, package version, provider adapter, output models, quote calculations, and workflow configuration are unchanged. No package publication, release, merge, or live entitlement test was performed.
- `dotnet list Market.slnx package --vulnerable --include-transitive --no-restore --source https://api.nuget.org/v3/index.json` reported no vulnerable direct or transitive packages. `git diff --check` passed.

### Original Phase 1 verification

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
