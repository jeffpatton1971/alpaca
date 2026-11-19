using Alpaca.Markets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Market.Cli;

internal sealed record MarketError(string Code, string Message);

internal sealed record MarketTrade(decimal Price, DateTimeOffset TimestampUtc);

internal sealed record SessionTiming(
    DateOnly Date,
    DateTimeOffset SessionOpenUtc,
    DateTimeOffset RegularOpenUtc,
    DateTimeOffset RegularCloseUtc,
    DateTimeOffset SessionCloseUtc);

internal sealed record ProviderSnapshot(
    DateTimeOffset ClockTimestampUtc,
    MarketSession Session,
    IReadOnlyDictionary<string, MarketTrade> Trades,
    IReadOnlyDictionary<string, decimal> PreviousCloses);

internal enum MarketSession
{
    Regular,
    PreMarket,
    AfterHours,
    Closed,
    Unknown
}

internal sealed record SymbolQuote(
    string Symbol,
    string Currency,
    decimal? Price,
    decimal? PreviousClose,
    decimal? Change,
    decimal? ChangePercent,
    string? PriceType,
    string? PriceTimestampUtc,
    string? PriceTimestampCentral,
    long? QuoteAgeSeconds,
    bool? IsStale,
    string Status,
    MarketError? Error)
{
    public long? ExcessAgeSeconds { get; init; }
    public bool? IsStaleBeyondExpectedDelay { get; init; }
}

internal sealed record QuoteSummary(int Requested, int Succeeded, int Failed);

internal sealed record QuoteResponse(
    string SchemaVersion,
    string Provider,
    string? Feed,
    string? RetrievedAtUtc,
    string TimeZone,
    string MarketSession,
    IReadOnlyList<SymbolQuote> Symbols,
    QuoteSummary Summary,
    MarketError? Error)
{
    public string? PreviousCloseFeed => FeedNames.HistoricalName(Feed);
    public int? ExpectedDelaySeconds => FeedNames.ExpectedDelaySeconds(Feed);
}

internal sealed record DoctorDiagnostic(
    string Check,
    string Status,
    string Message);

internal sealed record DoctorResponse(
    string SchemaVersion,
    string Provider,
    string? Feed,
    string Status,
    IReadOnlyList<DoctorDiagnostic> Diagnostics)
{
    public string? PreviousCloseFeed => FeedNames.HistoricalName(Feed);
    public int? ExpectedDelaySeconds => FeedNames.ExpectedDelaySeconds(Feed);
}

internal sealed record SymbolInput(string Symbol, bool IsValid);

internal sealed record MarketSettings(
    string ApiKey,
    string ApiSecret,
    Uri DataEndpoint,
    MarketDataFeed Feed,
    int StaleAfterMinutes);

internal sealed record ConfigurationResult(
    MarketSettings? Configuration,
    MarketDataFeed? Feed,
    IReadOnlyList<DoctorDiagnostic> Diagnostics,
    MarketError? Error)
{
    public bool IsValid => Configuration is not null;
}

internal static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static readonly JsonSerializerOptions V1Options = CreateV1Options();

    private static JsonSerializerOptions CreateV1Options()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            // Version 1.0 must not acquire even null properties from the opt-in representation.
            if (info.Type != typeof(QuoteResponse) && info.Type != typeof(DoctorResponse)
                && info.Type != typeof(SymbolQuote)) return;
            foreach (var property in info.Properties.Where(property => property.Name is
                "previousCloseFeed" or "expectedDelaySeconds" or "excessAgeSeconds" or "isStaleBeyondExpectedDelay").ToArray())
                info.Properties.Remove(property);
        });
        return new JsonSerializerOptions(Options) { TypeInfoResolver = resolver };
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, value switch
    {
        QuoteResponse { SchemaVersion: "1.1" } or DoctorResponse { SchemaVersion: "1.1" } => Options,
        _ => V1Options
    });
}

internal interface IMarketDataProvider : IDisposable
{
    Task<ProviderSnapshot> GetSnapshotAsync(
        IReadOnlyList<string> symbols,
        MarketDataFeed feed,
        CancellationToken cancellationToken);

    Task CheckConnectivityAsync(
        MarketDataFeed feed,
        CancellationToken cancellationToken);
}
