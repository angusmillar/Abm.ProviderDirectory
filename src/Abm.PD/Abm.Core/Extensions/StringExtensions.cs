using System.Globalization;

namespace Abm.Core.Extensions;

public static class StringExtensions
{
    /// <summary>
    /// Get a DateTimeOffset from and ISO 8601 formated date time (e.g. 2026-08-28T10:00:00+10:00)
    /// </summary>
    /// <param name="dateTimeIso8601Format"></param>
    /// <returns></returns>
    public static DateTimeOffset GetIso8601DateTimeOffset(this string dateTimeIso8601Format)
    {
        const string format = "yyyy-MM-ddTHH:mm:sszzz";
        return DateTimeOffset.ParseExact(dateTimeIso8601Format, format, CultureInfo.InvariantCulture);
    }
}
    