using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Abm.PD.Domain.Exceptions;

namespace Abm.PD.Domain.NdJsonSupport;

/// <summary>
/// Reads Newline Delimited JSON (NDJSON) from a stream, one independent JSON value per line, without buffering
/// the whole stream. See https://github.com/ndjson/ndjson-spec
/// </summary>
public static class NdJsonReader
{
    /// <summary>
    /// Reads each line of the stream as a <typeparamref name="T"/> using <see cref="JsonSerializer"/>.
    /// </summary>
    public static IAsyncEnumerable<NdJsonLine<T>> ReadAsync<T>(
        Stream stream,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        return ReadAsync(
            stream: stream,
            deserializer: line => JsonSerializer.Deserialize<T>(line, jsonSerializerOptions),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Reads each line of the stream with the supplied <paramref name="deserializer"/>. Use this overload when the
    /// type needs a serializer other than a plain <see cref="JsonSerializer"/> call, such as a FHIR POCO.
    /// </summary>
    public static async IAsyncEnumerable<NdJsonLine<T>> ReadAsync<T>(
        Stream stream,
        Func<string, T?> deserializer,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(deserializer);

        //leaveOpen: the caller owns the stream it handed us.
        using var streamReader = new StreamReader(
            stream: stream,
            encoding: Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);

        long lineNumber = 0;
        while (await streamReader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;

            //NDJSON permits blank lines, they carry no JSON value.
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            //The line number is the only context a caller has once the stream has moved on, so any deserializer
            //failure is rethrown carrying it. The original exception is preserved as the inner exception.
            T value;
            try
            {
                value = deserializer(line)
                        ?? throw new NdJsonException(
                            $"Line {lineNumber} of the NDJSON stream deserialized to null.");
            }
            catch (Exception exception) when (exception is not (NdJsonException or OperationCanceledException))
            {
                throw new NdJsonException(
                    $"Unable to deserialize line {lineNumber} of the NDJSON stream to a {typeof(T).Name}.",
                    exception);
            }

            yield return new NdJsonLine<T>(lineNumber, value);
        }
    }
}
