namespace Market.Cli;

internal static class MarketTime
{
    private static readonly TimeZoneInfo EasternTime = FindTimeZone("America/New_York", "Eastern Standard Time");
    private static readonly TimeZoneInfo CentralTime = FindTimeZone("America/Chicago", "Central Standard Time");

    public static DateOnly EasternDate(DateTimeOffset utcTimestamp) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcTimestamp, EasternTime).DateTime);

    public static string ToUtcString(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            System.Globalization.CultureInfo.InvariantCulture);

    public static string ToCentralString(DateTimeOffset timestamp) =>
        TimeZoneInfo.ConvertTime(timestamp, CentralTime)
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", System.Globalization.CultureInfo.InvariantCulture);

    public static DateTime ToUtcDateTime(DateOnly date, bool nextDay = false)
    {
        var localDate = nextDay ? date.AddDays(1) : date;
        var localMidnight = DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localMidnight, EasternTime);
    }

    public static DateTimeOffset AsUtc(DateTime dateTime) =>
        new(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));

    private static TimeZoneInfo FindTimeZone(string ianaId, string windowsId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
    }
}

internal static class MarketSessionClassifier
{
    public static MarketSession Resolve(
        DateTimeOffset timestampUtc,
        bool isRegularSessionOpen,
        IReadOnlyCollection<SessionTiming> sessions)
    {
        if (isRegularSessionOpen)
        {
            return MarketSession.Regular;
        }

        if (sessions.Count == 0)
        {
            return MarketSession.Unknown;
        }

        var currentDate = MarketTime.EasternDate(timestampUtc);
        var today = sessions.FirstOrDefault(session => session.Date == currentDate);
        if (today is null)
        {
            return MarketSession.Closed;
        }

        if (timestampUtc >= today.SessionOpenUtc && timestampUtc < today.RegularOpenUtc)
        {
            return MarketSession.PreMarket;
        }

        if (timestampUtc >= today.RegularCloseUtc && timestampUtc < today.SessionCloseUtc)
        {
            return MarketSession.AfterHours;
        }

        return MarketSession.Closed;
    }

    public static SessionTiming? PreviousCompletedSession(
        DateTimeOffset timestampUtc,
        IReadOnlyCollection<SessionTiming> sessions) =>
        sessions
            .Where(session => session.RegularCloseUtc <= timestampUtc)
            .OrderByDescending(session => session.RegularCloseUtc)
            .FirstOrDefault();
}
