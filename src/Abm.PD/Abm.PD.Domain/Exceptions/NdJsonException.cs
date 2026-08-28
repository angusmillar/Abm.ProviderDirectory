namespace Abm.PD.Domain.Exceptions;

public class NdJsonException : ApplicationException
{
    public NdJsonException()
        : base(){}

    public NdJsonException(string message)
        : base(message){}

    public NdJsonException(string message, Exception innerException)
        : base(message, innerException){}
}
