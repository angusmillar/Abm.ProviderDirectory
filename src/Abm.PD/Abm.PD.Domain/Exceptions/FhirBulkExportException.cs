namespace Abm.PD.Domain.Exceptions;

public class FhirBulkExportException : ApplicationException
{
    public FhirBulkExportException()
        : base(){}
    
    public FhirBulkExportException(string message)
        : base(message){}
    
    public FhirBulkExportException(string message, Exception innerException)
        : base(message, innerException){}
}