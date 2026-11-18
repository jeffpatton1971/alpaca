using Alpaca.Markets;
using Alpaca.Markets.Extensions;

namespace Market.Cli;

internal sealed class AlpacaMarketDataProvider : IMarketDataProvider
{
    private const int MaxSymbolsPerRequest = 1000;
    private const int MaxPagesPerBatch = 2048;
    private const int MaxPageTokenLength = 4096;

    private readonly IAlpacaDataClient _dataClient;
    private readonly IAlpacaTradingClient _tradingClient;

    public AlpacaMarketDataProvider(MarketSettings configuration)
        : this(CreateDataConfiguration(configuration).GetClient(),
            CreateTradingConfiguration(configuration).GetClient())
    {
    }

    internal AlpacaMarketDataProvider(IAlpacaDataClient dataClient, IAlpacaTradingClient tradingClient)
    {
        _dataClient = dataClient;
        _tradingClient = tradingClient;
    }

    internal static AlpacaDataClientConfiguration CreateDataConfiguration(MarketSettings settings) =>
        WithoutRetries(new MarketDataEnvironment(settings.DataEndpoint)
            .GetAlpacaDataClientConfiguration(new SecretKey(settings.ApiKey, settings.ApiSecret)));

    internal static AlpacaTradingClientConfiguration CreateTradingConfiguration(MarketSettings settings) =>
        WithoutRetries(Environments.Live
            .GetAlpacaTradingClientConfiguration(new SecretKey(settings.ApiKey, settings.ApiSecret)));

    private static T WithoutRetries<T>(T configuration) where T : AlpacaClientConfigurationBase
    {
        configuration.ThrottleParameters = new ThrottleParameters(0, [], []);
        return configuration;
    }

    public async Task<ProviderSnapshot> GetSnapshotAsync(
        IReadOnlyList<string> symbols,
        MarketDataFeed feed,
        CancellationToken cancellationToken)
    {
        var trades = await GetLatestTradesAsync(symbols, feed, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset clockTimestamp = default;
        MarketSession marketSession;
        SessionTiming? completedSession;
        try
        {
            var clock = await _tradingClient.GetClockAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            clockTimestamp = MarketTime.AsUtc(clock.TimestampUtc);
            var currentDate = MarketTime.EasternDate(clockTimestamp);
            var calendar = await _tradingClient.ListIntervalCalendarAsync(
                new CalendarRequest(currentDate.AddDays(-14), currentDate.AddDays(1)),
                cancellationToken).ConfigureAwait(false);
            var sessions = calendar.Select(ToSessionTiming).ToArray();
            marketSession = MarketSessionClassifier.Resolve(clockTimestamp, clock.IsOpen, sessions);
            completedSession = MarketSessionClassifier.PreviousCompletedSession(clockTimestamp, sessions);
        }
        catch (Exception exception) when (MarketErrorMapper.IsProviderFailure(exception))
        {
            marketSession = MarketSession.Unknown;
            completedSession = null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var previousClose = new Dictionary<string, decimal>(StringComparer.Ordinal);
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
            cancellationToken.ThrowIfCancellationRequested();
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
            var requested = chunk.ToHashSet(StringComparer.Ordinal);
            var seenTokens = new HashSet<string>(StringComparer.Ordinal);
            for (var pageNumber = 1; ; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = await _dataClient.ListHistoricalBarsAsync(request, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var bar in page.Items)
                {
                    if (requested.Contains(bar.Symbol)
                        && MarketTime.EasternDate(MarketTime.AsUtc(bar.TimeUtc)) == sessionDate)
                    {
                        var close = (decimal)bar.Close;
                        if (!results.TryAdd(bar.Symbol, close) && results[bar.Symbol] != close)
                        {
                            throw new UnexpectedProviderResponseException();
                        }
                    }
                }

                if (page.NextPageToken is not { } token)
                {
                    break;
                }

                if (pageNumber >= MaxPagesPerBatch || token.Length > MaxPageTokenLength
                    || string.IsNullOrWhiteSpace(token) || token.Any(char.IsControl) || !seenTokens.Add(token))
                {
                    throw new UnexpectedProviderResponseException();
                }

                request.Pagination.Token = token;
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
