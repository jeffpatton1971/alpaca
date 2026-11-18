using Alpaca.Markets;

namespace Market.Cli.Tests;

public sealed class SymbolAndConfigurationTests
{
    [Fact]
    public void ParseUppercasesDeduplicatesAndPreservesInputOrder()
    {
        var symbols = SymbolParser.Parse([" nvda ", "amd", "NVDA", "BRK.B", "3m"]);

        Assert.Equal(["NVDA", "AMD", "BRK.B", "3M"], symbols.Select(symbol => symbol.Symbol));
        Assert.All(symbols, symbol => Assert.True(symbol.IsValid));
    }

    [Fact]
    public void ParseMarksMalformedSymbolsInvalid()
    {
        var symbols = SymbolParser.Parse(["A", "BRK.", "TOO-LONG-SYMBOL", "bad symbol"]);

        Assert.Equal([true, false, false, false], symbols.Select(symbol => symbol.IsValid));
    }

    [Fact]
    public void MissingCredentialsAreReportedWithoutIncludingValues()
    {
        var result = MarketConfiguration.Load(new Dictionary<string, string?>());

        Assert.False(result.IsValid);
        Assert.Equal("MISSING_CONFIGURATION", result.Error!.Code);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Check == "apiKey" && diagnostic.Status == "error");
        Assert.DoesNotContain("synthetic", string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    [Fact]
    public void LoadsExplicitFeedHttpsEndpointAndFreshnessThreshold()
    {
        var result = MarketConfiguration.Load(TestData.Environment("SIP", "https://data.example.test/", "45"));

        Assert.True(result.IsValid);
        Assert.Equal(MarketDataFeed.Sip, result.Configuration!.Feed);
        Assert.Equal(new Uri("https://data.example.test/"), result.Configuration.DataEndpoint);
        Assert.Equal(45, result.Configuration.StaleAfterMinutes);
    }

    [Theory]
    [InlineData("http://data.example.test")]
    [InlineData("https://user:pass@data.example.test")]
    [InlineData("https://data.example.test/v2")]
    [InlineData("https://data.example.test/?feed=sip")]
    public void RejectsUnsafeOrUnsupportedCustomEndpoints(string endpoint)
    {
        var result = MarketConfiguration.Load(TestData.Environment(endpoint: endpoint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Check == "endpoint" && diagnostic.Status == "error");
    }

    [Fact]
    public void RejectsUnsupportedFeedAndNonpositiveFreshness()
    {
        var result = MarketConfiguration.Load(TestData.Environment("auto", staleAfter: "0"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Check == "feed" && diagnostic.Status == "error");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Check == "freshness" && diagnostic.Status == "error");
    }
}
