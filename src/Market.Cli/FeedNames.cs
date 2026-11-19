using Alpaca.Markets;

namespace Market.Cli;

internal static class FeedNames
{
    public static bool TryParseValue(string value, out MarketDataFeed feed)
    {
        feed = value.ToLowerInvariant() switch
        {
            "iex" => MarketDataFeed.Iex,
            "sip" => MarketDataFeed.Sip,
            "delayed_sip" => MarketDataFeed.DelayedSip,
            _ => (MarketDataFeed)(-1)
        };
        return feed != (MarketDataFeed)(-1);
    }

    public static string? Canonical(MarketDataFeed? feed) => feed switch
    {
        MarketDataFeed.Iex => "iex",
        MarketDataFeed.Sip => "sip",
        MarketDataFeed.DelayedSip => "delayed_sip",
        null => null,
        _ => throw new ArgumentOutOfRangeException(nameof(feed))
    };

    public static MarketDataFeed Historical(MarketDataFeed feed) =>
        feed == MarketDataFeed.DelayedSip ? MarketDataFeed.Sip : feed;

    public static string? HistoricalName(string? feed) => feed == "delayed_sip" ? "sip" : feed;

    public static int? ExpectedDelaySeconds(string? feed) => feed switch
    {
        "delayed_sip" => 900,
        "iex" or "sip" => 0,
        _ => null
    };
}
