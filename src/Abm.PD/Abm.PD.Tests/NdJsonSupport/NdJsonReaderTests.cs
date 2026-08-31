using System.Text;
using System.Text.Json;
using Abm.PD.Domain.Exceptions;
using Abm.PD.Domain.NdJsonSupport;
using Abm.PD.Tests.TestDoubles;

namespace Abm.PD.Tests.NdJsonSupport;

public class NdJsonReaderTests
{
    private sealed record Widget(string Id, int Size);

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static Stream StreamOf(
        string content,
        bool withByteOrderMark = false)
    {
        byte[] bytes = withByteOrderMark
            ? [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(content)]
            : Encoding.UTF8.GetBytes(content);

        return new MemoryStream(bytes);
    }

    private static async Task<List<NdJsonLine<T>>> DrainAsync<T>(
        IAsyncEnumerable<NdJsonLine<T>> lines)
    {
        List<NdJsonLine<T>> drained = [];
        await foreach (NdJsonLine<T> line in lines)
        {
            drained.Add(line);
        }

        return drained;
    }

    [Fact]
    public async Task ReadAsync_ReadsEveryLineAsItsOwnValue()
    {
        await using Stream stream = StreamOf(
            "{\"id\":\"a\",\"size\":1}\n{\"id\":\"b\",\"size\":2}\n{\"id\":\"c\",\"size\":3}");

        List<NdJsonLine<Widget>> lines = await DrainAsync(NdJsonReader.ReadAsync<Widget>(stream, WebOptions));

        Assert.Equal(3, lines.Count);
        Assert.Equal(["a", "b", "c"], lines.Select(line => line.Value.Id));
        Assert.Equal([1, 2, 3], lines.Select(line => line.Value.Size));
    }

    [Fact]
    public async Task ReadAsync_NumbersLinesFromOne()
    {
        await using Stream stream = StreamOf("\"first\"\n\"second\"");

        List<NdJsonLine<string>> lines = await DrainAsync(NdJsonReader.ReadAsync<string>(stream));

        Assert.Equal([1L, 2L], lines.Select(line => line.LineNumber));
    }

    [Fact]
    public async Task ReadAsync_SkipsBlankLinesButStillCountsThem()
    {
        //A blank line carries no JSON value, but it is still a line of the file, so the numbers a caller is given
        //stay usable as a position in the source file.
        await using Stream stream = StreamOf("\n\"first\"\n   \n\"second\"\n");

        List<NdJsonLine<string>> lines = await DrainAsync(NdJsonReader.ReadAsync<string>(stream));

        Assert.Equal(2, lines.Count);
        Assert.Equal(2L, lines[0].LineNumber);
        Assert.Equal("first", lines[0].Value);
        Assert.Equal(4L, lines[1].LineNumber);
        Assert.Equal("second", lines[1].Value);
    }

    [Theory]
    [InlineData("\"a\"\n\"b\"")]
    [InlineData("\"a\"\r\n\"b\"")]
    [InlineData("\"a\"\r\n\"b\"\r\n")]
    public async Task ReadAsync_HandlesBothLineEndingsAndATrailingNewline(
        string content)
    {
        await using Stream stream = StreamOf(content);

        List<NdJsonLine<string>> lines = await DrainAsync(NdJsonReader.ReadAsync<string>(stream));

        Assert.Equal(["a", "b"], lines.Select(line => line.Value));
    }

    [Fact]
    public async Task ReadAsync_HandlesAByteOrderMark()
    {
        await using Stream stream = StreamOf("{\"id\":\"a\",\"size\":1}", withByteOrderMark: true);

        List<NdJsonLine<Widget>> lines = await DrainAsync(NdJsonReader.ReadAsync<Widget>(stream, WebOptions));

        Assert.Equal("a", Assert.Single(lines).Value.Id);
    }

    [Fact]
    public async Task ReadAsync_AnEmptyStreamYieldsNothing()
    {
        await using Stream stream = StreamOf(string.Empty);

        List<NdJsonLine<string>> lines = await DrainAsync(NdJsonReader.ReadAsync<string>(stream));

        Assert.Empty(lines);
    }

    [Fact]
    public async Task ReadAsync_LeavesTheCallersStreamOpen()
    {
        //The reader is handed a stream it does not own, so it must not close it out from under the caller.
        await using MemoryStream stream = (MemoryStream)StreamOf("\"a\"");

        await DrainAsync(NdJsonReader.ReadAsync<string>(stream));

        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task ReadAsync_ADeserializerReturningNullThrowsNamingTheLine()
    {
        await using Stream stream = StreamOf("\"first\"\nnull\n\"third\"");

        NdJsonException exception = await Assert.ThrowsAsync<NdJsonException>(
            () => DrainAsync(NdJsonReader.ReadAsync<string>(stream)));

        Assert.Contains("Line 2", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task ReadAsync_ADeserializerFailureIsRethrownWithTheLineNumberAndTheOriginalCause()
    {
        await using Stream stream = StreamOf("{\"id\":\"a\",\"size\":1}\nnot json at all");

        NdJsonException exception = await Assert.ThrowsAsync<NdJsonException>(
            () => DrainAsync(NdJsonReader.ReadAsync<Widget>(stream, WebOptions)));

        Assert.Contains("line 2", exception.Message);
        Assert.Contains(nameof(Widget), exception.Message);
        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task ReadAsync_AnNdJsonExceptionFromTheDeserializerIsNotWrappedAgain()
    {
        await using Stream stream = StreamOf("\"first\"");
        NdJsonException thrown = new("the deserializer's own complaint");

        NdJsonException exception = await Assert.ThrowsAsync<NdJsonException>(
            () => DrainAsync(NdJsonReader.ReadAsync<string>(stream, _ => throw thrown)));

        Assert.Same(thrown, exception);
    }

    [Fact]
    public async Task ReadAsync_ACancellationFromTheDeserializerIsNotWrapped()
    {
        await using Stream stream = StreamOf("\"first\"");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => DrainAsync(NdJsonReader.ReadAsync<string>(stream, _ => throw new OperationCanceledException())));
    }

    [Fact]
    public async Task ReadAsync_AnAlreadyCancelledTokenYieldsNothing()
    {
        await using Stream stream = StreamOf("\"a\"\n\"b\"");
        using CancellationTokenSource cancellationTokenSource = new();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DrainAsync(NdJsonReader.ReadAsync<string>(
                stream: stream,
                deserializer: value => value,
                cancellationToken: cancellationTokenSource.Token)));
    }

    [Fact]
    public async Task ReadAsync_StopsAtTheNextStreamReadOnceCancelled()
    {
        //Cancellation is only seen when the reader has to go back to the stream, so the lines here are longer
        //than the stream's read chunk to guarantee a second read is needed for the second line.
        string content = string.Join("\n", Enumerable.Range(1, 20).Select(number => $"\"{number}{new string('x', 200)}\""));
        await using ReadTrackingStream stream = new(Encoding.UTF8.GetBytes(content), maxBytesPerRead: 64);
        using CancellationTokenSource cancellationTokenSource = new();

        int yielded = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (NdJsonLine<string> _ in NdJsonReader.ReadAsync<string>(
                               stream: stream,
                               deserializer: value => value,
                               cancellationToken: cancellationTokenSource.Token))
            {
                yielded++;
                await cancellationTokenSource.CancelAsync();
            }
        });

        Assert.Equal(1, yielded);
    }

    [Fact]
    public async Task ReadAsync_ANullStreamThrowsOnlyOnceEnumerationStarts()
    {
        //ReadAsync is an async iterator, so its guard clauses do not run at the call site. A caller can not rely
        //on the argument being validated before the first MoveNextAsync.
        IAsyncEnumerable<NdJsonLine<string>> lines = NdJsonReader.ReadAsync<string>(stream: null!);

        await Assert.ThrowsAsync<ArgumentNullException>(() => DrainAsync(lines));
    }

    [Fact]
    public async Task ReadAsync_ANullDeserializerThrows()
    {
        await using Stream stream = StreamOf("\"a\"");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => DrainAsync(NdJsonReader.ReadAsync<string>(stream, deserializer: null!)));
    }

    [Fact]
    public async Task ReadAsync_DoesNotReadPastTheLineItHasYielded()
    {
        //The whole point of the reader is that an output file of any size never lands in memory. Reading the
        //first line must not drain the stream.
        byte[] bytes = Encoding.UTF8.GetBytes(
            string.Join("\n", Enumerable.Range(1, 400).Select(number => $"\"line-{number}\"")));

        await using ReadTrackingStream stream = new(bytes);

        await using IAsyncEnumerator<NdJsonLine<string>> enumerator =
            NdJsonReader.ReadAsync<string>(stream).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("line-1", enumerator.Current.Value);
        Assert.False(stream.ReadToEnd);
        Assert.True(stream.BytesRead < stream.TotalBytes);
    }
}
