using System.Text.Json;
using Alpaca.Markets;

namespace Market.Cli.Tests;

public sealed class CliApplicationTests
{
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
