namespace Abm.Core.Time;

public interface IDateTimeProvider
{
    DateTimeOffset Now { get; }
}