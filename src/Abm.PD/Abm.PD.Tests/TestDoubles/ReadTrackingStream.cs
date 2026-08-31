namespace Abm.PD.Tests.TestDoubles;

/// <summary>
/// A read-only stream over a fixed buffer that records how many bytes have actually been pulled from it. Used to
/// prove that <c>GetExport</c> streams its output files rather than buffering them, which the repository's
/// design notes call out as an invariant that must not be broken.
/// </summary>
public sealed class ReadTrackingStream(
    byte[] buffer,
    int maxBytesPerRead = 64) : Stream
{
    private int Position_;

    public long BytesRead { get; private set; }

    public int TotalBytes => buffer.Length;

    public bool ReadToEnd => Position_ >= buffer.Length;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => buffer.Length;

    public override long Position
    {
        get => Position_;
        set => throw new NotSupportedException();
    }

    public override int Read(
        byte[] destination,
        int offset,
        int count)
    {
        //A small ceiling on each read so that a single ReadLineAsync can not incidentally drain the whole
        //stream, which would make the streaming assertion meaningless.
        int available = Math.Min(Math.Min(count, maxBytesPerRead), buffer.Length - Position_);
        if (available <= 0)
        {
            return 0;
        }

        Array.Copy(buffer, Position_, destination, offset, available);
        Position_ += available;
        BytesRead += available;
        return available;
    }

    public override void Flush()
    {
    }

    public override long Seek(
        long offset,
        SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(
        long value) => throw new NotSupportedException();

    public override void Write(
        byte[] source,
        int offset,
        int count) => throw new NotSupportedException();
}
