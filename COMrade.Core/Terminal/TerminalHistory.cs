namespace COMrade.Core.Terminal;

/// <summary>
/// What a tab's terminal has shown, kept so a new terminal can show it again: WinUI cannot move a
/// view to another window, so a tab moved to another window gets a new terminal. Output goes
/// through the history to the terminal attached to it.
/// </summary>
/// <remarks>Thread-safe: received data arrives on a background thread.</remarks>
public sealed class TerminalHistory(int limit = TerminalHistory.DefaultLimit)
{
    /// <summary>About the control's own scrollback of 9001 lines of typical device output.</summary>
    public const int DefaultLimit = 1024 * 1024;

    private readonly Lock _lock = new();
    private readonly List<byte> _bytes = [];
    private Action<byte[]>? _output;

    /// <summary>Everything written, in order, as it is shown; e.g. for a log. Raised on the writer's thread.</summary>
    public event EventHandler<byte[]>? Written;

    /// <summary>Kept bytes, oldest first.</summary>
    public int Length
    {
        get
        {
            lock (_lock)
                return _bytes.Count;
        }
    }

    /// <summary>
    /// Lines dropped from the start beyond the limit so far: line n of an earlier copy is line
    /// n minus the lines dropped since in a later one.
    /// </summary>
    public long DroppedLines
    {
        get
        {
            lock (_lock)
                return field;
        }
        private set;
    }

    /// <summary>
    /// Sends the history to <paramref name="output"/>, then everything written after it. The replay
    /// and the switch happen together, so no output is lost or shown twice.
    /// </summary>
    public void Attach(Action<byte[]> output)
    {
        lock (_lock)
        {
            if (_bytes.Count > 0)
                output([.. _bytes]);
            _output = output;
        }
    }

    /// <summary>
    /// A copy of everything kept, oldest first, and <see cref="DroppedLines"/> when it was taken:
    /// the line numbers of its lines since the first output.
    /// </summary>
    public (byte[] Output, long DroppedLines) Snapshot()
    {
        lock (_lock)
            return ([.. _bytes], DroppedLines);
    }

    /// <summary>
    /// Hands everything kept to <paramref name="show"/>, with <see cref="DroppedLines"/> to number
    /// its lines, e.g. to show it again with search matches marked. Output written meanwhile waits,
    /// so it follows the replay and none is lost.
    /// </summary>
    public void Replay(Action<byte[], long> show)
    {
        lock (_lock)
            show([.. _bytes], DroppedLines);
    }

    public void Detach()
    {
        lock (_lock)
            _output = null;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        lock (_lock)
        {
            _bytes.AddRange(data);
            Trim();
            var copy = data.ToArray();
            _output?.Invoke(copy);
            Written?.Invoke(this, copy);
        }
    }

    /// <summary>Forgets everything shown, as the terminal's Clear does.</summary>
    public void Clear()
    {
        lock (_lock)
            _bytes.Clear();
    }

    // Drops the oldest output beyond the limit, from the start of a line, so the replay does not
    // begin in the middle of a line or of a colour sequence.
    private void Trim()
    {
        if (_bytes.Count <= limit)
            return;

        var cut = _bytes.Count - limit;
        var lineStart = _bytes.IndexOf((byte)'\n', cut);
        var dropped = lineStart < 0 ? cut : lineStart + 1;
        DroppedLines += _bytes.Take(dropped).Count(b => b == (byte)'\n');
        _bytes.RemoveRange(0, dropped);
    }
}
