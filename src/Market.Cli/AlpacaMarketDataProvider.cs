using Alpaca.Markets;

namespace Market.Cli;

internal sealed class AlpacaMarketDataProvider : IMarketDataProvider
{
    private const int MaxSymbolsPerRequest = 1000;

    private readonly IAlpacaDataClient _dataClient;
    private readonly IAlpacaTradingClient _tradingClient;

    public AlpacaMarketDataProvider(MarketSettings configuration)
    {
        var key = new SecretKey(configuration.ApiKey, configuration.ApiSecret);
        _dataClient = new MarketDataEnvironment(configuration.DataEndpoint).GetAlpacaDataClient(key);
        _tradingClient = Environments.Live.GetAlpacaTradingClient(key);
    }

    public async Task<ProviderSnapshot> GetSnapshotAsync(
        IReadOnlyList<string> symbols,
        MarketDataFeed feed,
        CancellationToken cancellationToken)
    {
        var clockTask = _tradingClient.GetClockAsync(cancellationToken);
        var tradesTask = GetLatestTradesAsync(symbols, feed, cancellationToken);
        await Task.WhenAll(clockTask, tradesTask).ConfigureAwait(false);

        var clock = await clockTask.ConfigureAwait(false);
        var trades = await tradesTask.ConfigureAwait(false);
        var clockTimestamp = MarketTime.AsUtc(clock.TimestampUtc);
        var currentDate = MarketTime.EasternDate(clockTimestamp);
        var calendar = await _tradingClient.ListIntervalCalendarAsync(
            new CalendarRequest(currentDate.AddDays(-14), currentDate.AddDays(1)),
            cancellationToken).ConfigureAwait(false);
        var sessions = calendar.Select(ToSessionTiming).ToArray();
        var marketSession = MarketSessionClassifier.Resolve(clockTimestamp, clock.IsOpen, sessions);

        var previousClose = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var completedSession = MarketSessionClassifier.PreviousCompletedSession(clockTimestamp, sessions);
        if (completedSession is not null)
        {
            previousClose = await GetPreviousClosesAsync(
                symbols,
                completedSession.Date,
                feed,
                cancellationToken).ConfigureAwait(false);
        }

        return new ProviderSnapshot(clockTimestamp, marketSession, trades, previousClose);
    }

    public async Task CheckConnectivityAsync(
        MarketDataFeed feed,
        CancellationToken cancellationToken)
    {
        await _dataClient.ListLatestTradesAsync(
            new LatestMarketDataListRequest(["SPY"]) { Feed = feed },
            cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _dataClient.Dispose();
        _tradingClient.Dispose();
    }

    private async Task<IReadOnlyDictionary<string, MarketTrade>> GetLatestTradesAsync(
        IReadOnlyList<string> symbols,
        MarketDataFeed feed,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, MarketTrade>(StringComparer.Ordinal);
        foreach (var chunk in symbols.Chunk(MaxSymbolsPerRequest))
        {
            var trades = await _dataClient.ListLatestTradesAsync(
                new LatestMarketDataListRequest(chunk) { Feed = feed },
                cancellationToken).ConfigureAwait(false);
            foreach (var (symbol, trade) in trades)
            {
                results[symbol] = new MarketTrade((decimal)trade.Price, MarketTime.AsUtc(trade.TimestampUtc));
            }
        }

        return results;
    }

    private async Task<Dictionary<string, decimal>> GetPreviousClosesAsync(
        IReadOnlyList<string> symbols,
        DateOnly sessionDate,
        MarketDataFeed feed,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var start = MarketTime.ToUtcDateTime(sessionDate);
        var end = MarketTime.ToUtcDateTime(sessionDate, nextDay: true);

        foreach (var chunk in symbols.Chunk(MaxSymbolsPerRequest))
        {
            var request = new HistoricalBarsRequest(chunk, start, end, BarTimeFrame.Day)
            {
                Adjustment = Adjustment.Nothing,
                Feed = feed
            };
            var page = await _dataClient.ListHistoricalBarsAsync(request, cancellationToken).ConfigureAwait(false);
            foreach (var bar in page.Items)
            {
                if (MarketTime.EasternDate(MarketTime.AsUtc(bar.TimeUtc)) == sessionDate)
                {
                    results[bar.Symbol] = (decimal)bar.Close;
                }
            }
        }

        return results;
    }

    private static SessionTiming ToSessionTiming(IIntervalCalendar calendar) =>
        new(
            DateOnly.FromDateTime(calendar.Trading.OpenEst.Date),
            calendar.Session.OpenEst.ToUniversalTime(),
            calendar.Trading.OpenEst.ToUniversalTime(),
            calendar.Trading.CloseEst.ToUniversalTime(),
            calendar.Session.CloseEst.ToUniversalTime());

    private sealed class MarketDataEnvironment(Uri dataEndpoint) : IEnvironment
    {
        private static IEnvironment Live => Environments.Live;

        public Uri AlpacaTradingApi => Live.AlpacaTradingApi;
        public Uri AlpacaDataApi => dataEndpoint;
        public Uri AlpacaStreamingApi => Live.AlpacaStreamingApi;
        public Uri AlpacaDataStreamingApi => Live.AlpacaDataStreamingApi;
        public Uri AlpacaCryptoStreamingApi => Live.AlpacaCryptoStreamingApi;
        public Uri AlpacaNewsStreamingApi => Live.AlpacaNewsStreamingApi;
        public Uri AlpacaOptionsStreamingApi => Live.AlpacaOptionsStreamingApi;
    }
}
