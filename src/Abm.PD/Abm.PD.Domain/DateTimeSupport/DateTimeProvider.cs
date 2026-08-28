using Abm.PD.Domain.Settings;
using Microsoft.Extensions.Options;

namespace Abm.PD.Domain.DateTimeSupport;

public class DateTimeProvider(IOptions<ServiceDefaultTimeZoneSettings> serviceDefaultTimeZoneSettings) : IDateTimeProvider
{
    public DateTimeOffset Now => GetNow();

    private DateTimeOffset GetNow()
    {
        return DateTimeOffset.Now.ToOffset(serviceDefaultTimeZoneSettings.Value.TimeZoneTimeSpan);
    }
}