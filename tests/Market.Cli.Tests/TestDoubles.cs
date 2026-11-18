using Alpaca.Markets;

namespace Market.Cli.Tests;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class FakeMarketDataProvider : IMarketDataProvider
{
    public ProviderSnapshot? Snapshot { get; set; }
    public Exception? Failure { get; set; }
    public IReadOnlyList<string> RequestedSymbols { get; private set; } = [];
    public MarketDataFeed RequestedFeed { get; private set; }
    public bool ConnectivityChecked { get; private set; }

    public Task<ProviderSnapshot> GetSnapshotAsync(
        IReadOnlyList<string> symbols,
        MarketDataFeed feed,
        CancellationToken cancellationToken)
    {
        RequestedSymbols = symbols;
        RequestedFeed = feed;
        if (Failure is not null)
        {
            throw Failure;
        }

        return Task.FromResult(Snapshot
            ?? throw new InvalidOperationException("The test provider has no snapshot."));
    }

    public Task CheckConnectivityAsync(
        MarketDataFeed feed,
        CancellationToken cancellationToken)
    {
        RequestedFeed = feed;
        ConnectivityChecked = true;
        if (Failure is not null)
        {
            throw Failure;
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 6, 10, 15, 0, 0, TimeSpan.Zero);
    public static readonly MarketSettings Settings = new(
        "synthetic-key",
        "synthetic-secret",
        Environments.Live.AlpacaDataApi,
        MarketDataFeed.Iex,
        15);

    public static IReadOnlyDictionary<string, string?> Environment(
        string feed = "iex",
        string? endpoint = null,
        string? staleAfter = null) =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ALPACA_API_KEY"] = "synthetic-key",
            ["ALPACA_API_SECRET"] = "synthetic-secret",
            ["ALPACA_DATA_FEED"] = feed,
            ["ALPACA_DATA_URL"] = endpoint,
            ["MARKET_STALE_AFTER_MINUTES"] = staleAfter
        };
}
