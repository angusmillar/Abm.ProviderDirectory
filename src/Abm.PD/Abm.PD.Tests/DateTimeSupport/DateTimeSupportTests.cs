using DomainDateTimeSupport = Abm.PD.Domain.DateTimeSupport.DateTimeSupport;

namespace Abm.PD.Tests.DateTimeSupport;

public class DateTimeSupportTests
{
    [Fact]
    public void GetDateTimeOffset_ParsesAnIso8601InstantWithItsOffset()
    {
        DateTimeOffset dateTimeOffset = DomainDateTimeSupport.GetDateTimeOffset("2026-08-28T10:00:00+10:00");

        Assert.Equal(new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.FromHours(10)), dateTimeOffset);
        Assert.Equal(TimeSpan.FromHours(10), dateTimeOffset.Offset);
    }

    [Fact]
    public void GetDateTimeOffset_KeepsANegativeOffset()
    {
        DateTimeOffset dateTimeOffset = DomainDateTimeSupport.GetDateTimeOffset("2026-01-01T00:00:00-05:00");

        Assert.Equal(TimeSpan.FromHours(-5), dateTimeOffset.Offset);
    }

    [Theory]
    //The exact format is yyyy-MM-ddTHH:mm:sszzz, so a Z designator, a missing offset, fractional seconds and a
    //date-only value are all rejected. This is the parser the console's hard coded _since values go through.
    [InlineData("2026-08-28T10:00:00Z")]
    [InlineData("2026-08-28T10:00:00")]
    [InlineData("2026-08-28T10:00:00.123+10:00")]
    [InlineData("2026-08-28")]
    [InlineData("28/08/2026 10:00:00 +10:00")]
    [InlineData("")]
    public void GetDateTimeOffset_RejectsAnythingOtherThanTheExactFormat(
        string value)
    {
        Assert.Throws<FormatException>(() => DomainDateTimeSupport.GetDateTimeOffset(value));
    }

    [Fact]
    public void GetDateTimeOffset_ANullValueThrows()
    {
        Assert.Throws<ArgumentNullException>(() => DomainDateTimeSupport.GetDateTimeOffset(null!));
    }
}
