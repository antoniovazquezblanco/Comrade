using COMrade.Core.Sessions;

namespace COMrade.Core.Mcp;

/// <summary>
/// One assistant's place in a session's traffic, both ways. Each sender's text is decoded on its
/// own, so a character split between two of its runs comes out whole although the other side sent
/// something between them.
/// </summary>
internal sealed class TrafficReader : IOutputReader
{
    private readonly Traffic _traffic;
    private readonly Dictionary<TrafficSender, OutputText> _texts = [];

    /// <param name="backlog">How much of the earlier traffic counts as unread at first.</param>
    public TrafficReader(Traffic traffic, int backlog)
    {
        _traffic = traffic;
        Position = Math.Max(traffic.Start, traffic.End - backlog);
    }

    /// <summary>One call at a time per assistant and port.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>Offset of the next byte to read.</summary>
    public long Position { get; private set; }

    public long Unread => Math.Max(0, _traffic.End - Position);

    /// <summary>Up to <paramref name="maxBytes"/> unread bytes in up to <paramref name="maxRuns"/> runs, and how many were dropped before they were read.</summary>
    public (IReadOnlyList<TrafficRun> Runs, long Missed) Read(int maxBytes, int maxRuns)
    {
        var (start, runs) = _traffic.Read(Position, maxBytes, maxRuns);
        var missed = start - Position;
        if (missed > 0)
            _texts.Clear();
        Position = start + runs.Sum(run => (long)run.Bytes.Length);
        return (runs, missed);
    }

    /// <summary>Skips unread bytes older than the newest <paramref name="maxBytes"/>; returns how many.</summary>
    public long SkipToLatest(int maxBytes)
    {
        var skipped = Math.Max(0, _traffic.End - maxBytes - Position);
        if (skipped == 0)
            return 0;

        Position += skipped;
        _texts.Clear();
        return skipped;
    }

    public Task WaitAsync(CancellationToken cancellationToken) => _traffic.WaitAsync(Position, cancellationToken);

    /// <summary>
    /// The run as text, continuing its sender's previous run: the device's without colour and cursor
    /// codes; what was sent to it as it was, control characters included, as they are the input.
    /// </summary>
    public string Text(TrafficRun run)
    {
        if (!_texts.TryGetValue(run.Sender, out var text))
            _texts[run.Sender] = text = new OutputText(strip: run.Sender is TrafficSender.Device);
        return text.Decode(run.Bytes);
    }

    /// <summary>The run in hex. Text read afterwards starts afresh.</summary>
    public string Hex(TrafficRun run)
    {
        _texts.Clear();
        return OutputText.Hex(run.Bytes);
    }
}
