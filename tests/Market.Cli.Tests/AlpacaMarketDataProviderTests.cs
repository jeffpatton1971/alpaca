using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Alpaca.Markets;
using Alpaca.Markets.Extensions;

namespace Market.Cli.Tests;

public sealed class AlpacaMarketDataProviderTests
{
    private const string Trades = """{"trades":{"NVDA":{"p":12,"t":"2026-06-10T14:59:55Z","s":1}}}""";
    private const string Clock = """
        {"timestamp":"2026-06-10T15:00:00Z","is_open":true,"next_open":"2026-06-11T13:30:00Z","next_close":"2026-06-10T20:00:00Z"}
        """;
    private const string Calendar = """
        [{"date":"2026-06-09","open":"09:30","close":"16:00","session_open":"0400","session_close":"2000"}]
        """;

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "RATE_LIMITED")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "PROVIDER_UNAVAILABLE")]
    public async Task BothSdkClientsSendFailedOperationOnlyOnce(HttpStatusCode status, string code)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var endpoint = new Uri($"http://{listener.LocalEndpoint}/");
        var data = AlpacaMarketDataProvider.CreateDataConfiguration(TestData.Settings);
        var trading = AlpacaMarketDataProvider.CreateTradingConfiguration(TestData.Settings);
        data.ApiEndpoint = endpoint;
        trading.ApiEndpoint = endpoint;
        Assert.NotSame(data.ThrottleParameters, trading.ThrottleParameters);
        Assert.NotSame(ThrottleParameters.Default, data.ThrottleParameters);
        Assert.NotSame(ThrottleParameters.Default, trading.ThrottleParameters);

        var calls = new List<string>();
        var server = ServeAsync();
        try
        {
            using var dataClient = data.GetClient();
            using var tradingClient = trading.GetClient();
            var dataError = await Assert.ThrowsAsync<RestClientErrorException>(() =>
                dataClient.ListLatestTradesAsync(new LatestMarketDataListRequest(["SPY"]), stop.Token));
            var clockError = await Assert.ThrowsAsync<RestClientErrorException>(() =>
                tradingClient.GetClockAsync(stop.Token));
            Assert.Equal(code, MarketErrorMapper.FromProviderException(dataError).Code);
            Assert.Equal(code, MarketErrorMapper.FromProviderException(clockError).Code);
        }
        finally
        {
            await stop.CancelAsync();
            await server;
        }
        Assert.Collection(calls,
            trade => Assert.Contains("/trades/latest?", trade),
            clock => Assert.Contains("/v2/clock ", clock));

        async Task ServeAsync()
        {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, leaveOpen: true);
                    calls.Add((await reader.ReadLineAsync(stop.Token))!);
                    while (await reader.ReadLineAsync(stop.Token) is { Length: > 0 })
                    {
                    }
                    var http = $"HTTP/1.1 {(int)status} Error\r\nContent-Length: 2\r\nRetry-After: 0\r\nConnection: close\r\n\r\n{{}}";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(http), stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
            }
        }
    }

    [Theory]
    [InlineData("/v2/clock")]
    [InlineData("/v2/calendar")]
    public async Task ContextFailureRetainsTradeAndProducesAccuratePartialJson(string failurePath)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var app = new CliApplication(TestData.Environment(), output, error,
            _ => Provider((uri, _) =>
            {
                Assert.EndsWith("/trades/latest", uri.AbsolutePath);
                return Response(Trades);
            }, (uri, _) => uri.AbsolutePath == failurePath
                ? Response("{}", HttpStatusCode.ServiceUnavailable)
                : TradingResponse(uri)), new FixedTimeProvider(TestData.Now));

        Assert.Equal(1, await app.RunAsync(["quote", "NVDA", "AMD"]));
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("unknown", root.GetProperty("marketSession").GetString());
        var trade = root.GetProperty("symbols")[0];
        Assert.Equal(12m, trade.GetProperty("price").GetDecimal());
        Assert.Equal("2026-06-10T14:59:55.0000000Z", trade.GetProperty("priceTimestampUtc").GetString());
        foreach (var field in new[] { "previousClose", "change", "changePercent" })
        {
            Assert.Equal(JsonValueKind.Null, trade.GetProperty(field).ValueKind);
        }
        Assert.Equal("partial", trade.GetProperty("status").GetString());
        Assert.Equal("error", root.GetProperty("symbols")[1].GetProperty("status").GetString());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("succeeded").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("failed").GetInt32());
    }

    [Theory]
    [InlineData("/v2/clock")]
    [InlineData("/v2/calendar")]
    public async Task ContextCancellationStillPropagates(string cancellationPath)
    {
        using var caller = new CancellationTokenSource();
        using var provider = Provider((_, _) => Response(Trades), (uri, token) =>
        {
            if (uri.AbsolutePath == cancellationPath)
            {
                caller.Cancel();
                token.ThrowIfCancellationRequested();
            }
            return TradingResponse(uri);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetSnapshotAsync(["NVDA"], MarketDataFeed.Iex, caller.Token));
    }

    [Fact]
    public async Task SinglePageFiltersOtherSymbolsAndDates()
    {
        var calls = 0;
        using var provider = Provider((uri, _) =>
        {
            if (uri.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal))
            {
                return Response(Trades);
            }
            calls++;
            Assert.Contains("feed=sip", uri.Query);
            Assert.Contains("adjustment=raw", uri.Query);
            return Bars(null, ("NVDA", "2026-06-09", 10), ("NVDA", "2026-06-10", 11), ("AMD", "2026-06-09", 20));
        });
        var result = await provider.GetSnapshotAsync(["NVDA", "MSFT"], MarketDataFeed.Sip, default);
        Assert.Equal(10m, result.PreviousCloses["NVDA"]);
        Assert.Single(result.PreviousCloses);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AllThousandClosesSurviveMultiplePagesIncludingAnEmptyPage()
    {
        var symbols = Enumerable.Range(0, 1000).Select(i => $"T{i:D4}").ToArray();
        var calls = 0;
        using var provider = Provider((uri, _) =>
        {
            if (uri.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal))
            {
                return Response("""{"trades":{}}""");
            }
            return ++calls switch
            {
                1 => Bars("opaque+/=", symbols.Take(500).Select(symbol => (symbol, "2026-06-09", 10m)).ToArray()),
                2 when Uri.UnescapeDataString(uri.Query).Contains("page_token=opaque+/=", StringComparison.Ordinal) =>
                    Bars("next"),
                3 when uri.Query.Contains("page_token=next", StringComparison.Ordinal) =>
                    Bars(null, symbols.Skip(499).Select(symbol => (symbol, "2026-06-09", 10m)).ToArray()),
                _ => throw new InvalidOperationException("Unexpected bars request.")
            };
        });
        var result = await provider.GetSnapshotAsync(symbols, MarketDataFeed.Iex, default);
        Assert.Equal(3, calls);
        Assert.Equal(1000, result.PreviousCloses.Count);
        Assert.All(symbols, symbol => Assert.Equal(10m, result.PreviousCloses[symbol]));
    }

    [Theory]
    [InlineData("repeat", 2)]
    [InlineData("", 1)]
    [InlineData("  ", 1)]
    public async Task InvalidOrRepeatedTokenFailsRatherThanReturningIncompleteCloses(string token, int expectedCalls)
    {
        var calls = 0;
        using var provider = Provider((uri, _) =>
        {
            if (uri.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal))
            {
                return Response(Trades);
            }
            calls++;
            return SingleSymbolBars(token);
        });
        var failure = await Assert.ThrowsAsync<UnexpectedProviderResponseException>(() =>
            provider.GetSnapshotAsync(["NVDA"], MarketDataFeed.Iex, default));
        Assert.Equal("UNEXPECTED_PROVIDER_RESPONSE", MarketErrorMapper.FromProviderException(failure).Code);
        Assert.Equal(expectedCalls, calls);
    }

    [Fact]
    public async Task CancellationPreventsFurtherPagination()
    {
        using var caller = new CancellationTokenSource();
        var calls = 0;
        using var provider = Provider((uri, _) =>
        {
            if (uri.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal))
            {
                return Response(Trades);
            }
            calls++;
            caller.Cancel();
            return SingleSymbolBars("next");
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetSnapshotAsync(["NVDA"], MarketDataFeed.Iex, caller.Token));
        Assert.Equal(1, calls);
    }

    internal static AlpacaMarketDataProvider Provider(
        Func<Uri, CancellationToken, HttpResponseMessage> data,
        Func<Uri, CancellationToken, HttpResponseMessage>? trading = null) =>
        new(AlpacaMarketDataProvider.CreateDataConfiguration(TestData.Settings)
                .WithHttpMessageHandlerFactory(inner => new StubHandler(inner, data)).GetClient(),
            AlpacaMarketDataProvider.CreateTradingConfiguration(TestData.Settings)
                .WithHttpMessageHandlerFactory(inner => new StubHandler(inner, trading ?? ((uri, _) => TradingResponse(uri))))
                .GetClient());

    internal static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body) };

    private static HttpResponseMessage TradingResponse(Uri uri) => uri.AbsolutePath switch
    {
        "/v2/clock" => Response(Clock),
        "/v2/calendar" => Response(Calendar),
        _ => throw new InvalidOperationException("Unexpected trading request.")
    };

    private static HttpResponseMessage Bars(string? token, params (string Symbol, string Date, decimal Close)[] entries) =>
        Response(JsonSerializer.Serialize(new
        {
            bars = entries.GroupBy(x => x.Symbol).ToDictionary(group => group.Key, group => group.Select(x => new
            {
                t = $"{x.Date}T04:00:00Z", o = x.Close, c = x.Close, h = x.Close, l = x.Close, v = 1
            })),
            next_page_token = token
        }));

    private static HttpResponseMessage SingleSymbolBars(string token) =>
        Response(JsonSerializer.Serialize(new
        {
            symbol = "NVDA",
            bars = new[] { new { t = "2026-06-09T04:00:00Z", o = 10, c = 10, h = 10, l = 10, v = 1 } },
            next_page_token = token
        }));

    private sealed class StubHandler(
        HttpMessageHandler inner,
        Func<Uri, CancellationToken, HttpResponseMessage> send) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(send(request.RequestUri!, cancellationToken));
        }
    }
}
