namespace COMrade.Core.Sessions;

/// <summary>
/// The raw bytes a session received, before any line-ending conversion, for AI assistants to read
/// at their own pace. Keeps the most recent ones; offsets count from the first byte the session
/// received, so a reader that fell behind knows how much it missed. Clearing the terminal does not
/// clear it, so readers never lose their place.
/// </summary>
/// <remarks>Thread-safe: received data arrives on a background thread.</remarks>
public sealed class ReceiveBuffer(int limit = ReceiveBuffer.DefaultLimit)
{
    /// <summary>As much as a tab's terminal history.</summary>
    public const int DefaultLimit = 1024 * 1024;

    private const int InitialCapacity = 4096;

    private readonly Lock _lock = new();
    private TaskCompletionSource _arrived = NewSignal();
    private byte[] _ring = [];
    private int _head;
    private int _count;
    private long _end;

    /// <summary>Offset of the oldest byte kept.</summary>
    public long Start
    {
        get
        {
            lock (_lock)
                return _end - _count;
        }
    }

    /// <summary>Offset just past the newest byte: how many bytes were received in total.</summary>
    public long End
    {
        get
        {
            lock (_lock)
                return _end;
        }
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        TaskCompletionSource arrived;
        lock (_lock)
        {
            _end += data.Length;
            if (data.Length > limit)
                data = data[^limit..];

            Grow(_count + data.Length);
            var overflow = _count + data.Length - _ring.Length;
            if (overflow > 0)
            {
                _head = (_head + overflow) % _ring.Length;
                _count -= overflow;
            }

            var tail = (_head + _count) % _ring.Length;
            var first = Math.Min(data.Length, _ring.Length - tail);
            data[..first].CopyTo(_ring.AsSpan(tail));
            data[first..].CopyTo(_ring);
            _count += data.Length;

            arrived = _arrived;
            _arrived = NewSignal();
        }

        arrived.SetResult();
    }

    /// <summary>
    /// Up to <paramref name="maxBytes"/> bytes from <paramref name="offset"/> on, or from the oldest
    /// byte kept when those at <paramref name="offset"/> were dropped; <c>Start</c> says where they begin.
    /// </summary>
    public (long Start, byte[] Bytes) Read(long offset, int maxBytes)
    {
        lock (_lock)
        {
            var start = Math.Clamp(offset, _end - _count, _end);
            var length = (int)Math.Min(maxBytes, _end - start);
            var bytes = new byte[length];
            if (length == 0)
                return (start, bytes);

            var from = (int)((_head + (start - (_end - _count))) % _ring.Length);
            var first = Math.Min(length, _ring.Length - from);
            _ring.AsSpan(from, first).CopyTo(bytes);
            _ring.AsSpan(0, length - first).CopyTo(bytes.AsSpan(first));
            return (start, bytes);
        }
    }

    /// <summary>Completes once there are bytes past <paramref name="offset"/>.</summary>
    public async Task WaitAsync(long offset, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task arrived;
            lock (_lock)
            {
                if (_end > offset)
                    return;
                arrived = _arrived.Task;
            }

            await arrived.WaitAsync(cancellationToken);
        }
    }

    // Starts small and doubles up to the limit, so a quiet tab does not hold a megabyte.
    private void Grow(int needed)
    {
        if (_ring.Length >= Math.Min(needed, limit))
            return;

        var capacity = Math.Max(InitialCapacity, _ring.Length);
        while (capacity < needed && capacity < limit)
            capacity *= 2;

        var ring = new byte[Math.Min(capacity, limit)];
        var first = Math.Min(_count, _ring.Length - _head);
        _ring.AsSpan(_head, first).CopyTo(ring);
        _ring.AsSpan(0, _count - first).CopyTo(ring.AsSpan(first));
        _ring = ring;
        _head = 0;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
