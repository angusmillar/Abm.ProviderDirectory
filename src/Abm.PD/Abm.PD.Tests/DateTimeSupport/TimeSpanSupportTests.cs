using System.Globalization;
using Abm.PD.Domain.DateTimeSupport;

namespace Abm.PD.Tests.DateTimeSupport;

public class TimeSpanSupportTests
{
    /// <summary>
    /// ToNarrative formats its fractional seconds with the current culture, so the tests pin the culture rather
    /// than inherit whatever the build agent happens to run under.
    /// </summary>
    private static string ToNarrativeInvariant(
        TimeSpan value)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            return value.ToNarrative();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(0, "no time at all")]
    [InlineData(0.4, "no time at all")]
    [InlineData(1, "1 milliseconds")]
    [InlineData(500, "500 milliseconds")]
    [InlineData(999, "999 milliseconds")]
    public void ToNarrative_SubSecond(
        double milliseconds,
        string expected)
    {
        Assert.Equal(expected, ToNarrativeInvariant(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Theory]
    [InlineData(1, "1 second")]
    [InlineData(1.4, "1.4 seconds")]
    [InlineData(2, "2 seconds")]
    [InlineData(59, "59 seconds")]
    public void ToNarrative_Seconds(
        double seconds,
        string expected)
    {
        Assert.Equal(expected, ToNarrativeInvariant(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(0, 1, 0, "1 minute")]
    [InlineData(0, 3, 12, "3 minutes 12 seconds")]
    [InlineData(0, 5, 0, "5 minutes")]
    [InlineData(0, 59, 59, "59 minutes 59 seconds")]
    public void ToNarrative_Minutes(
        int hours,
        int minutes,
        int seconds,
        string expected)
    {
        Assert.Equal(expected, ToNarrativeInvariant(new TimeSpan(hours, minutes, seconds)));
    }

    [Theory]
    [InlineData(1, 0, "1 hour")]
    [InlineData(2, 5, "2 hours 5 minutes")]
    [InlineData(23, 59, "23 hours 59 minutes")]
    public void ToNarrative_Hours(
        int hours,
        int minutes,
        string expected)
    {
        Assert.Equal(expected, ToNarrativeInvariant(new TimeSpan(hours, minutes, 0)));
    }

    [Theory]
    [InlineData(1, 0, "1 day")]
    [InlineData(3, 4, "3 days 4 hours")]
    [InlineData(400, 1, "400 days 1 hour")]
    public void ToNarrative_Days(
        int days,
        int hours,
        string expected)
    {
        Assert.Equal(expected, ToNarrativeInvariant(new TimeSpan(days, hours, 0, 0)));
    }

    [Fact]
    public void ToNarrative_ANegativeTimeSpanIsTheNarrativeOfItsMagnitudeWithASign()
    {
        Assert.Equal("-3 minutes 12 seconds", ToNarrativeInvariant(new TimeSpan(0, 3, 12).Negate()));
    }

    [Fact]
    public void ToNarrative_SecondsAreOmittedFromAWholeNumberOfMinutes()
    {
        //The minor unit is dropped when it is zero, so the narrative never reads "5 minutes 0 seconds".
        Assert.DoesNotContain("0 seconds", ToNarrativeInvariant(TimeSpan.FromMinutes(5)));
    }
}
