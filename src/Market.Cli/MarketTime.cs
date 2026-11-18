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

    public static SessionTiming? ReferenceSession(
        DateTimeOffset observation,
        IReadOnlyList<SessionTiming> orderedSessions)
    {
        var date = MarketTime.EasternDate(observation);
        for (var index = 1; index < orderedSessions.Count; index++)
        {
            var session = orderedSessions[index];
            if (session.Date < date)
            {
                continue;
            }

            var prior = orderedSessions[index - 1];
            return session.Date == date && prior.Date < date
                && (index + 1 == orderedSessions.Count || orderedSessions[index + 1].Date != date)
                && (index < 2 || orderedSessions[index - 2].Date != prior.Date)
                && session.SessionOpenUtc <= session.RegularOpenUtc
                && session.RegularOpenUtc < session.RegularCloseUtc
                && session.RegularCloseUtc <= session.SessionCloseUtc
                && observation >= session.SessionOpenUtc && observation < session.SessionCloseUtc
                && prior.RegularOpenUtc < prior.RegularCloseUtc && prior.RegularCloseUtc <= observation
                ? prior
                : null;
        }

        return null;
    }
}
