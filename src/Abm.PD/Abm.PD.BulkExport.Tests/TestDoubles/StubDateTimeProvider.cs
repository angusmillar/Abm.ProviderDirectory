using Abm.Core.Time;

namespace Abm.PD.BulkExport.Tests.TestDoubles;

/// <summary>
/// An <see cref="IDateTimeProvider"/> with a fixed, advanceable clock so that the export's StartTime and EndTime
/// are assertable rather than whatever the machine's clock happened to read.
/// </summary>
public sealed class StubDateTimeProvider(
    DateTimeOffset now,
    TimeSpan serviceDefaultTimeZone) : IDateTimeProvider
{
    public DateTimeOffset Now { get; private set; } = now;

    public DateTimeOffset ToServiceOffset(
        DateTime utcDateTime)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc)).ToOffset(ServiceDefaultTimeZone);
    }

    public DateTimeOffset? ToServiceOffset(
        DateTime? utcDateTime)
    {
        if (utcDateTime == null)
        {
            return null;
        }
        return ToServiceOffset(utcDateTime.Value);
    }

    public TimeSpan ServiceDefaultTimeZone { get; } = serviceDefaultTimeZone;

    public void Advance(
        TimeSpan timeSpan)
    {
        Now = Now.Add(timeSpan);
    }
}
