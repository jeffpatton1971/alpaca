using Alpaca.Markets;

namespace Market.Cli;

internal sealed class QuoteService(
    IMarketDataProvider provider,
    MarketSettings configuration,
    TimeProvider timeProvider)
{
    public async Task<QuoteResponse> GetQuotesAsync(
        IReadOnlyList<SymbolInput> symbols,
        CancellationToken cancellationToken)
    {
        var validSymbols = symbols.Where(symbol => symbol.IsValid).Select(symbol => symbol.Symbol).ToArray();
        ProviderSnapshot? snapshot = null;
        if (validSymbols.Length > 0)
        {
            snapshot = await provider.GetSnapshotAsync(validSymbols, configuration.Feed, cancellationToken)
                .ConfigureAwait(false);
        }

        var retrievedAt = timeProvider.GetUtcNow();
        var outputSymbols = symbols.Select(symbol => ToSymbolQuote(symbol, snapshot, retrievedAt)).ToArray();
        var succeeded = outputSymbols.Count(symbol => symbol.Status is "ok" or "partial");
        var failed = outputSymbols.Length - succeeded;
        return new QuoteResponse(
            "1.0",
            "alpaca",
            configuration.Feed.ToString().ToLowerInvariant(),
            MarketTime.ToUtcString(retrievedAt),
            "America/Chicago",
            snapshot?.Session switch
            {
                MarketSession.Regular => "regular",
                MarketSession.PreMarket => "pre_market",
                MarketSession.AfterHours => "after_hours",
                MarketSession.Closed => "closed",
                _ => "unknown"
            },
            outputSymbols,
            new QuoteSummary(outputSymbols.Length, succeeded, failed),
            null);
    }

    public static QuoteResponse CreateFailureResponse(
        IReadOnlyList<SymbolInput> symbols,
        MarketError error,
        DateTimeOffset retrievedAt,
        string? feed)
    {
        var results = symbols.Select(symbol => new SymbolQuote(
            symbol.Symbol,
            "USD",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "error",
            symbol.IsValid
                ? error
                : new MarketError("INVALID_SYMBOL", "The symbol format is invalid."))).ToArray();
        return new QuoteResponse(
            "1.0",
            "alpaca",
            feed,
            MarketTime.ToUtcString(retrievedAt),
            "America/Chicago",
            "unknown",
            results,
            new QuoteSummary(results.Length, 0, results.Length),
            error);
    }

    private SymbolQuote ToSymbolQuote(
        SymbolInput symbol,
        ProviderSnapshot? snapshot,
        DateTimeOffset retrievedAt)
    {
        if (!symbol.IsValid)
        {
            return Empty(symbol.Symbol, "error",
                new MarketError("INVALID_SYMBOL", "The symbol format is invalid."));
        }

        if (snapshot is null)
        {
            return Empty(symbol.Symbol, "error",
                new MarketError("MARKET_DATA_UNAVAILABLE", "Market data is unavailable."));
        }

        var hasTrade = snapshot.Trades.TryGetValue(symbol.Symbol, out var trade);
        var hasPreviousClose = snapshot.PreviousCloses.TryGetValue(symbol.Symbol, out var previousClose);
        var error = !hasTrade
            ? new MarketError("MARKET_DATA_UNAVAILABLE", "No eligible latest trade is available for this symbol.")
            : !hasPreviousClose
                ? new MarketError("PREVIOUS_CLOSE_UNAVAILABLE", "No daily bar is available for the previous completed regular session.")
                : previousClose == 0
                    ? new MarketError("PREVIOUS_CLOSE_ZERO", "The previous close is zero; daily change calculations are unavailable.")
                    : null;

        if (hasTrade && (trade!.TimestampUtc == default || trade.TimestampUtc > retrievedAt))
        {
            error = new MarketError("INVALID_OBSERVATION_TIMESTAMP",
                "The provider observation timestamp is unavailable or later than the retrieval time.");
            hasTrade = false;
            trade = null;
        }

        TimeSpan? age = hasTrade ? retrievedAt - trade!.TimestampUtc : null;
        decimal? price = hasTrade ? trade!.Price : null;
        decimal? change = hasTrade && hasPreviousClose && previousClose != 0
            ? trade!.Price - previousClose
            : null;
        decimal? changePercent = change.HasValue
            ? change.Value / previousClose * 100m
            : null;
        var status = error is null
            ? "ok"
            : hasTrade || hasPreviousClose
                ? "partial"
                : "error";
        return new SymbolQuote(
            symbol.Symbol,
            "USD",
            price,
            hasPreviousClose ? previousClose : null,
            change,
            changePercent,
            hasTrade ? "last_trade" : null,
            hasTrade ? MarketTime.ToUtcString(trade!.TimestampUtc) : null,
            hasTrade ? MarketTime.ToCentralString(trade!.TimestampUtc) : null,
            age.HasValue ? Math.Max(0, (long)Math.Floor(age.Value.TotalSeconds)) : null,
            age.HasValue ? age.Value > TimeSpan.FromMinutes(configuration.StaleAfterMinutes) : null,
            status,
            error);
    }

    private static SymbolQuote Empty(string symbol, string status, MarketError error) =>
        new(symbol, "USD", null, null, null, null, null, null, null, null, null, status, error);
}
