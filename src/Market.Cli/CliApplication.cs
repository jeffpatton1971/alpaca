using System.Reflection;
using Alpaca.Markets;

namespace Market.Cli;

internal sealed class CliApplication(
    IReadOnlyDictionary<string, string?> environment,
    TextWriter output,
    TextWriter error,
    Func<MarketSettings, IMarketDataProvider> providerFactory,
    TimeProvider timeProvider,
    TimeSpan? requestTimeout = null)
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
        {
            await output.WriteLineAsync(HelpText).ConfigureAwait(false);
            return 0;
        }

        if (args.Length == 1 && args[0] == "--version")
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
            await output.WriteLineAsync(version.ToString(3)).ConfigureAwait(false);
            return 0;
        }

        return args[0] switch
        {
            "quote" => await RunQuoteAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "doctor" => await RunDoctorAsync(args[1..], cancellationToken).ConfigureAwait(false),
            _ => await WriteUsageErrorAsync("Unknown command.", args.Contains("--json", StringComparer.Ordinal))
                .ConfigureAwait(false)
        };
    }

    private async Task<int> RunQuoteAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = InvocationOptions.Parse(args);
        args = options.Arguments;
        // Retain options in args for validation, but never serialize them as symbols on failure.
        var symbols = SymbolParser.Parse(args.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal)));
        var configurationResult = options.FeedError is null
            ? MarketConfiguration.Load(environment, options.FeedOverride)
            : null;
        var selectedFeed = FeedNames.Canonical(configurationResult?.Feed);
        var selectionError = options.FeedError ?? options.SchemaError;
        if (selectedFeed == "delayed_sip" && options.SchemaVersion != "1.1")
        {
            selectionError ??= new MarketError("INVALID_ARGUMENTS", InvocationOptions.DelayedRequirement);
            selectedFeed = null;
        }
        if (selectionError is not null || symbols.Count == 0
            || args.Any(argument => argument.StartsWith("--", StringComparison.Ordinal)
            && argument != "--json"))
        {
            var errorResult = selectionError ?? new MarketError("INVALID_ARGUMENTS",
                "Supply one or more symbols; supported options are --json, --feed iex|sip|delayed_sip, and --schema-version 1.0|1.1.");
            await WriteQuoteJsonAsync(QuoteService.CreateFailureResponse(
                symbols, errorResult, timeProvider.GetUtcNow(), selectedFeed), options.SchemaVersion).ConfigureAwait(false);
            await error.WriteLineAsync($"market: {errorResult.Code}: {errorResult.Message}").ConfigureAwait(false);
            return 2;
        }

        if (symbols.Count > SymbolParser.MaximumSymbols)
        {
            var errorResult = new MarketError("TOO_MANY_SYMBOLS",
                $"A quote request may contain at most {SymbolParser.MaximumSymbols} distinct symbols.");
            await WriteQuoteJsonAsync(QuoteService.CreateFailureResponse(
                symbols, errorResult, timeProvider.GetUtcNow(), selectedFeed), options.SchemaVersion).ConfigureAwait(false);
            await error.WriteLineAsync($"market: {errorResult.Code}: {errorResult.Message}").ConfigureAwait(false);
            return 2;
        }

        if (symbols.All(symbol => !symbol.IsValid))
        {
            var errorResult = new MarketError("INVALID_SYMBOL", "No symbol has a valid US stock or ETF ticker format.");
            await WriteQuoteJsonAsync(QuoteService.CreateFailureResponse(
                symbols, errorResult, timeProvider.GetUtcNow(), selectedFeed), options.SchemaVersion).ConfigureAwait(false);
            await error.WriteLineAsync($"market: {errorResult.Code}: {errorResult.Message}").ConfigureAwait(false);
            return 2;
        }

        if (!configurationResult!.IsValid)
        {
            var configError = configurationResult.Error!;
            var response = QuoteService.CreateFailureResponse(
                symbols,
                configError,
                timeProvider.GetUtcNow(),
                FeedNames.Canonical(configurationResult.Feed));
            await WriteQuoteJsonAsync(response, options.SchemaVersion).ConfigureAwait(false);
            await error.WriteLineAsync($"market: {configError.Code}: {configError.Message}").ConfigureAwait(false);
            return MarketErrorMapper.ExitCode(configError);
        }

        var configuration = configurationResult.Configuration!;
        using var timeoutSource = new CancellationTokenSource(requestTimeout ?? RequestTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            var response = await RunProviderAsync(configuration, (provider, token) =>
                new QuoteService(provider, configuration, timeProvider).GetQuotesAsync(symbols, token),
                linkedSource.Token).ConfigureAwait(false);
            await WriteQuoteJsonAsync(response, options.SchemaVersion).ConfigureAwait(false);
            foreach (var quote in response.Symbols.Where(quote => quote.Error is not null))
            {
                await error.WriteLineAsync($"market: {quote.Symbol} {quote.Error!.Code}: {quote.Error.Message}")
                    .ConfigureAwait(false);
            }

            return response.Summary.Failed > 0 || response.Symbols.Any(symbol => symbol.Status == "partial")
                ? 1
                : 0;
        }
        catch (Exception exception) when (IsOperationFailure(exception))
        {
            return await WriteQuoteFailureAsync(
                symbols, MapOperationFailure(exception, cancellationToken, timeoutSource.Token), configuration.Feed,
                options.SchemaVersion)
                .ConfigureAwait(false);
        }
    }

    private async Task<int> RunDoctorAsync(string[] args, CancellationToken cancellationToken)
    {
        var json = args.Contains("--json", StringComparer.Ordinal);
        var options = InvocationOptions.Parse(args);
        if ((options.FeedError ?? options.SchemaError) is { } selectionError)
        {
            return await WriteUsageErrorAsync(selectionError.Message, json, options.SchemaVersion).ConfigureAwait(false);
        }

        args = options.Arguments;
        var checkConnection = args.Contains("--check-connection", StringComparer.Ordinal);
        if (args.Any(argument => argument.StartsWith("--", StringComparison.Ordinal)
            && argument is not "--json" and not "--check-connection")
            || args.Any(argument => !argument.StartsWith("--", StringComparison.Ordinal)))
        {
            return await WriteUsageErrorAsync(
                "doctor supports only --json, --check-connection, --feed iex|sip|delayed_sip, and --schema-version 1.0|1.1.",
                json, options.SchemaVersion)
                .ConfigureAwait(false);
        }

        var result = MarketConfiguration.Load(environment, options.FeedOverride);
        if (result.Feed == MarketDataFeed.DelayedSip && options.SchemaVersion != "1.1")
            return await WriteUsageErrorAsync(InvocationOptions.DelayedRequirement, json, options.SchemaVersion)
                .ConfigureAwait(false);
        var diagnostics = result.Diagnostics.ToList();
        MarketError? connectionError = null;
        if (result.IsValid && checkConnection)
        {
            using var timeoutSource = new CancellationTokenSource(requestTimeout ?? RequestTimeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            try
            {
                var configuration = result.Configuration!;
                await RunProviderAsync(configuration, async (provider, token) =>
                {
                    await provider.CheckConnectivityAsync(configuration.Feed, token).ConfigureAwait(false);
                    return true;
                }, linkedSource.Token).ConfigureAwait(false);
                diagnostics.Add(new DoctorDiagnostic("marketDataAccess", "ok",
                    "Authenticated market-data access succeeded using the selected feed."));
            }
            catch (Exception exception) when (IsOperationFailure(exception))
            {
                connectionError = MapOperationFailure(exception, cancellationToken, timeoutSource.Token);
                diagnostics.Add(ToDoctorFailure(connectionError));
            }
        }

        var status = diagnostics.Any(diagnostic => diagnostic.Status == "error") ? "error" : "ok";
        if (json)
        {
            await WriteJsonAsync(new DoctorResponse(
                options.SchemaVersion,
                "alpaca",
                FeedNames.Canonical(result.Feed),
                status,
                diagnostics)).ConfigureAwait(false);
            if (status == "error")
            {
                await error.WriteLineAsync("market: DOCTOR_FAILED: One or more configuration or access checks failed.")
                    .ConfigureAwait(false);
            }
        }
        else
        {
            await output.WriteLineAsync($"Alpaca configuration: {status}").ConfigureAwait(false);
            foreach (var diagnostic in diagnostics)
            {
                await output.WriteLineAsync($"{diagnostic.Status,-5} {diagnostic.Check}: {diagnostic.Message}")
                    .ConfigureAwait(false);
            }
        }

        return connectionError is not null ? MarketErrorMapper.ExitCode(connectionError)
            : result.Error is not null ? MarketErrorMapper.ExitCode(result.Error) : 0;
    }

    private async Task<T> RunProviderAsync<T>(
        MarketSettings configuration,
        Func<IMarketDataProvider, CancellationToken, Task<T>> operation,
        CancellationToken token)
    {
        // The SDK can read the response body synchronously without a token.
        // The worker owns the provider and never writes command output.
        var task = Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested();
            using var provider = providerFactory(configuration);
            return await operation(provider, token).ConfigureAwait(false);
        }, token);
        try
        {
            var result = await task.WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _ = task.ContinueWith(static faulted => { _ = faulted.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    private static bool IsOperationFailure(Exception exception) =>
        exception is OperationCanceledException || MarketErrorMapper.IsProviderFailure(exception);

    private static MarketError MapOperationFailure(
        Exception exception, CancellationToken callerToken, CancellationToken timeoutToken) =>
        callerToken.IsCancellationRequested
            ? MarketErrorMapper.FromProviderException(new OperationCanceledException(callerToken))
            : MarketErrorMapper.FromProviderException(exception,
                timedOut: timeoutToken.IsCancellationRequested || exception is OperationCanceledException);

    private async Task<int> WriteQuoteFailureAsync(
        IReadOnlyList<SymbolInput> symbols,
        MarketError marketError,
        MarketDataFeed feed,
        string schemaVersion)
    {
        var response = QuoteService.CreateFailureResponse(
            symbols, marketError, timeProvider.GetUtcNow(), FeedNames.Canonical(feed));
        await WriteQuoteJsonAsync(response, schemaVersion).ConfigureAwait(false);
        await error.WriteLineAsync($"market: {marketError.Code}: {marketError.Message}").ConfigureAwait(false);
        return MarketErrorMapper.ExitCode(marketError);
    }

    private async Task<int> WriteUsageErrorAsync(string message, bool json, string version = "1.0")
    {
        if (json)
        {
            await WriteJsonAsync(new
            {
                schemaVersion = version,
                provider = "alpaca",
                error = new MarketError("INVALID_ARGUMENTS", message)
            }).ConfigureAwait(false);
        }
        else
        {
            await error.WriteLineAsync($"market: {message} Use 'market --help' for usage.").ConfigureAwait(false);
        }

        return 2;
    }

    private async Task WriteJsonAsync<T>(T value) =>
        await output.WriteLineAsync(JsonOutput.Serialize(value)).ConfigureAwait(false);

    private Task WriteQuoteJsonAsync(QuoteResponse response, string version) =>
        WriteJsonAsync(response with { SchemaVersion = version });

    private static DoctorDiagnostic ToDoctorFailure(MarketError error) =>
        new("marketDataAccess", "error", $"{error.Code}: {error.Message}");

    private const string HelpText = """
        market - read-only Alpaca US stock and ETF market-data CLI

        Usage:
          market quote <SYMBOL> [SYMBOL ...] [--feed iex|sip|delayed_sip] [--schema-version 1.0|1.1] [--json]
          market doctor [--feed iex|sip|delayed_sip] [--schema-version 1.0|1.1] [--check-connection] [--json]
          market --help
          market --version

        quote emits versioned JSON by default. Configure ALPACA_API_KEY and
        ALPACA_API_SECRET in the process environment. Feed precedence is
        --feed, then ALPACA_DATA_FEED, then iex. Feed values are
        case-insensitive. Use --feed once, after the command name; it affects
        only this invocation. Schema defaults to 1.0; delayed_sip requires
        --schema-version 1.1. Schema 1.1 adds nominal delay and excess age;
        raw age and isStale retain their meanings. Delayed latest trades use
        historical SIP for prior closes. Doctor probes latest-trade access only.
        No trading operations are available.
        """;
}
