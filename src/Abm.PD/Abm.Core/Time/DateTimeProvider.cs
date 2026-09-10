using Microsoft.Extensions.Options;

namespace Abm.Core.Time;

public class DateTimeProvider(IOptions<TimeSettings> serviceDefaultTimeZoneSettings) : IDateTimeProvider
{
    public DateTimeOffset Now => GetNow();

    private DateTimeOffset GetNow()
    {
        return DateTimeOffset.Now.ToOffset(serviceDefaultTimeZoneSettings.Value.ServiceDefaultTimeZone);
    }
}