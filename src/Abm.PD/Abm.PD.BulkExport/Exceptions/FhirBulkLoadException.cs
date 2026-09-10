namespace Abm.PD.BulkExport.Exceptions;

public class FhirBulkLoadException : ApplicationException
{
    public FhirBulkLoadException()
        : base(){}

    public FhirBulkLoadException(string message)
        : base(message){}

    public FhirBulkLoadException(string message, Exception innerException)
        : base(message, innerException){}
}
