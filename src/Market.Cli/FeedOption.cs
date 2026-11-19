using Alpaca.Markets;

namespace Market.Cli;

internal sealed record FeedOption(string[] Arguments, MarketDataFeed? Override, MarketError? Error)
{
    public static bool TryParseValue(string value, out MarketDataFeed feed)
    {
        feed = MarketDataFeed.Iex;
        if (value.Equals("iex", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("sip", StringComparison.OrdinalIgnoreCase))
        {
            feed = MarketDataFeed.Sip;
            return true;
        }

        return false;
    }

    public static FeedOption Parse(string[] args)
    {
        var remaining = new List<string>();
        MarketDataFeed? feed = null;
        MarketError? error = null;
        var seen = false;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.StartsWith("--feed=", StringComparison.Ordinal))
            {
                error ??= Invalid("Use --feed followed by iex or sip as a separate argument.");
                continue;
            }

            if (argument != "--feed")
            {
                remaining.Add(argument);
                continue;
            }

            if (seen)
            {
                error ??= Invalid("Supply --feed only once.");
            }
            seen = true;

            if (index + 1 == args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error ??= Invalid("--feed requires either iex or sip.");
                continue;
            }

            // Consume even invalid values so they cannot become symbols or appear in output.
            if (TryParseValue(args[++index], out var selected))
            {
                feed = selected;
            }
            else
            {
                error ??= Invalid("--feed must be either iex or sip.");
            }
        }

        return new FeedOption(remaining.ToArray(), error is null ? feed : null, error);
    }

    private static MarketError Invalid(string message) => new("INVALID_ARGUMENTS", message);
}
