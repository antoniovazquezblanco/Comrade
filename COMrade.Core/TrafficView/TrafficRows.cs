using COMrade.Core.Sessions;

namespace COMrade.Core.TrafficView;

/// <summary>A row of the analysis view: bytes one sender sent that fit a line of it.</summary>
/// <param name="Continues">The row wraps the one before: it has no time of its own.</param>
public readonly record struct TrafficRow(long Offset, int Length, TrafficSender Sender, bool Continues)
{
    public long End => Offset + Length;
}

/// <summary>
/// A session's traffic laid out in rows for the analysis view, see <see cref="TrafficText"/>. A row
/// ends after a line feed, where a run of the traffic ends (another sender, or a pause), and before
/// a character that would not fit its text in <see cref="Columns"/> or its bytes in
/// <see cref="MaxBytes"/>, the hex column's. As the traffic grows, only the last row is laid out
/// again; all of them are when the widths (<see cref="Resize"/>), <see cref="ShowSent"/> or the
/// start (<see cref="Clear"/>) change. Rows go with the traffic the session no longer keeps.
/// </summary>
/// <remarks>Not thread-safe: the view uses it on its own thread. The traffic is.</remarks>
public sealed class TrafficRows(Traffic traffic)
{
    private readonly List<Entry> _rows = [];

    // Rows before this one are gone with their traffic: removed in batches, not one by one.
    private int _first;

    // Clear: the traffic before it does not show.
    private long _from;

    // The traffic is laid out up to here.
    private long _laidOut;

    private int _columns = 80;
    private int _maxBytes = int.MaxValue;

    /// <summary>Columns a row's text fits in.</summary>
    public int Columns
    {
        get => _columns;
        set => Resize(value, _maxBytes);
    }

    /// <summary>Bytes a row holds at most; no limit at first.</summary>
    public int MaxBytes
    {
        get => _maxBytes;
        set => Resize(_columns, value);
    }

    /// <summary>The rows include what was sent to the device, not only what it sent; for the RX/TX filter to come.</summary>
    public bool ShowSent
    {
        get;
        set
        {
            if (value == field)
                return;
            field = value;
            Relayout();
        }
    } = true;

    public int Count => _rows.Count - _first;

    public TrafficRow this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            var entry = _rows[_first + index];
            return new TrafficRow(entry.Offset, entry.Length, entry.Sender, entry.Continues);
        }
    }

    /// <summary>Lays out the traffic that arrived since and drops the rows of traffic no longer kept; true when the rows changed.</summary>
    public bool Update()
    {
        var start = Math.Max(traffic.Start, _from);
        var changed = DropBefore(start);
        var end = traffic.End;
        if (end <= _laidOut)
            return changed;

        // The last row may grow: it is laid out again with what follows it.
        var from = Math.Max(_laidOut, start);
        var continues = false;
        if (Count > 0 && _rows[^1] is { Closed: false } last && last.End == _laidOut)
        {
            from = last.Offset;
            continues = last.Continues;
            _rows.RemoveAt(_rows.Count - 1);
        }

        Layout(from, end, continues);
        return true;
    }

    /// <summary>Sets <see cref="Columns"/> and <see cref="MaxBytes"/>, laying the rows out again once if either changes.</summary>
    public void Resize(int columns, int maxBytes)
    {
        (columns, maxBytes) = (Math.Max(1, columns), Math.Max(1, maxBytes));
        if (columns == _columns && maxBytes == _maxBytes)
            return;
        (_columns, _maxBytes) = (columns, maxBytes);
        Relayout();
    }

    /// <summary>Shows only the traffic that comes from now on, as Clear does in the terminal.</summary>
    public void Clear()
    {
        _from = traffic.End;
        Relayout();
    }

    /// <summary>The row that holds the byte at <paramref name="offset"/>, or the nearest after it; <see cref="Count"/> when none does.</summary>
    public int IndexOf(long offset)
    {
        var (low, high) = (_first, _rows.Count);
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_rows[middle].End <= offset)
                low = middle + 1;
            else
                high = middle;
        }

        return low - _first;
    }

    private void Relayout()
    {
        _rows.Clear();
        _first = 0;
        _laidOut = 0;
        Update();
    }

    private bool DropBefore(long start)
    {
        // A row whose start went keeps the rest.
        var gone = IndexOf(start);
        var trimmed = gone < Count && _rows[_first + gone].Offset < start;
        if (trimmed)
        {
            var row = _rows[_first + gone];
            _rows[_first + gone] = row with { Offset = start, Length = (int)(row.End - start) };
        }

        if (gone == 0)
            return trimmed;

        _first += gone;
        if (_first >= 1024 && _first >= _rows.Count / 2)
        {
            _rows.RemoveRange(0, _first);
            _first = 0;
        }

        return true;
    }

    private void Layout(long from, long end, bool continues)
    {
        var (at, runs) = traffic.Read(from, (int)Math.Min(end - from, int.MaxValue));
        if (at != from)
            continues = false;

        for (var index = 0; index < runs.Count; index++)
        {
            var run = runs[index];
            if (ShowSent || run.Sender is TrafficSender.Device)
                LayoutRun(at, run, continues, isLast: index == runs.Count - 1);
            at += run.Bytes.Length;
            continues = false;
        }

        _laidOut = Math.Max(at, _laidOut);
    }

    private void LayoutRun(long offset, TrafficRun run, bool continues, bool isLast)
    {
        var bytes = run.Bytes.AsSpan();
        var (start, width) = (0, 0);
        for (var index = 0; index < bytes.Length;)
        {
            var unit = TrafficText.Next(bytes[index..]);

            // A character that may still be completed waits in the last row, which is laid out
            // again: its escapes may not fit the text for now, but its bytes do, as they must
            // once it is complete.
            var full = index - start + unit.Length > _maxBytes
                || (width + unit.Width > _columns && !(unit.IsIncomplete && isLast));
            if (index > start && full)
            {
                Add(offset + start, index - start, run.Sender, continues, closed: true);
                (start, width, continues) = (index, 0, true);
            }

            index += unit.Length;
            width += unit.Width;
            if (bytes[index - 1] == (byte)'\n')
            {
                Add(offset + start, index - start, run.Sender, continues, closed: true);
                (start, width, continues) = (index, 0, false);
            }
        }

        if (start < bytes.Length)
            Add(offset + start, bytes.Length - start, run.Sender, continues, closed: false);
    }

    private void Add(long offset, int length, TrafficSender sender, bool continues, bool closed) =>
        _rows.Add(new Entry(offset, length, sender, continues, closed));

    // A row, and whether it is complete: ended by a line feed or full.
    private readonly record struct Entry(long Offset, int Length, TrafficSender Sender, bool Continues, bool Closed)
    {
        public long End => Offset + Length;
    }
}
