namespace Abm.PD.BulkExport.DateTimeSupport;

public interface IDateTimeProvider
{
    DateTimeOffset Now { get; }
}