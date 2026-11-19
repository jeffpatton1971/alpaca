using System.Net;
using System.Text.Json;
using Alpaca.Markets;

namespace Market.Cli.Tests;

public sealed class DelayedSipTests
{
    [Theory]
    [InlineData(false, "invalid", "DeLaYeD_SiP", "delayed_sip", "sip", 900)]
    [InlineData(true, "invalid", "DeLaYeD_SiP", "delayed_sip", "sip", 900)]
    [InlineData(false, "DELAYED_SIP", null, "delayed_sip", "sip", 900)]
    [InlineData(true, "DELAYED_SIP", null, "delayed_sip", "sip", 900)]
    [InlineData(false, "delayed_sip", "iex", "iex", "iex", 0)]
    [InlineData(true, "delayed_sip", "sip", "sip", "sip", 0)]
    public async Task SelectionHasCanonicalProvenanceAndNoMutation(bool doctor, string environmentFeed,
        string? cliFeed, string expected, string historical, int delay)
    {
        var environment = TestData.Environment(environmentFeed).ToDictionary();
        var before = environment.ToArray();
        var provider = Provider(TestData.Now.AddSeconds(-901));
        using var output = new StringWriter();
        var app = new CliApplication(environment, output, TextWriter.Null, _ => provider, new FixedTimeProvider(TestData.Now));
        string[] args = doctor ? ["doctor", "--check-connection", "--json"] : ["quote", "NVDA"];
        args = [.. args, "--schema-version", "1.1"];
        if (cliFeed is not null) args = [.. args, "--feed", cliFeed];
        Assert.Equal(0, await app.RunAsync(args));
        using var json = JsonDocument.Parse(output.ToString());
        AssertMetadata(json.RootElement, expected, historical, delay);
        Assert.Equal(expected, FeedNames.Canonical(provider.RequestedFeed));
        Assert.Equal(before, environment.ToArray());
    }

    public static IEnumerable<object[]> InvalidSchemas()
    {
        string[][] cases =
        [
            ["--schema-version"], ["--schema-version", "--json"],
            ["--schema-version", ""], ["--schema-version", " "],
            ["--schema-version", " 1.1"], ["--schema-version", "1.1 "],
            ["--schema-version", "2.0"], ["--schema-version", "1.1", "--schema-version", "1.1"],
            ["--schema-version", "1.0", "--schema-version", "1.1"], ["--schema-version=1.1"],
            ["--schema-version", "synthetic-private-value"],
            ["--schema-version", "--source=synthetic-private-value"],
            ["--schema-version=--source=synthetic-private-value"],
            ["--schema-version", "--feed", "sip"],
            ["--schema-version", "--feed", "sip", "1.1"]
        ];
        foreach (var doctor in new[] { false, true })
        foreach (var args in cases) yield return [doctor, args];
    }

    [Theory]
    [MemberData(nameof(InvalidSchemas))]
    public async Task SchemaErrorsFailBeforeProviderWithoutLeakingValues(bool doctor, string[] options)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var app = new CliApplication(new Dictionary<string, string?>(), output, error,
            _ => throw new InvalidOperationException("No provider expected"), new FixedTimeProvider(TestData.Now));
        string[] args = doctor ? ["doctor", "--json"] : ["quote", "NVDA", "--json"];
        Assert.Equal(2, await app.RunAsync([.. args, .. options]));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("1.0", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("INVALID_ARGUMENTS", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        if (!doctor)
        {
            // In the interleaved malformed case, 1.1 is a separate positional token,
            // not an adjacent schema value. It must not repair the missing option value.
            string[] symbols = options.SequenceEqual(new[] { "--schema-version", "--feed", "sip", "1.1" })
                ? ["NVDA", "1.1"] : ["NVDA"];
            Assert.Equal(symbols, json.RootElement.GetProperty("symbols").EnumerateArray().Select(q => q.GetProperty("symbol").GetString()));
            Assert.Equal(symbols.Length, json.RootElement.GetProperty("summary").GetProperty("requested").GetInt32());
        }
        Assert.DoesNotContain("synthetic-private-value", (output.ToString() + error).ToLowerInvariant());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DelayedSelectionRequiresOptInEvenWithoutCredentials(bool doctor, bool fromEnvironment)
    {
        var environment = new Dictionary<string, string?>();
        if (fromEnvironment) environment["ALPACA_DATA_FEED"] = "delayed_sip";
        using var output = new StringWriter();
        var app = new CliApplication(environment, output, TextWriter.Null,
            _ => throw new InvalidOperationException("No provider expected"), new FixedTimeProvider(TestData.Now));
        string[] args = doctor ? ["doctor", "--json"] : ["quote", "NVDA"];
        if (!fromEnvironment) args = [.. args, "--feed", "delayed_sip"];
        Assert.Equal(2, await app.RunAsync(args));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("1.0", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Contains(InvocationOptions.DelayedRequirement, output.ToString());
        if (!doctor) Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("feed").ValueKind);
        Assert.False(json.RootElement.TryGetProperty("expectedDelaySeconds", out _));
    }

    [Theory]
    [InlineData("--schema-version", "1.1", "--feed", "delayed_sip", "NVDA")]
    [InlineData("NVDA", "--feed", "delayed_sip", "--schema-version", "1.1")]
    [InlineData("--feed", "delayed_sip", "NVDA", "--schema-version", "1.1")]
    public async Task OptionsMayAppearInEitherOrder(params string[] options)
    {
        using var output = new StringWriter();
        var app = new CliApplication(TestData.Environment(), output, TextWriter.Null,
            _ => Provider(TestData.Now.AddMinutes(-16)), new FixedTimeProvider(TestData.Now));
        Assert.Equal(0, await app.RunAsync(["quote", .. options]));
        using var json = JsonDocument.Parse(output.ToString());
        AssertMetadata(json.RootElement, "delayed_sip", "sip", 900);
        Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("requested").GetInt32());
    }

    [Theory]
    [InlineData(8990000000L, 899, false, 0, false)]
    [InlineData(9000000000L, 900, false, 0, false)]
    [InlineData(9000000001L, 900, true, 0, false)]
    [InlineData(9010000000L, 901, true, 1, false)]
    [InlineData(18000000000L, 1800, true, 900, false)]
    [InlineData(18000000001L, 1800, true, 900, true)]
    [InlineData(864000000000L, 86400, true, 85500, true)]
    public async Task FreshnessUsesFullPrecisionWithoutChangingRawAgeOrStatus(long ageTicks, long age,
        bool rawStale, long excess, bool beyondDelay)
    {
        using var output = new StringWriter();
        var app = new CliApplication(TestData.Environment(), output, TextWriter.Null,
            _ => Provider(TestData.Now.AddTicks(-ageTicks)), new FixedTimeProvider(TestData.Now));
        Assert.Equal(0, await app.RunAsync(["quote", "NVDA", "--feed", "delayed_sip", "--schema-version", "1.1"]));
        using var json = JsonDocument.Parse(output.ToString());
        var quote = json.RootElement.GetProperty("symbols")[0];
        Assert.Equal(age, quote.GetProperty("quoteAgeSeconds").GetInt64());
        Assert.Equal(rawStale, quote.GetProperty("isStale").GetBoolean());
        Assert.Equal(excess, quote.GetProperty("excessAgeSeconds").GetInt64());
        Assert.Equal(beyondDelay, quote.GetProperty("isStaleBeyondExpectedDelay").GetBoolean());
        Assert.Equal("ok", quote.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("future")]
    [InlineData("default")]
    public async Task UnavailableTimestampHasNullDerivedFields(string scenario)
    {
        using var output = new StringWriter();
        var provider = Provider(scenario == "default" ? default : TestData.Now.AddSeconds(1), scenario == "missing");
        var app = new CliApplication(TestData.Environment(), output, TextWriter.Null, _ => provider, new FixedTimeProvider(TestData.Now));
        Assert.Equal(1, await app.RunAsync(["quote", "NVDA", "--feed", "delayed_sip", "--schema-version", "1.1"]));
        using var json = JsonDocument.Parse(output.ToString());
        foreach (var name in new[] { "quoteAgeSeconds", "isStale", "excessAgeSeconds", "isStaleBeyondExpectedDelay" })
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("symbols")[0].GetProperty(name).ValueKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigurationFailuresPreserveCanonicalProvenance(bool doctor)
    {
        using var output = new StringWriter();
        var app = new CliApplication(new Dictionary<string, string?>(), output, TextWriter.Null,
            _ => throw new InvalidOperationException("No provider expected"), new FixedTimeProvider(TestData.Now));
        string[] args = doctor ? ["doctor", "--json"] : ["quote", "NVDA"];
        Assert.Equal(3, await app.RunAsync([.. args, "--feed", "delayed_sip", "--schema-version", "1.1"]));
        using var json = JsonDocument.Parse(output.ToString());
        AssertMetadata(json.RootElement, "delayed_sip", "sip", 900);
        if (!doctor) Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("symbols")[0].GetProperty("excessAgeSeconds").ValueKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnauthorizedLatestRequestHasNoFallbackAndRetainsSchema(bool doctor)
    {
        var requests = new List<Uri>();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var app = new CliApplication(TestData.Environment(), output, error,
            _ => AlpacaMarketDataProviderTests.Provider((uri, _) =>
            {
                requests.Add(uri);
                return AlpacaMarketDataProviderTests.Response("""{"code":40310000,"message":"synthetic-private-value"}""", HttpStatusCode.Forbidden);
            }), new FixedTimeProvider(TestData.Now));
        string[] args = doctor ? ["doctor", "--check-connection", "--json"] : ["quote", "NVDA"];
        Assert.Equal(4, await app.RunAsync([.. args, "--feed", "delayed_sip", "--schema-version", "1.1"]));
        Assert.Contains("feed=delayed_sip", Assert.Single(requests).Query);
        using var json = JsonDocument.Parse(output.ToString());
        AssertMetadata(json.RootElement, "delayed_sip", "sip", 900);
        Assert.Contains("UNAUTHORIZED_DATA_FEED", output.ToString());
        Assert.DoesNotContain("synthetic-private-value", output.ToString() + error);
    }

    [Fact]
    public async Task OfflineDoctorAndNextInvocationKeepTheirContracts()
    {
        using var output = new StringWriter();
        var app = new CliApplication(TestData.Environment(), output, TextWriter.Null,
            _ => throw new InvalidOperationException("Offline doctor must not create a provider"), new FixedTimeProvider(TestData.Now));
        Assert.Equal(0, await app.RunAsync(["doctor", "--schema-version", "1.1", "--feed", "delayed_sip", "--json"]));
        using (var json = JsonDocument.Parse(output.ToString())) AssertMetadata(json.RootElement, "delayed_sip", "sip", 900);
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await app.RunAsync(["doctor", "--json"]));
        using var normal = JsonDocument.Parse(output.ToString());
        Assert.Equal("1.0", normal.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("iex", normal.RootElement.GetProperty("feed").GetString());
        Assert.Equal(new[] { "schemaVersion", "provider", "feed", "status", "diagnostics" },
            normal.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task HistoricalAuthorizationFailureDoesNotFallBackOrLoseProvenance()
    {
        var requests = new List<Uri>();
        using var output = new StringWriter();
        var app = new CliApplication(TestData.Environment(), output, TextWriter.Null,
            _ => AlpacaMarketDataProviderTests.Provider((uri, _) =>
            {
                requests.Add(uri);
                if (uri.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal))
                    return AlpacaMarketDataProviderTests.Response(
                        """{"trades":{"NVDA":{"p":12,"t":"2026-06-10T14:44:59Z","s":1}}}""");
                return AlpacaMarketDataProviderTests.Response("""{"code":40310000,"message":"denied"}""", HttpStatusCode.Forbidden);
            }), new FixedTimeProvider(TestData.Now));
        Assert.Equal(4, await app.RunAsync(["quote", "NVDA", "--feed", "delayed_sip", "--schema-version", "1.1"]));
        Assert.Equal(2, requests.Count);
        Assert.Contains("feed=delayed_sip", requests[0].Query);
        Assert.Contains("feed=sip", requests[1].Query);
        using var json = JsonDocument.Parse(output.ToString());
        AssertMetadata(json.RootElement, "delayed_sip", "sip", 900);
        Assert.Equal("UNAUTHORIZED_DATA_FEED", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("2026-06-10T04:14:59Z", false)]
    [InlineData("2026-06-10T04:15:00Z", true)]
    public async Task HistoricalWindowMustEndOutsideTheRecentDataRestriction(string clock, bool requestBars)
    {
        // Synthetic early session intentionally exercises the cutoff guard independently
        // of normal US session opening times. The comparison date must never shift.
        var barRequests = 0;
        using var provider = AlpacaMarketDataProviderTests.Provider((uri, _) =>
        {
            if (uri.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal))
                return AlpacaMarketDataProviderTests.Response(
                    """{"trades":{"NVDA":{"p":12,"t":"2026-06-10T04:00:00Z","s":1}}}""");
            barRequests++;
            Assert.Contains("feed=sip", uri.Query);
            Assert.Contains("start=2026-06-09", Uri.UnescapeDataString(uri.Query));
            Assert.Contains("end=2026-06-10", Uri.UnescapeDataString(uri.Query));
            return AlpacaMarketDataProviderTests.Response("""{"symbol":"NVDA","bars":[],"next_page_token":null}""");
        }, (uri, _) => uri.AbsolutePath == "/v2/clock"
            ? AlpacaMarketDataProviderTests.Response(JsonSerializer.Serialize(new
                { timestamp = clock, is_open = false, next_open = clock, next_close = clock }))
            : AlpacaMarketDataProviderTests.Response(
                """[{"date":"2026-06-09","open":"09:30","close":"16:00","session_open":"0000","session_close":"2000"},{"date":"2026-06-10","open":"09:30","close":"16:00","session_open":"0000","session_close":"2000"}]"""));
        var result = await provider.GetSnapshotAsync(["NVDA"], MarketDataFeed.DelayedSip, default);
        Assert.Equal(requestBars ? 1 : 0, barRequests);
        Assert.Empty(result.PreviousCloses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidFeedWithValidSchemaUsesSafeV11ErrorEnvelope(bool doctor)
    {
        using var output = new StringWriter();
        var app = new CliApplication(TestData.Environment(), output, TextWriter.Null,
            _ => throw new InvalidOperationException("No provider expected"), new FixedTimeProvider(TestData.Now));
        string[] args = doctor ? ["doctor", "--json"] : ["quote", "NVDA"];
        Assert.Equal(2, await app.RunAsync([.. args, "--feed", "--source=synthetic-private-value", "--schema-version", "1.1"]));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("1.1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.DoesNotContain("synthetic-private-value", output.ToString());
        if (doctor)
            Assert.Equal(new[] { "schemaVersion", "provider", "error" }, json.RootElement.EnumerateObject().Select(p => p.Name));
        else
        {
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("feed").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("expectedDelaySeconds").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("previousCloseFeed").ValueKind);
            Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("requested").GetInt32());
        }
    }

    [Theory]
    [InlineData("iex")]
    [InlineData("sip")]
    public async Task V11AddsOnlyDocumentedFieldsToV10(string feed)
    {
        var outputs = new List<System.Text.Json.Nodes.JsonObject>();
        foreach (var version in new[] { "1.0", "1.1" })
        {
            using var output = new StringWriter();
            var app = new CliApplication(TestData.Environment(feed), output, TextWriter.Null,
                _ => Provider(TestData.Now.AddMinutes(-16)), new FixedTimeProvider(TestData.Now));
            Assert.Equal(0, await app.RunAsync(["quote", "NVDA", "--schema-version", version]));
            outputs.Add(System.Text.Json.Nodes.JsonNode.Parse(output.ToString())!.AsObject());
        }
        Assert.False(outputs[0].ContainsKey("expectedDelaySeconds"));
        Assert.False(outputs[0]["symbols"]![0]!.AsObject().ContainsKey("excessAgeSeconds"));
        Assert.Equal(0, outputs[1]["expectedDelaySeconds"]!.GetValue<int>());
        Assert.Equal(feed, outputs[1]["previousCloseFeed"]!.GetValue<string>());
        Assert.Equal(960, outputs[1]["symbols"]![0]!["excessAgeSeconds"]!.GetValue<int>());
        outputs[1]["schemaVersion"] = "1.0";
        outputs[1].Remove("previousCloseFeed");
        outputs[1].Remove("expectedDelaySeconds");
        var quote = outputs[1]["symbols"]![0]!.AsObject();
        quote.Remove("excessAgeSeconds");
        quote.Remove("isStaleBeyondExpectedDelay");
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(outputs[0], outputs[1]));
    }

    private static void AssertMetadata(JsonElement root, string feed, string historical, int delay)
    {
        Assert.Equal("1.1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(feed, root.GetProperty("feed").GetString());
        Assert.Equal(historical, root.GetProperty("previousCloseFeed").GetString());
        Assert.Equal(delay, root.GetProperty("expectedDelaySeconds").GetInt32());
    }

    private static FakeMarketDataProvider Provider(DateTimeOffset timestamp, bool missing = false) => new()
    {
        Snapshot = new ProviderSnapshot(TestData.Now, MarketSession.Regular,
            missing ? new Dictionary<string, MarketTrade>() : new Dictionary<string, MarketTrade> { ["NVDA"] = new(12m, timestamp) },
            new Dictionary<string, decimal> { ["NVDA"] = 10m })
    };
}
