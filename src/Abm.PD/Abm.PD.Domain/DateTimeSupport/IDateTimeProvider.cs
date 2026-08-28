namespace Abm.PD.Domain.DateTimeSupport;

public interface IDateTimeProvider
{
    DateTimeOffset Now { get; }
}