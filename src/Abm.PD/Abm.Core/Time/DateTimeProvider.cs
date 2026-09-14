using Microsoft.Extensions.Options;

namespace Abm.Core.Time;

public class DateTimeProvider(IOptions<TimeSettings> timeSettings) : IDateTimeProvider
{
    public DateTimeOffset Now => GetNow();

    public DateTimeOffset ToServiceOffset(
        DateTime utcDateTime)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc)).ToOffset(timeSettings.Value.ServiceDefaultTimeZone);
    }

    public TimeSpan ServiceDefaultTimeZone => timeSettings.Value.ServiceDefaultTimeZone;
    
    public DateTimeOffset? ToServiceOffset(
        DateTime? utcDateTime)
    {
        if (utcDateTime == null)
        {
            return null;
        }
        return ToServiceOffset(utcDateTime.Value);
    }

    private DateTimeOffset GetNow()
    {
        return DateTimeOffset.Now.ToOffset(timeSettings.Value.ServiceDefaultTimeZone);
    }
}