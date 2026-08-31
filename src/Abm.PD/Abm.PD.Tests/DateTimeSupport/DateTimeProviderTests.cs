using Abm.PD.Domain.DateTimeSupport;
using Abm.PD.Domain.Settings;
using Microsoft.Extensions.Options;

namespace Abm.PD.Tests.DateTimeSupport;

public class DateTimeProviderTests
{
    private static DateTimeProvider ProviderFor(
        TimeSpan timeZoneTimeSpan)
    {
        return new DateTimeProvider(
            Options.Create(new ServiceDefaultTimeZoneSettings { TimeZoneTimeSpan = timeZoneTimeSpan }));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(0)]
    [InlineData(9)]
    public void Now_IsReportedInTheConfiguredServiceTimeZone(
        int offsetHours)
    {
        TimeSpan expectedOffset = TimeSpan.FromHours(offsetHours);

        DateTimeOffset now = ProviderFor(expectedOffset).Now;

        Assert.Equal(expectedOffset, now.Offset);
    }

    [Fact]
    public void Now_IsTheSameInstantWhicheverTimeZoneIsConfigured()
    {
        //Changing the configured time zone must only change how the instant is presented, never which instant
        //it is.
        DateTimeOffset sydney = ProviderFor(TimeSpan.FromHours(10)).Now;
        DateTimeOffset utc = ProviderFor(TimeSpan.Zero).Now;

        Assert.True((utc.UtcDateTime - sydney.UtcDateTime).Duration() < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Now_DefaultsToTheMachinesLocalOffsetWhenNothingIsConfigured()
    {
        DateTimeOffset now = new DateTimeProvider(Options.Create(new ServiceDefaultTimeZoneSettings())).Now;

        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow), now.Offset);
    }

    [Fact]
    public void Now_MovesForward()
    {
        DateTimeProvider dateTimeProvider = ProviderFor(TimeSpan.FromHours(10));

        DateTimeOffset first = dateTimeProvider.Now;
        DateTimeOffset second = dateTimeProvider.Now;

        Assert.True(second >= first);
    }
}
