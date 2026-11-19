# market

`market` is a standalone, read-only .NET 10 CLI for retrieving current and recent US stock and ETF prices from Alpaca. Its versioned JSON is designed for people, scripts, and authorized local AI-agent workflows. Building and packing require the .NET 10 SDK; running the installed framework-dependent tool requires a compatible .NET 10 runtime.

LlamaRC42 Documentation Contract v0.1 is the authoritative engineering source. Start with [project.md](project.md), [Phase 1 requirements](specifications/phase-1-requirements.md), and the [architecture overview](architecture/overview.md).

## Scope and limitations

The CLI reports the latest eligible stock trade, prior completed regular-session close, daily change, UTC and America/Chicago timestamps, market session, feed, and freshness. A missing value stays null; a stale trade is marked stale.

IEX covers a limited venue and may be infrequent for thinly traded ETFs. SIP provides consolidated information only when the Alpaca account is authorized. The selected feed is explicit and is never silently changed. This tool is not a trading client: it does not place orders or modify an account or portfolio.

Phase 1 does not store history or provide a web service, MCP server, or direct ChatGPT integration.

## Install

Build and pack locally (publication to NuGet is not implied):

```powershell
dotnet restore Market.slnx
dotnet build Market.slnx --configuration Release --no-restore
dotnet test Market.slnx --configuration Release --no-build
dotnet pack src\Market.Cli\Market.Cli.csproj --configuration Release --output artifacts\packages
dotnet tool install --global Market.Cli --add-source (Resolve-Path artifacts\packages).Path
market --version
```

On macOS/Linux, use `src/Market.Cli/Market.Cli.csproj`, `artifacts/packages`, and `"$PWD/artifacts/packages"` in the corresponding commands. Ensure the global .NET tools directory is on `PATH`. Updating and uninstalling:

```text
dotnet tool update --global Market.Cli --add-source <package-directory>
dotnet tool uninstall --global Market.Cli
```

## Configure

Set `ALPACA_API_KEY` and `ALPACA_API_SECRET` in the process environment. Optionally set `ALPACA_DATA_URL` to an HTTPS origin (without credentials, path, query, or fragment), `ALPACA_DATA_FEED` to `iex`, `sip`, or `delayed_sip` (default `iex`), and `MARKET_STALE_AFTER_MINUTES` to a positive whole number (default 15). Delayed SIP additionally requires the schema opt-in below. Do not put credentials in project files or command-line arguments.

Both `quote` and `doctor` accept `--feed iex`, `--feed sip`, or `--feed delayed_sip` for one invocation. Selection precedence is **CLI → environment → default IEX**. A valid override also replaces an invalid environment feed; it never changes your shell or persistent settings. Feed values are case-insensitive and JSON uses canonical lowercase names. An unset or blank environment feed uses IEX.

Use the two-token option once, anywhere after the command name. Missing, empty, whitespace-padded, unsupported, or duplicate values, `--feed=value`, and root-level placement are usage errors (exit 2). An invalid effective environment feed is a configuration error (exit 3), subject to missing-credential diagnostic ordering.

Schema defaults to **1.0**. Delayed SIP requires explicit `--schema-version 1.1` on either command, including offline doctor. The schema option accepts only `1.0` or `1.1`, once, as a separate argument after the command; malformed or incompatible selections exit 2 before requests. No environment variable changes the schema.

Schema 1.1 adds `previousCloseFeed` and `expectedDelaySeconds` to quote/normal doctor output, plus `excessAgeSeconds` and `isStaleBeyondExpectedDelay` per quote symbol. Raw `quoteAgeSeconds` and `isStale` keep their 1.0 meanings. Delayed SIP has a nominal 900-second delay: a 901-second-old trade has one excess second and is not stale beyond the default additional 900-second threshold, even though raw `isStale` is true. Neither age measure alone makes an observation an error or diagnoses a provider outage. Missing/invalid timestamps produce null age values. Existing schema 1.0 output has no new properties.

Delayed latest trades and doctor probes request `delayed_sip`; prior-session raw bars explicitly request historical `sip` outside its recent-data restriction. Responses identify this mapping; it is not fallback after a failed request. Feed selection does not establish account entitlement. Doctor without `--check-connection` remains offline; its connection probe tests latest trades only, not prior-close access. Home automation and its pinned schema 1.0 configuration require separate approval to change.

Malformed option tokens are excluded from quote-error symbol results and counts. For example, `market quote NVDA --feed --invalid --json` exits 2 and reports only `NVDA` in `symbols`, with `summary.requested` equal to 1.

```powershell
$env:ALPACA_API_KEY = "<your-key>"
$env:ALPACA_API_SECRET = "<your-secret>"
$env:ALPACA_DATA_FEED = "iex"
```

```bash
export ALPACA_API_KEY='<your-key>'
export ALPACA_API_SECRET='<your-secret>'
export ALPACA_DATA_FEED='iex'
```

## Commands

```text
market quote NVDA AMD AVGO
market quote RACK --json
market doctor --json
market doctor --check-connection --json
market quote RACK TCAI CLOU ARTY DTCR --feed sip --json
market doctor --feed iex --check-connection --json
market quote RACK TCAI CLOU ARTY DTCR --feed delayed_sip --schema-version 1.1 --json
market doctor --feed delayed_sip --schema-version 1.1 --json
market --help
market --version
```

`quote` outputs JSON by default; `--json` explicitly selects machine-readable output. A batch example:

```powershell
market quote RACK TCAI CLOU ARTY DTCR --json |
    Set-Content -Path prices.json
```

In JSON mode stdout contains only JSON; diagnostics are written to stderr. See [the JSON and error contract](specifications/phase-1-requirements.md#6-version-1-json-response) for semantics, session handling, and exit codes.

`quote` accepts up to 1,000 distinct symbols per request. `doctor --check-connection` makes an optional authenticated market-data probe using SPY and does not return or retain that probe's price.

If Alpaca's clock or calendar is unavailable, `quote` preserves available trades as partial results with an `unknown` market session and no previous close. Provider calls are not retried. Quote and doctor connection checks time out after 30 seconds; Ctrl+C cancels them with exit code `130`.

Daily change compares each trade with the regular-session close immediately before that trade's Eastern trading date. For example, Tuesday after-hours trades still compare against Monday's close if Monday was a trading day. The top-level `marketSession` describes the exchange around retrieval, not each trade's session.

## Build and test

```text
dotnet restore Market.slnx
dotnet build Market.slnx --configuration Release --no-restore
dotnet test Market.slnx --configuration Release --no-build
dotnet pack src/Market.Cli/Market.Cli.csproj --configuration Release --output artifacts/packages
```

Automated tests use mocked provider calls and do not require Alpaca credentials. A live provider check is optional and is never run automatically.
