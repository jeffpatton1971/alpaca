using System.Text.Json;
using Alpaca.Markets;

namespace Market.Cli.Tests;

public sealed class CliApplicationTests
{
    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, false, null)]
    [InlineData(false, true, null)]
    [InlineData(true, true, null)]
    [InlineData(false, false, "sip")]
    [InlineData(true, false, "sip")]
    [InlineData(false, true, "sip")]
    [InlineData(true, true, "sip")]
    [InlineData(false, false, "delayed_sip")]
    [InlineData(true, false, "delayed_sip")]
    [InlineData(false, true, "delayed_sip")]
    [InlineData(true, true, "delayed_sip")]
    public async Task BlockedProviderCannotPreventDeadlineOrCallerCancellation(bool doctor, bool cancel, string? overrideFeed)
    {
        using var caller = new CancellationTokenSource();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var provider = new BlockedProvider();
        var app = new CliApplication(TestData.Environment(), output, error, _ => provider,
            TimeProvider.System, cancel ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(500));

        var args = Arguments(doctor);
        if (overrideFeed is not null) args = [.. args, "--feed", overrideFeed];
        if (overrideFeed == "delayed_sip") args = [.. args, "--schema-version", "1.1"];
        var command = app.RunAsync(args, caller.Token);
        try
        {
            await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (cancel)
            {
                caller.Cancel();
            }
            Assert.Equal(cancel ? 130 : 4, await command.WaitAsync(TimeSpan.FromSeconds(5)));
            AssertFailureJson(output.ToString(), doctor, cancel ? "REQUEST_CANCELLED" : "NETWORK_TIMEOUT");
        }
        finally
        {
            // Simulate a late SDK fault and allow the worker to dispose its provider.
            provider.Pending.TrySetException(new IOException("late SDK fault"));
            await provider.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        AssertFailureJson(output.ToString(), doctor, cancel ? "REQUEST_CANCELLED" : "NETWORK_TIMEOUT");
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(overrideFeed ?? "iex", json.RootElement.GetProperty("feed").GetString());
        Assert.Equal(overrideFeed == "delayed_sip" ? "1.1" : "1.0", json.RootElement.GetProperty("schemaVersion").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedSdkResponseReturnsSanitizedJsonAndExitFour(bool doctor)
    {
        const string privatePayload = "private-payload-synthetic-marker";
        using var output = new StringWriter();
        using var error = new StringWriter();
        var app = new CliApplication(TestData.Environment(), output, error,
            _ => AlpacaMarketDataProviderTests.Provider((_, _) =>
                AlpacaMarketDataProviderTests.Response($"{{\"trades\":{privatePayload}")),
            TimeProvider.System);

        Assert.Equal(4, await app.RunAsync(Arguments(doctor)));
        AssertFailureJson(output.ToString(), doctor, "UNEXPECTED_PROVIDER_RESPONSE");
        Assert.DoesNotContain(privatePayload, output.ToString());
        Assert.DoesNotContain(privatePayload, error.ToString());
    }

    [Theory]
    [InlineData(0, "regular")]
    [InlineData(1, "pre_market")]
    [InlineData(2, "after_hours")]
    [InlineData(3, "closed")]
    [InlineData(4, "unknown")]
    public async Task QuoteSerializesExactV1SessionValues(int session, string expected)
    {
        using var output = new StringWriter();
        var provider = new FakeMarketDataProvider
        {
            Snapshot = new ProviderSnapshot(TestData.Now, (MarketSession)session,
                new Dictionary<string, MarketTrade> { ["NVDA"] = new(12m, TestData.Now) },
                new Dictionary<string, decimal> { ["NVDA"] = 10m })
        };
        var app = new CliApplication(TestData.Environment(), output, TextWriter.Null,
            _ => provider, new FixedTimeProvider(TestData.Now));
        Assert.Equal(0, await app.RunAsync(Arguments(false)));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("1.0", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(expected, json.RootElement.GetProperty("marketSession").GetString());
    }

    private static string[] Arguments(bool doctor) =>
        doctor ? ["doctor", "--check-connection", "--json"] : ["quote", "NVDA", "--json"];

    private static void AssertFailureJson(string output, bool doctor, string code)
    {
        using var json = JsonDocument.Parse(output);
        if (doctor)
        {
            Assert.Equal("error", json.RootElement.GetProperty("status").GetString());
            Assert.Contains(json.RootElement.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
                diagnostic.GetProperty("check").GetString() == "marketDataAccess"
                && diagnostic.GetProperty("message").GetString()!.StartsWith($"{code}:", StringComparison.Ordinal));
        }
        else
        {
            Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
    }

    private sealed class BlockedProvider : IMarketDataProvider
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ProviderSnapshot> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProviderSnapshot> GetSnapshotAsync(IReadOnlyList<string> symbols, MarketDataFeed feed,
            CancellationToken cancellationToken) => Block();

        public Task CheckConnectivityAsync(MarketDataFeed feed, CancellationToken cancellationToken) => Block();

        private Task<ProviderSnapshot> Block()
        {
            Entered.SetResult();
            return Pending.Task;
        }

        public void Dispose() => Disposed.TrySetResult();
    }

    [Fact]
    public async Task QuoteWritesOnlyJsonToStdoutAndUsesConfiguredFeed()
    {
        var provider = new FakeMarketDataProvider
        {
            Snapshot = new ProviderSnapshot(
                TestData.Now,
                MarketSession.Regular,
                new Dictionary<string, MarketTrade>
                {
                    ["NVDA"] = new MarketTrade(12m, TestData.Now)
                },
                new Dictionary<string, decimal>
                {
                    ["NVDA"] = 10m
                })
        };
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CliApplication(
            TestData.Environment("sip"),
            output,
            error,
            _ => provider,
            new FixedTimeProvider(TestData.Now));

        var exitCode = await application.RunAsync(["quote", "NVDA", "--json"]);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("sip", document.RootElement.GetProperty("feed").GetString());
        Assert.Equal("", error.ToString());
        Assert.Equal(["NVDA"], provider.RequestedSymbols);
        Assert.Equal(MarketDataFeed.Sip, provider.RequestedFeed);
    }

    [Fact]
    public async Task DoctorReportsMissingCredentialsAsStructuredDiagnostics()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CliApplication(
            new Dictionary<string, string?>(),
            output,
            error,
            _ => throw new InvalidOperationException("Provider should not be created."),
            TimeProvider.System);

        var exitCode = await application.RunAsync(["doctor", "--json"]);

        Assert.Equal(3, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("error", document.RootElement.GetProperty("status").GetString());
        Assert.Contains(document.RootElement.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("check").GetString() == "apiKey");
        Assert.Contains("DOCTOR_FAILED", error.ToString());
    }

    [Fact]
    public async Task SanitizesProviderExceptionAndKeepsJsonOutputValid()
    {
        const string secret = "synthetic-secret-value";
        var provider = new FakeMarketDataProvider
        {
            Failure = new RestClientErrorException(secret)
        };
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CliApplication(
            TestData.Environment(),
            output,
            error,
            _ => provider,
            new FixedTimeProvider(TestData.Now));

        var exitCode = await application.RunAsync(["quote", "NVDA"]);

        Assert.Equal(4, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("PROVIDER_UNAVAILABLE", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain(secret, output.ToString());
        Assert.DoesNotContain(secret, error.ToString());
    }

    [Fact]
    public async Task OptionalDoctorConnectionCheckUsesProviderAndSelectedFeed()
    {
        var provider = new FakeMarketDataProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CliApplication(
            TestData.Environment("sip"),
            output,
            error,
            _ => provider,
            TimeProvider.System);

        var exitCode = await application.RunAsync(["doctor", "--check-connection", "--json"]);

        Assert.Equal(0, exitCode);
        Assert.True(provider.ConnectivityChecked);
        Assert.Equal(MarketDataFeed.Sip, provider.RequestedFeed);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public async Task QuoteUsageErrorsRemainValidJsonByDefault()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CliApplication(
            new Dictionary<string, string?>(),
            output,
            error,
            _ => throw new InvalidOperationException("Provider should not be created."),
            TimeProvider.System);

        var exitCode = await application.RunAsync(["quote"]);

        Assert.Equal(2, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("INVALID_ARGUMENTS", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}
