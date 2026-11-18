using System.Net.Sockets;
using System.Reflection;
using Alpaca.Markets;

namespace Market.Cli;

internal sealed class CliApplication(
    IReadOnlyDictionary<string, string?> environment,
    TextWriter output,
    TextWriter error,
    Func<MarketSettings, IMarketDataProvider> providerFactory,
    TimeProvider timeProvider)
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
        var symbols = SymbolParser.Parse(args.Where(argument => argument != "--json"));
        var configurationResult = MarketConfiguration.Load(environment);
        var selectedFeed = configurationResult.Feed?.ToString().ToLowerInvariant();
        if (symbols.Count == 0 || args.Any(argument => argument.StartsWith("--", StringComparison.Ordinal)
            && argument != "--json"))
        {
            var errorResult = new MarketError("INVALID_ARGUMENTS", "Supply one or more symbols; only --json is supported.");
            await WriteJsonAsync(QuoteService.CreateFailureResponse(
                symbols, errorResult, timeProvider.GetUtcNow(), selectedFeed)).ConfigureAwait(false);
            await error.WriteLineAsync($"market: {errorResult.Code}: {errorResult.Message}").ConfigureAwait(false);
            return 2;
        }

        if (symbols.Count > SymbolParser.MaximumSymbols)
        {
            var errorResult = new MarketError("TOO_MANY_SYMBOLS",
                $"A quote request may contain at most {SymbolParser.MaximumSymbols} distinct symbols.");
            await WriteJsonAsync(QuoteService.CreateFailureResponse(
                symbols, errorResult, timeProvider.GetUtcNow(), selectedFeed)).ConfigureAwait(false);
            await error.WriteLineAsync($"market: {errorResult.Code}: {errorResult.Message}").ConfigureAwait(false);
            return 2;
        }

        if (symbols.All(symbol => !symbol.IsValid))
        {
            var errorResult = new MarketError("INVALID_SYMBOL", "No symbol has a valid US stock or ETF ticker format.");
            await WriteJsonAsync(QuoteService.CreateFailureResponse(
                symbols, errorResult, timeProvider.GetUtcNow(), selectedFeed)).ConfigureAwait(false);
            await error.WriteLineAsync($"market: {errorResult.Code}: {errorResult.Message}").ConfigureAwait(false);
            return 2;
        }

        if (!configurationResult.IsValid)
        {
            var configError = configurationResult.Error!;
            var response = QuoteService.CreateFailureResponse(
                symbols,
                configError,
                timeProvider.GetUtcNow(),
                configurationResult.Feed?.ToString().ToLowerInvariant());
            await WriteJsonAsync(response).ConfigureAwait(false);
            await error.WriteLineAsync($"market: {configError.Code}: {configError.Message}").ConfigureAwait(false);
            return MarketErrorMapper.ExitCode(configError);
        }

        var configuration = configurationResult.Configuration!;
        using var timeoutSource = new CancellationTokenSource(RequestTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            using var provider = providerFactory(configuration);
            var service = new QuoteService(provider, configuration, timeProvider);
            var response = await service.GetQuotesAsync(symbols, linkedSource.Token).ConfigureAwait(false);
            await WriteJsonAsync(response).ConfigureAwait(false);
            foreach (var quote in response.Symbols.Where(quote => quote.Error is not null))
            {
                await error.WriteLineAsync($"market: {quote.Symbol} {quote.Error!.Code}: {quote.Error.Message}")
                    .ConfigureAwait(false);
            }

            return response.Summary.Failed > 0 || response.Symbols.Any(symbol => symbol.Status == "partial")
                ? 1
                : 0;
        }
        catch (RestClientErrorException exception)
        {
            return await WriteQuoteFailureAsync(
                symbols, MarketErrorMapper.FromProviderException(exception), configuration.Feed)
                .ConfigureAwait(false);
        }
        catch (RequestValidationException exception)
        {
            return await WriteQuoteFailureAsync(
                symbols, MarketErrorMapper.FromProviderException(exception), configuration.Feed)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            return await WriteQuoteFailureAsync(
                symbols, MarketErrorMapper.FromProviderException(exception), configuration.Feed)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            var timedOut = timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            return await WriteQuoteFailureAsync(
                symbols, MarketErrorMapper.FromProviderException(exception, timedOut), configuration.Feed)
                .ConfigureAwait(false);
        }
        catch (SocketException exception)
        {
            return await WriteQuoteFailureAsync(
                symbols, MarketErrorMapper.FromProviderException(exception), configuration.Feed)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            return await WriteQuoteFailureAsync(
                symbols, MarketErrorMapper.FromProviderException(exception), configuration.Feed)
                .ConfigureAwait(false);
        }
    }

    private async Task<int> RunDoctorAsync(string[] args, CancellationToken cancellationToken)
    {
        var json = args.Contains("--json", StringComparer.Ordinal);
        var checkConnection = args.Contains("--check-connection", StringComparer.Ordinal);
        if (args.Any(argument => argument.StartsWith("--", StringComparison.Ordinal)
            && argument is not "--json" and not "--check-connection")
            || args.Any(argument => !argument.StartsWith("--", StringComparison.Ordinal)))
        {
            return await WriteUsageErrorAsync("doctor supports only --json and --check-connection.", json)
                .ConfigureAwait(false);
        }

        var result = MarketConfiguration.Load(environment);
        var diagnostics = result.Diagnostics.ToList();
        if (result.IsValid && checkConnection)
        {
            using var timeoutSource = new CancellationTokenSource(RequestTimeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            try
            {
                using var provider = providerFactory(result.Configuration!);
                await provider.CheckConnectivityAsync(result.Configuration!.Feed, linkedSource.Token).ConfigureAwait(false);
                diagnostics.Add(new DoctorDiagnostic("marketDataAccess", "ok",
                    "Authenticated market-data access succeeded using the selected feed."));
            }
            catch (RestClientErrorException exception)
            {
                diagnostics.Add(ToDoctorFailure(MarketErrorMapper.FromProviderException(exception)));
            }
            catch (RequestValidationException exception)
            {
                diagnostics.Add(ToDoctorFailure(MarketErrorMapper.FromProviderException(exception)));
            }
            catch (HttpRequestException exception)
            {
                diagnostics.Add(ToDoctorFailure(MarketErrorMapper.FromProviderException(exception)));
            }
            catch (OperationCanceledException exception)
            {
                var timedOut = timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
                diagnostics.Add(ToDoctorFailure(MarketErrorMapper.FromProviderException(exception, timedOut)));
            }
            catch (SocketException exception)
            {
                diagnostics.Add(ToDoctorFailure(MarketErrorMapper.FromProviderException(exception)));
            }
            catch (TimeoutException exception)
            {
                diagnostics.Add(ToDoctorFailure(MarketErrorMapper.FromProviderException(exception)));
            }
        }

        var status = diagnostics.Any(diagnostic => diagnostic.Status == "error") ? "error" : "ok";
        if (json)
        {
            await WriteJsonAsync(new DoctorResponse(
                "1.0",
                "alpaca",
                result.Configuration?.Feed.ToString().ToLowerInvariant()
                    ?? result.Feed?.ToString().ToLowerInvariant(),
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

        return status == "ok" ? 0 : result.Configuration is null ? 3 : 4;
    }

    private async Task<int> WriteQuoteFailureAsync(
        IReadOnlyList<SymbolInput> symbols,
        MarketError marketError,
        MarketDataFeed feed)
    {
        var response = QuoteService.CreateFailureResponse(
            symbols, marketError, timeProvider.GetUtcNow(), feed.ToString().ToLowerInvariant());
        await WriteJsonAsync(response).ConfigureAwait(false);
        await error.WriteLineAsync($"market: {marketError.Code}: {marketError.Message}").ConfigureAwait(false);
        return MarketErrorMapper.ExitCode(marketError);
    }

    private async Task<int> WriteUsageErrorAsync(string message, bool json)
    {
        if (json)
        {
            await WriteJsonAsync(new
            {
                schemaVersion = "1.0",
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

    private static DoctorDiagnostic ToDoctorFailure(MarketError error) =>
        new("marketDataAccess", "error", $"{error.Code}: {error.Message}");

    private const string HelpText = """
        market - read-only Alpaca US stock and ETF market-data CLI

        Usage:
          market quote <SYMBOL> [SYMBOL ...] [--json]
          market doctor [--check-connection] [--json]
          market --help
          market --version

        quote emits versioned JSON by default. Configure ALPACA_API_KEY and
        ALPACA_API_SECRET in the process environment. ALPACA_DATA_FEED selects
        iex (default) or sip. No trading operations are available.
        """;
}
