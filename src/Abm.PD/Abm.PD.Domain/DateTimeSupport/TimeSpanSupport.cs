namespace Abm.PD.Domain.DateTimeSupport;

public static class TimeSpanSupport
{
    /// <summary>
    /// Formats a TimeSpan as a short human-readable narrative, e.g.
    /// "less than a second", "1.4 seconds", "3 minutes 12 seconds", "2 hours 5 minutes", "3 days 4 hours".
    /// </summary>
    public static string ToNarrative(
        this TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            return "-" + ToNarrative(value.Negate());

        if (value.TotalMilliseconds < 1)
            return "no time at all";

        if (value.TotalSeconds < 1)
            return $"{value.TotalMilliseconds:0} milliseconds";

        if (value.TotalMinutes < 1)
            return $"{value.TotalSeconds:0.#} {Plural(value.TotalSeconds, "second")}";

        if (value.TotalHours < 1)
            return Compose(value.Minutes, "minute", value.Seconds, "second");

        if (value.TotalDays < 1)
            return Compose(value.Hours, "hour", value.Minutes, "minute");

        return Compose(value.Days, "day", value.Hours, "hour");

        static string Compose(
            int major,
            string majorName,
            int minor,
            string minorName)
        {
            var text = $"{major} {Plural(major, majorName)}";
            return minor > 0 ? $"{text} {minor} {Plural(minor, minorName)}" : text;
        }

        static string Plural(
            double count,
            string noun) =>
            Math.Abs(count - 1) < 0.0001 ? noun : noun + "s";
    }
}