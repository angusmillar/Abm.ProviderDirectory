using Abm.PD.Domain.DateTimeSupport;

namespace Abm.PD.Tests.TestDoubles;

/// <summary>
/// An <see cref="IDateTimeProvider"/> with a fixed, advanceable clock so that the export's StartTime and EndTime
/// are assertable rather than whatever the machine's clock happened to read.
/// </summary>
public sealed class StubDateTimeProvider(
    DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset Now { get; private set; } = now;

    public void Advance(
        TimeSpan timeSpan)
    {
        Now = Now.Add(timeSpan);
    }
}
