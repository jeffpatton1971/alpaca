using System.Net;
using Alpaca.Markets;

namespace Market.Cli.Tests;

public sealed class SessionAndErrorTests
{
    [Fact]
    public void ClassifiesRegularPreMarketAfterHoursAndClosedSessions()
    {
        var day = new SessionTiming(
            new DateOnly(2026, 6, 10),
            new DateTimeOffset(2026, 6, 10, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 10, 13, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 10, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(MarketSession.PreMarket, MarketSessionClassifier.Resolve(day.RegularOpenUtc.AddMinutes(-1), false, [day]));
        Assert.Equal(MarketSession.Regular, MarketSessionClassifier.Resolve(day.RegularOpenUtc, true, [day]));
        Assert.Equal(MarketSession.AfterHours, MarketSessionClassifier.Resolve(day.RegularCloseUtc.AddMinutes(1), false, [day]));
        Assert.Equal(MarketSession.Closed, MarketSessionClassifier.Resolve(day.SessionOpenUtc.AddMinutes(-1), false, [day]));
        Assert.Equal(MarketSession.Closed, MarketSessionClassifier.Resolve(day.SessionCloseUtc, false, [day]));
    }

    [Fact]
    public void CalendarAbsenceForWeekendOrHolidayIsClosedButUnavailableCalendarIsUnknown()
    {
        var priorWeekday = new SessionTiming(
            new DateOnly(2026, 6, 10),
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue);
        var weekend = new DateTimeOffset(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(MarketSession.Closed, MarketSessionClassifier.Resolve(weekend, false, [priorWeekday]));
        Assert.Equal(MarketSession.Unknown, MarketSessionClassifier.Resolve(weekend, false, []));
    }

    [Fact]
    public void SelectsTheMostRecentRegularSessionThatHasClosed()
    {
        var earlier = Timing(new DateOnly(2026, 6, 9), 20);
        var current = Timing(new DateOnly(2026, 6, 10), 20);
        var timestamp = current.RegularCloseUtc.AddTicks(-1);

        Assert.Equal(earlier.Date, MarketSessionClassifier.PreviousCompletedSession(timestamp, [earlier, current])!.Date);
        Assert.Equal(current.Date, MarketSessionClassifier.PreviousCompletedSession(
            current.RegularCloseUtc, [earlier, current])!.Date);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "AUTHENTICATION_FAILED")]
    [InlineData(HttpStatusCode.Forbidden, "UNAUTHORIZED_DATA_FEED")]
    [InlineData(HttpStatusCode.TooManyRequests, "RATE_LIMITED")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "PROVIDER_UNAVAILABLE")]
    public void MapsProviderHttpErrorsToSanitizedCodes(HttpStatusCode status, string code)
    {
        var exception = new HttpRequestException("synthetic-secret", null, status);

        var error = MarketErrorMapper.FromProviderException(exception);

        Assert.Equal(code, error.Code);
        Assert.DoesNotContain("synthetic-secret", error.Message);
    }

    [Fact]
    public void MapsTimeoutAndCancellationSeparately()
    {
        Assert.Equal("NETWORK_TIMEOUT",
            MarketErrorMapper.FromProviderException(new OperationCanceledException(), timedOut: true).Code);
        Assert.Equal("REQUEST_CANCELLED",
            MarketErrorMapper.FromProviderException(new OperationCanceledException()).Code);
        Assert.Equal(130, MarketErrorMapper.ExitCode(new MarketError("REQUEST_CANCELLED", "cancelled")));
    }

    private static SessionTiming Timing(DateOnly date, int closeHour) =>
        new(
            date,
            new DateTimeOffset(date.Year, date.Month, date.Day, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(date.Year, date.Month, date.Day, 13, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(date.Year, date.Month, date.Day, closeHour, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(date.Year, date.Month, date.Day, 23, 0, 0, TimeSpan.Zero));
}
