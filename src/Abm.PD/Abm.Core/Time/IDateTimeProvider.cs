namespace Abm.Core.Time;

public interface IDateTimeProvider
{
    DateTimeOffset Now { get; }

    public DateTimeOffset ToServiceOffset(
        DateTime utcDateTime);
        
    public DateTimeOffset? ToServiceOffset(
        DateTime? utcDateTime);

    public TimeSpan ServiceDefaultTimeZone {get; }
}