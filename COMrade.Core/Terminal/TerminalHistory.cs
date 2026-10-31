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
            _output?.Invoke(data.ToArray());
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
        _bytes.RemoveRange(0, lineStart < 0 ? cut : lineStart + 1);
    }
}
