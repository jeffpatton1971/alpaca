using Alpaca.Markets;

namespace Market.Cli;

internal sealed record InvocationOptions(string[] Arguments, MarketDataFeed? FeedOverride,
    string SchemaVersion, MarketError? FeedError, MarketError? SchemaError)
{
    public const string DelayedRequirement = "delayed_sip requires --schema-version 1.1.";

    public static InvocationOptions Parse(string[] args)
    {
        var remaining = new List<string>();
        MarketDataFeed? feed = null;
        var version = "1.0";
        var seenFeed = false;
        var seenSchema = false;
        MarketError? feedError = null;
        MarketError? schemaError = null;
        // Parse together so removing one option cannot make another option's
        // missing value accidentally consume a later positional argument.
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.StartsWith("--feed=", StringComparison.Ordinal))
            {
                feedError ??= InvalidFeed();
                continue;
            }
            if (argument.StartsWith("--schema-version=", StringComparison.Ordinal))
            {
                schemaError ??= InvalidSchema();
                continue;
            }
            if (argument is not "--feed" and not "--schema-version")
            {
                remaining.Add(argument);
                continue;
            }

            var isFeed = argument == "--feed";
            if (isFeed)
            {
                if (seenFeed) feedError ??= InvalidFeed();
                seenFeed = true;
            }
            else
            {
                if (seenSchema) schemaError ??= InvalidSchema();
                seenSchema = true;
            }
            if (index + 1 == args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                if (isFeed) feedError ??= InvalidFeed();
                else schemaError ??= InvalidSchema();
                continue;
            }

            // Consume even rejected values; never expose them as quote symbols.
            var value = args[++index];
            if (isFeed)
            {
                if (FeedNames.TryParseValue(value, out var selected)) feed = selected;
                else feedError ??= InvalidFeed();
            }
            else if (value is "1.0" or "1.1") version = value;
            else schemaError ??= InvalidSchema();
        }

        return new InvocationOptions(remaining.ToArray(), feedError is null ? feed : null,
            schemaError is null ? version : "1.0", feedError, schemaError);
    }

    private static MarketError InvalidFeed() => new("INVALID_ARGUMENTS",
        "Supply --feed once, followed by iex, sip, or delayed_sip as a separate argument.");

    private static MarketError InvalidSchema() => new("INVALID_ARGUMENTS",
        "Supply --schema-version once, followed by 1.0 or 1.1 as a separate argument.");
}
