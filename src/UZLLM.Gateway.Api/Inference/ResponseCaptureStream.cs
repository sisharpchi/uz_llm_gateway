namespace UZLLM.Gateway.Api.Inference;

/// <summary>Best-effort bounded copy of bytes actually sent to the client; never owns the underlying response.</summary>
public sealed class ResponseCaptureStream(Stream inner, int maximumBytes) : Stream
{
    private readonly MemoryStream captured = new();
    private bool exceeded;

    public byte[]? CompletePayload => exceeded ? null : captured.ToArray();
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        inner.Write(buffer, offset, count);
        Capture(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        inner.Write(buffer);
        Capture(buffer);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count,
        CancellationToken cancellationToken)
    {
        await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        Capture(buffer.AsSpan(offset, count));
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(buffer, cancellationToken);
        Capture(buffer.Span);
    }

    private void Capture(ReadOnlySpan<byte> value)
    {
        if (exceeded) return;
        if (captured.Length + value.Length > maximumBytes)
        {
            exceeded = true;
            captured.SetLength(0);
            return;
        }
        captured.Write(value);
    }
}
