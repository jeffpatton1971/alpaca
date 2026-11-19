using System.Net;
using System.Text.Json;
using Alpaca.Markets;

namespace Market.Cli.Tests;

public sealed class FeedOptionTests
{
    public static IEnumerable<object?[]> SelectionCases()
    {
        (string? Environment, string? Override, string Expected)[] cases =
        [
            (null, null, "iex"), ("", null, "iex"), (" \t", null, "iex"),
            ("iex", null, "iex"), ("SIP", null, "sip"),
            ("iex", "SiP", "sip"), ("sip", "IEX", "iex"),
            (null, "sip", "sip"), ("invalid", "sip", "sip"),
            ("delayed_sip", "iex", "iex")
        ];
        foreach (var doctor in new[] { false, true })
        foreach (var item in cases)
        {
            yield return [doctor, item.Environment, item.Override, item.Expected];
        }
    }

    [Theory]
    [MemberData(nameof(SelectionCases))]
    public async Task BothCommandsResolvePrecedenceWithoutMutatingConfiguration(
        bool doctor, string? environmentFeed, string? cliFeed, string expected)
    {
        var environment = TestData.Environment().ToDictionary();
        if (environmentFeed is null) environment.Remove("ALPACA_DATA_FEED");
        else environment["ALPACA_DATA_FEED"] = environmentFeed;
        var original = environment.ToArray();
        var processFeed = Environment.GetEnvironmentVariable("ALPACA_DATA_FEED");
        var provider = Provider();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var app = Application(environment, output, error, provider);
        var args = doctor ? new List<string> { "doctor", "--check-connection", "--json" }
            : ["quote", "NVDA", "--json"];
        if (cliFeed is not null) args.AddRange(["--feed", cliFeed]);

        Assert.Equal(0, await app.RunAsync(args.ToArray()));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("1.0", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(expected, json.RootElement.GetProperty("feed").GetString());
        Assert.Equal(expected == "iex" ? MarketDataFeed.Iex : MarketDataFeed.Sip, provider.RequestedFeed);
        Assert.Equal(doctor, provider.ConnectivityChecked);
        Assert.Equal(original, environment.ToArray());
        Assert.True(processFeed == Environment.GetEnvironmentVariable("ALPACA_DATA_FEED"));
        Assert.Empty(error.ToString());
    }

    public static IEnumerable<object[]> InvalidOptions()
    {
        string[][] options =
        [
            ["--feed"], ["--feed", "--json"], ["--feed", ""], ["--feed", " "],
            ["--feed", " sip"], ["--feed", "sip "], ["--feed", "delayed_sip"],
            ["--feed", "otc"], ["--feed", "boats"], ["--feed", "auto"],
            ["--feed", "0"], ["--feed", "Iex,Sip"],
            ["--feed", "iex", "--feed", "iex"], ["--feed", "iex", "--feed", "sip"],
            ["--feed", "--feed", "sip"], ["--feed=sip"], ["--feed="],
            ["--feed", "synthetic-private-value"], ["--feed=synthetic-private-value"],
            ["--feed", "--synthetic-private-value"],
            ["--feed", "--source=synthetic-private-value"],
            ["--feed=--synthetic-private-value"],
            ["--feed=--source=synthetic-private-value"],
            ["--feed", "--json", "--source=synthetic-private-value"],
            ["--feed", "--check-connection"]
        ];
        foreach (var doctor in new[] { false, true })
        foreach (var args in options) yield return [doctor, args];
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public async Task InvalidOptionsAreSafeUsageErrorsBeforeConfigurationOrProvider(
        bool doctor, string[] options)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var app = new CliApplication(new Dictionary<string, string?>(), output, error,
            _ => throw new InvalidOperationException("Provider must not be created."), new FixedTimeProvider(TestData.Now));
        string[] command = doctor ? ["doctor", "--json"] : ["quote", "NVDA"];

        Assert.Equal(2, await app.RunAsync([.. command, .. options]));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("1.0", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("INVALID_ARGUMENTS", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        if (!doctor)
        {
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("feed").ValueKind);
            var symbol = Assert.Single(json.RootElement.GetProperty("symbols").EnumerateArray());
            Assert.Equal("NVDA", symbol.GetProperty("symbol").GetString());
            var summary = json.RootElement.GetProperty("summary");
            Assert.Equal(1, summary.GetProperty("requested").GetInt32());
            Assert.Equal(0, summary.GetProperty("succeeded").GetInt32());
            Assert.Equal(1, summary.GetProperty("failed").GetInt32());
        }
        Assert.DoesNotContain("synthetic-private-value", (output.ToString() + error).ToLowerInvariant());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FilteringOptionTokensDoesNotSilentlyAcceptUnknownOptions(bool overrideFeed)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var app = new CliApplication(TestData.Environment(), output, error,
            _ => throw new InvalidOperationException("Provider must not be created."), new FixedTimeProvider(TestData.Now));
        string[] args = ["quote", "NVDA", "--source=synthetic-private-value", "--json"];
        if (overrideFeed) args = [.. args, "--feed", "sip"];

        Assert.Equal(2, await app.RunAsync(args));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("INVALID_ARGUMENTS", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(overrideFeed ? "sip" : "iex", json.RootElement.GetProperty("feed").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("requested").GetInt32());
        Assert.Equal("NVDA", Assert.Single(json.RootElement.GetProperty("symbols").EnumerateArray())
            .GetProperty("symbol").GetString());
        Assert.DoesNotContain("synthetic-private-value", (output.ToString() + error).ToLowerInvariant());
    }

    [Theory]
    [InlineData("delayedsip")]
    [InlineData("auto")]
    [InlineData(" sip ")]
    public async Task InvalidEnvironmentRetainsConfigurationFailure(string feed)
    {
        foreach (var command in new[] { new[] { "quote", "NVDA" }, new[] { "doctor", "--json", "--check-connection" } })
        {
            using var output = new StringWriter();
            var app = new CliApplication(TestData.Environment(feed), output, TextWriter.Null,
                _ => throw new InvalidOperationException("Provider must not be created."), TimeProvider.System);
            Assert.Equal(3, await app.RunAsync(command));
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("feed").ValueKind);
            if (command[0] == "quote")
                Assert.Equal("INVALID_CONFIGURATION", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
    }

    [Theory]
    [InlineData("--feed", "sip", "NVDA", "--json")]
    [InlineData("NVDA", "--feed", "sip", "--json")]
    [InlineData("--json", "NVDA", "--feed", "sip")]
    public async Task QuoteOrderingPreservesV1Response(params string[] args)
    {
        using var expected = new StringWriter();
        using var actual = new StringWriter();
        Assert.Equal(0, await Application(TestData.Environment("sip"), expected, TextWriter.Null, Provider())
            .RunAsync(["quote", "NVDA", "--json"]));
        Assert.Equal(0, await Application(TestData.Environment("iex"), actual, TextWriter.Null, Provider())
            .RunAsync(["quote", .. args]));
        Assert.Equal(expected.ToString(), actual.ToString());
    }

    [Theory]
    [InlineData("--feed", "sip", "--json", "--check-connection")]
    [InlineData("--json", "--feed", "sip", "--check-connection")]
    [InlineData("--check-connection", "--json", "--feed", "sip")]
    public async Task DoctorAcceptsOptionOrdering(params string[] args)
    {
        using var output = new StringWriter();
        var provider = Provider();
        Assert.Equal(0, await Application(TestData.Environment(), output, TextWriter.Null, provider)
            .RunAsync(["doctor", .. args]));
        Assert.Equal(MarketDataFeed.Sip, provider.RequestedFeed);
    }

    [Fact]
    public async Task OverrideDoesNotLeakIntoNextInvocation()
    {
        using var output = new StringWriter();
        var provider = Provider();
        var app = Application(TestData.Environment("iex"), output, TextWriter.Null, provider);
        Assert.Equal(0, await app.RunAsync(["quote", "NVDA", "--feed", "sip"]));
        Assert.Equal(MarketDataFeed.Sip, provider.RequestedFeed);
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await app.RunAsync(["quote", "NVDA"]));
        Assert.Equal(MarketDataFeed.Iex, provider.RequestedFeed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidFeedIsReportedWhenCredentialsAreMissing(bool doctor)
    {
        using var output = new StringWriter();
        var app = new CliApplication(new Dictionary<string, string?> { ["ALPACA_DATA_FEED"] = "invalid" },
            output, TextWriter.Null, _ => throw new InvalidOperationException("No provider."), TimeProvider.System);
        string[] command = doctor ? ["doctor", "--json"] : ["quote", "NVDA"];
        Assert.Equal(3, await app.RunAsync([.. command, "--feed", "sip"]));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("sip", json.RootElement.GetProperty("feed").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RootFeedIsRejected(bool doctor)
    {
        using var output = new StringWriter();
        var app = Application(TestData.Environment(), output, TextWriter.Null, Provider());
        Assert.Equal(2, await app.RunAsync(["--feed", "sip", doctor ? "doctor" : "quote", "--json"]));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("INVALID_ARGUMENTS", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task OfflineDoctorShowsOverrideWithoutCreatingProvider()
    {
        using var output = new StringWriter();
        var app = new CliApplication(TestData.Environment("invalid"), output, TextWriter.Null,
            _ => throw new InvalidOperationException("Offline doctor must not create a provider."), TimeProvider.System);
        Assert.Equal(0, await app.RunAsync(["doctor", "--feed", "sip", "--json"]));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(new[] { "schemaVersion", "provider", "feed", "status", "diagnostics" },
            json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("sip", json.RootElement.GetProperty("feed").GetString());
        Assert.DoesNotContain(json.RootElement.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("check").GetString() == "marketDataAccess");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnauthorizedOverrideMakesOneSdkRequestWithNoFallback(bool doctor)
    {
        var requests = new List<Uri>();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var app = new CliApplication(TestData.Environment("iex"), output, error,
            _ => AlpacaMarketDataProviderTests.Provider((uri, _) =>
            {
                requests.Add(uri);
                return AlpacaMarketDataProviderTests.Response(
                    """{"code":40310000,"message":"synthetic-private-value"}""", HttpStatusCode.Forbidden);
            }), new FixedTimeProvider(TestData.Now));
        string[] command = doctor ? ["doctor", "--check-connection", "--json"] : ["quote", "NVDA"];
        Assert.Equal(4, await app.RunAsync([.. command, "--feed", "sip"]));
        Assert.Contains("feed=sip", Assert.Single(requests).Query);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("sip", json.RootElement.GetProperty("feed").GetString());
        Assert.Contains("UNAUTHORIZED_DATA_FEED", output.ToString());
        Assert.DoesNotContain("synthetic-private-value", output.ToString() + error);
    }

    [Theory]
    [InlineData("iex", "sip")]
    [InlineData("sip", "iex")]
    public async Task OverrideReachesBothLatestTradeAndRawBarSdkRequests(string environmentFeed, string selected)
    {
        var requests = new List<Uri>();
        using var output = new StringWriter();
        var app = new CliApplication(TestData.Environment(environmentFeed), output, TextWriter.Null,
            _ => AlpacaMarketDataProviderTests.Provider((uri, _) =>
            {
                requests.Add(uri);
                Assert.Contains($"feed={selected}", uri.Query);
                if (uri.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal))
                    return AlpacaMarketDataProviderTests.Response(
                        """{"trades":{"NVDA":{"p":12,"t":"2026-06-10T14:59:55Z","s":1}}}""");
                Assert.Contains("adjustment=raw", uri.Query);
                Assert.Contains("start=2026-06-09", Uri.UnescapeDataString(uri.Query));
                return AlpacaMarketDataProviderTests.Response(
                    """{"symbol":"NVDA","bars":[{"t":"2026-06-09T04:00:00Z","o":10,"c":10,"h":10,"l":10,"v":1}],"next_page_token":null}""");
            }), new FixedTimeProvider(TestData.Now));
        Assert.Equal(0, await app.RunAsync(["quote", "NVDA", "--feed", selected]));
        Assert.Equal(2, requests.Count);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(10m, json.RootElement.GetProperty("symbols")[0].GetProperty("previousClose").GetDecimal());
    }

    [Fact]
    public async Task OptionBetweenSymbolsPreservesOrderAndDeduplication()
    {
        using var output = new StringWriter();
        var provider = Provider();
        Assert.Equal(1, await Application(TestData.Environment(), output, TextWriter.Null, provider)
            .RunAsync(["quote", "nvda", "--feed", "sip", "amd", "NVDA"]));
        Assert.Equal(new[] { "NVDA", "AMD" }, provider.RequestedSymbols);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(new[] { "NVDA", "AMD" }, json.RootElement.GetProperty("symbols").EnumerateArray()
            .Select(symbol => symbol.GetProperty("symbol").GetString()));
    }

    [Theory]
    [InlineData(1000, 1)]
    [InlineData(1001, 2)]
    public async Task FeedValueDoesNotCountTowardSymbolLimit(int count, int expectedExit)
    {
        using var output = new StringWriter();
        var provider = Provider();
        var symbols = Enumerable.Range(0, count).Select(index => $"A{index}").ToArray();
        Assert.Equal(expectedExit, await Application(TestData.Environment(), output, TextWriter.Null, provider)
            .RunAsync(["quote", .. symbols, "--feed", "sip"]));
        Assert.Equal(count == 1000 ? count : 0, provider.RequestedSymbols.Count);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(count, json.RootElement.GetProperty("summary").GetProperty("requested").GetInt32());
    }

    [Fact]
    public async Task HelpKeepsPrecedenceAndDoctorUsageKeepsTextMode()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var app = new CliApplication(new Dictionary<string, string?>(), output, error,
            _ => throw new InvalidOperationException("No provider."), TimeProvider.System);
        Assert.Equal(0, await app.RunAsync(["quote", "--feed", "--help"]));
        Assert.Contains("--feed iex|sip", output.ToString());
        output.GetStringBuilder().Clear();
        Assert.Equal(2, await app.RunAsync(["doctor", "--feed", "delayed_sip"]));
        Assert.Empty(output.ToString());
        Assert.Contains("delayed_sip requires --schema-version 1.1", error.ToString());
    }

    private static CliApplication Application(IReadOnlyDictionary<string, string?> environment,
        TextWriter output, TextWriter error, FakeMarketDataProvider provider) =>
        new(environment, output, error, _ => provider, new FixedTimeProvider(TestData.Now));

    private static FakeMarketDataProvider Provider() => new()
    {
        Snapshot = new ProviderSnapshot(TestData.Now, MarketSession.Regular,
            new Dictionary<string, MarketTrade> { ["NVDA"] = new(12m, TestData.Now.AddMinutes(-16)) },
            new Dictionary<string, decimal> { ["NVDA"] = 10m })
    };
}
