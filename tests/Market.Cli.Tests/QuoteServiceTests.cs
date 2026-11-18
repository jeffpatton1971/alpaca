using System.Text.Json;
using Alpaca.Markets;

namespace Market.Cli.Tests;

public sealed class QuoteServiceTests
{
    [Fact]
    public async Task CalculatesDailyChangeFromLastTradeAndRawPreviousClose()
    {
        var provider = Provider(
            ("NVDA", 12.5m, TestData.Now.AddSeconds(-5)),
            ("NVDA", 10m));
        var service = new QuoteService(provider, TestData.Settings, new FixedTimeProvider(TestData.Now));

        var response = await service.GetQuotesAsync(SymbolParser.Parse(["NVDA"]), CancellationToken.None);
        var quote = Assert.Single(response.Symbols);

        Assert.Equal(12.5m, quote.Price);
        Assert.Equal(10m, quote.PreviousClose);
        Assert.Equal(2.5m, quote.Change);
        Assert.Equal(25m, quote.ChangePercent);
        Assert.Equal("last_trade", quote.PriceType);
        Assert.Equal(5, quote.QuoteAgeSeconds);
        Assert.False(quote.IsStale);
        Assert.Equal("ok", quote.Status);
    }

    [Fact]
    public async Task CalculatesNegativeChangeUsingDecimalArithmetic()
    {
        var provider = Provider(("NVDA", 8.5m, TestData.Now), ("NVDA", 10m));
        var service = new QuoteService(provider, TestData.Settings, new FixedTimeProvider(TestData.Now));

        var quote = Assert.Single((await service.GetQuotesAsync(
            SymbolParser.Parse(["NVDA"]), CancellationToken.None)).Symbols);

        Assert.Equal(-1.5m, quote.Change);
        Assert.Equal(-15m, quote.ChangePercent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingOrZeroPreviousCloseProducesNullCalculations(bool includeClose)
    {
        var previousCloses = includeClose
            ? new Dictionary<string, decimal> { ["NVDA"] = 0m }
            : new Dictionary<string, decimal>();
        var provider = new FakeMarketDataProvider
        {
            Snapshot = new ProviderSnapshot(
                TestData.Now,
                MarketSession.Regular,
                new Dictionary<string, MarketTrade>
                {
                    ["NVDA"] = new MarketTrade(12m, TestData.Now)
                },
                previousCloses)
        };
        var service = new QuoteService(provider, TestData.Settings, new FixedTimeProvider(TestData.Now));

        var quote = Assert.Single((await service.GetQuotesAsync(
            SymbolParser.Parse(["NVDA"]), CancellationToken.None)).Symbols);

        Assert.Null(quote.Change);
        Assert.Null(quote.ChangePercent);
        Assert.Equal("partial", quote.Status);
        Assert.Equal(includeClose ? "PREVIOUS_CLOSE_ZERO" : "PREVIOUS_CLOSE_UNAVAILABLE", quote.Error!.Code);
    }

    [Fact]
    public async Task PreservesOrderAndReturnsPartialResultsForMissingAndInvalidSymbols()
    {
        var provider = Provider(("AMD", 11m, TestData.Now), ("AMD", 10m));
        provider.Snapshot = provider.Snapshot! with
        {
            PreviousCloses = new Dictionary<string, decimal>
            {
                ["AMD"] = 10m,
                ["MISSING"] = 8m
            }
        };
        var service = new QuoteService(provider, TestData.Settings, new FixedTimeProvider(TestData.Now));

        var symbols = SymbolParser.Parse(["AMD", "bad symbol", "MISSING", "AMD"]);
        var response = await service.GetQuotesAsync(symbols, CancellationToken.None);

        Assert.Equal(["AMD", "BAD SYMBOL", "MISSING"], response.Symbols.Select(quote => quote.Symbol));
        Assert.Equal(["AMD", "MISSING"], provider.RequestedSymbols);
        Assert.Equal(new QuoteSummary(3, 2, 1), response.Summary);
        Assert.Equal("ok", response.Symbols[0].Status);
        Assert.Equal("error", response.Symbols[1].Status);
        Assert.Equal("partial", response.Symbols[2].Status);
        Assert.Equal("INVALID_SYMBOL", response.Symbols[1].Error!.Code);
        Assert.Equal("MARKET_DATA_UNAVAILABLE", response.Symbols[2].Error!.Code);
    }

    [Fact]
    public async Task MarksOldTradesStaleWithoutCallingThemCurrent()
    {
        var provider = Provider(("NVDA", 12m, TestData.Now.AddMinutes(-16)), ("NVDA", 10m));
        var service = new QuoteService(provider, TestData.Settings, new FixedTimeProvider(TestData.Now));

        var quote = Assert.Single((await service.GetQuotesAsync(
            SymbolParser.Parse(["NVDA"]), CancellationToken.None)).Symbols);

        Assert.True(quote.IsStale);
        Assert.Equal(960, quote.QuoteAgeSeconds);
        Assert.Equal("ok", quote.Status);
    }

    [Fact]
    public async Task RejectsFutureObservationTimestamp()
    {
        var provider = Provider(("NVDA", 12m, TestData.Now.AddSeconds(1)), ("NVDA", 10m));
        var service = new QuoteService(provider, TestData.Settings, new FixedTimeProvider(TestData.Now));

        var quote = Assert.Single((await service.GetQuotesAsync(
            SymbolParser.Parse(["NVDA"]), CancellationToken.None)).Symbols);

        Assert.Null(quote.Price);
        Assert.Equal("INVALID_OBSERVATION_TIMESTAMP", quote.Error!.Code);
    }

    [Fact]
    public async Task SerializesStableVersionedJsonWithExplicitNullsAndCentralOffset()
    {
        var provider = Provider(("NVDA", 12m, TestData.Now), ("NVDA", 10m));
        var service = new QuoteService(provider, TestData.Settings, new FixedTimeProvider(TestData.Now));

        var response = await service.GetQuotesAsync(
            SymbolParser.Parse(["NVDA"]), CancellationToken.None);
        using var document = JsonDocument.Parse(JsonOutput.Serialize(response));
        var root = document.RootElement;
        var quote = root.GetProperty("symbols")[0];

        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("alpaca", root.GetProperty("provider").GetString());
        Assert.Equal("iex", root.GetProperty("feed").GetString());
        Assert.Equal("America/Chicago", root.GetProperty("timeZone").GetString());
        Assert.Equal("2026-06-10T10:00:00.0000000-05:00", quote.GetProperty("priceTimestampCentral").GetString());
        Assert.True(root.GetProperty("error").ValueKind == JsonValueKind.Null);
        Assert.True(quote.GetProperty("error").ValueKind == JsonValueKind.Null);
    }

    [Theory]
    [InlineData("2024-03-10T08:30:00+00:00", "2024-03-10T03:30:00.0000000-05:00")]
    [InlineData("2024-11-03T08:30:00+00:00", "2024-11-03T02:30:00.0000000-06:00")]
    public void ConvertsCentralTimeAcrossDaylightSavingTransitions(string utc, string central)
    {
        var timestamp = DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(central, MarketTime.ToCentralString(timestamp));
    }

    private static FakeMarketDataProvider Provider(
        (string Symbol, decimal Price, DateTimeOffset TimestampUtc) trade,
        (string Symbol, decimal Close) close)
    {
        var trades = new Dictionary<string, MarketTrade>
        {
            [trade.Symbol] = new MarketTrade(trade.Price, trade.TimestampUtc)
        };
        var previousCloses = new Dictionary<string, decimal>
        {
            [close.Symbol] = close.Close
        };
        return new FakeMarketDataProvider
        {
            Snapshot = new ProviderSnapshot(
                TestData.Now,
                MarketSession.Regular,
                trades,
                previousCloses)
        };
    }
}
