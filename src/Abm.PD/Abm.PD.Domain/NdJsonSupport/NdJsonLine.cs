namespace Abm.PD.Domain.NdJsonSupport;

/// <summary>
/// One deserialized value from an NDJSON stream, carrying the 1-based line it was read from so a failure or an
/// unexpected value can be traced back to its position in the source file.
/// </summary>
public readonly record struct NdJsonLine<T>(
    long LineNumber,
    T Value);
