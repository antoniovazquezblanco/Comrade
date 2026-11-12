using COMrade.Core.Sessions;

namespace COMrade.Core.Mcp;

/// <summary>An assistant's place in what a session keeps for it to read.</summary>
internal interface IOutputReader
{
    long Unread { get; }

    /// <summary>Completes once there are unread bytes.</summary>
    Task WaitAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One assistant's place in a session's received bytes. Text is decoded as UTF-8 without colour
/// and cursor codes, carrying a character or a code split between two reads over to the next.
/// </summary>
internal sealed class PortReader : IOutputReader
{
    private readonly ReceiveBuffer _buffer;
    private OutputText _text = new();

    /// <param name="backlog">How much of what arrived earlier counts as unread at first.</param>
    public PortReader(ReceiveBuffer buffer, int backlog)
    {
        _buffer = buffer;
        Position = Math.Max(buffer.Start, buffer.End - backlog);
    }

    /// <summary>One call at a time per assistant and port.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>Offset of the next byte to read.</summary>
    public long Position { get; private set; }

    public long Unread => Math.Max(0, _buffer.End - Position);

    /// <summary>Up to <paramref name="maxBytes"/> unread bytes, and how many were dropped from the buffer before they were read.</summary>
    public (byte[] Bytes, long Missed) Read(int maxBytes)
    {
        var (start, bytes) = _buffer.Read(Position, maxBytes);
        var missed = start - Position;
        if (missed > 0)
            ResetText();
        Position = start + bytes.Length;
        return (bytes, missed);
    }

    /// <summary>Marks everything received so far as read; returns how many bytes that skipped.</summary>
    public long SkipToEnd() => SkipTo(_buffer.End);

    /// <summary>Skips unread bytes older than the newest <paramref name="maxBytes"/>; returns how many.</summary>
    public long SkipToLatest(int maxBytes) => SkipTo(_buffer.End - maxBytes);

    public Task WaitAsync(CancellationToken cancellationToken) => _buffer.WaitAsync(Position, cancellationToken);

    /// <summary>The bytes as text without colour and cursor codes, continuing from the previous read.</summary>
    public string Text(ReadOnlySpan<byte> bytes) => _text.Decode(bytes);

    /// <summary>The bytes in hex, e.g. "48 69 0D 0A". Text read afterwards starts afresh.</summary>
    public string Hex(byte[] bytes)
    {
        ResetText();
        return OutputText.Hex(bytes);
    }

    private long SkipTo(long offset)
    {
        var skipped = Math.Max(0, offset - Position);
        if (skipped == 0)
            return 0;

        Position = offset;
        ResetText();
        return skipped;
    }

    private void ResetText() => _text = new OutputText();
}
