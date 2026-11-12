namespace COMrade.Core.Sessions;

/// <summary>Who sent bytes that went over a session's port.</summary>
public enum TrafficSender
{
    /// <summary>The device: what the port received.</summary>
    Device,

    /// <summary>The user, typing in the terminal.</summary>
    Keyboard,

    /// <summary>An AI assistant, through MCP.</summary>
    Assistant,

    /// <summary>COMrade itself, such as Enter at each speed while it detects the device's.</summary>
    Comrade,
}

/// <summary>Bytes one sender sent in a row, from <see cref="Traffic.Read"/>.</summary>
/// <param name="At">When the first byte of the run went over the port, also for a run read from its middle.</param>
public sealed record TrafficRun(TrafficSender Sender, DateTimeOffset At, byte[] Bytes);

/// <summary>
/// Both directions of a session's port in the order they went over it, for AI assistants: what the
/// device sent and what was sent to it, in runs marked with who sent them and when. Keeps the most
/// recent bytes, as <see cref="ReceiveBuffer"/> does, with offsets that count from the first byte.
/// </summary>
/// <remarks>Thread-safe: received data arrives on a background thread.</remarks>
public sealed class Traffic(int limit = Traffic.DefaultLimit, int maxRuns = Traffic.DefaultMaxRuns)
{
    public const int DefaultLimit = ReceiveBuffer.DefaultLimit;

    /// <summary>
    /// Runs kept: a device and its senders taking turns a byte at a time cannot use up the memory
    /// with them; the oldest quarter goes at once with its bytes.
    /// </summary>
    public const int DefaultMaxRuns = 64 * 1024;

    /// <summary>A pause this long starts a new run, with its own time, although the sender is the same.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(1);

    private readonly Lock _lock = new();
    private readonly ReceiveBuffer _bytes = new(limit);
    private readonly List<Run> _runs = [];
    private DateTimeOffset _last;

    /// <summary>Offset of the oldest byte kept.</summary>
    public long Start
    {
        get
        {
            lock (_lock)
                return OldestKept();
        }
    }

    /// <summary>Offset just past the newest byte: how many bytes went over the port in total.</summary>
    public long End => _bytes.End;

    public void Append(TrafficSender sender, ReadOnlySpan<byte> data, DateTimeOffset at)
    {
        if (data.IsEmpty)
            return;

        lock (_lock)
        {
            if (_runs.Count == 0 || _runs[^1].Sender != sender || at - _last >= Pause)
            {
                if (_runs.Count == maxRuns)
                    _runs.RemoveRange(0, Math.Max(1, maxRuns / 4));
                _runs.Add(new Run(_bytes.End, sender, at));
            }

            _last = at;
            _bytes.Append(data);

            // Runs whose bytes are gone, many at once: reads skip them meanwhile.
            var gone = RunAt(_bytes.Start);
            if (gone > 0 && gone >= _runs.Count / 4)
                _runs.RemoveRange(0, gone);
        }
    }

    /// <summary>
    /// Up to <paramref name="maxBytes"/> bytes in up to <paramref name="maxRuns"/> runs from
    /// <paramref name="offset"/> on, or from the oldest kept when those at <paramref name="offset"/>
    /// were dropped; <c>Start</c> says where they begin.
    /// </summary>
    public (long Start, IReadOnlyList<TrafficRun> Runs) Read(long offset, int maxBytes, int maxRuns = int.MaxValue)
    {
        lock (_lock)
        {
            var (start, bytes) = _bytes.Read(Math.Max(offset, OldestKept()), maxBytes);
            var runs = new List<TrafficRun>();
            var index = RunAt(start);
            for (var at = start; at < start + bytes.Length && runs.Count < maxRuns; index++)
            {
                var end = Math.Min(index + 1 < _runs.Count ? _runs[index + 1].Offset : long.MaxValue, start + bytes.Length);
                runs.Add(new TrafficRun(_runs[index].Sender, _runs[index].At, bytes[(int)(at - start)..(int)(end - start)]));
                at = end;
            }

            return (start, runs);
        }
    }

    /// <summary>Completes once there are bytes past <paramref name="offset"/>.</summary>
    public Task WaitAsync(long offset, CancellationToken cancellationToken) => _bytes.WaitAsync(offset, cancellationToken);

    // Must be called holding the lock.
    private long OldestKept() => _runs.Count == 0 ? _bytes.Start : Math.Max(_bytes.Start, _runs[0].Offset);

    // Must be called holding the lock: the last run that starts at or before offset.
    private int RunAt(long offset)
    {
        var (low, high) = (0, _runs.Count - 1);
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (_runs[middle].Offset <= offset)
                low = middle;
            else
                high = middle - 1;
        }

        return low;
    }

    private readonly record struct Run(long Offset, TrafficSender Sender, DateTimeOffset At);
}
