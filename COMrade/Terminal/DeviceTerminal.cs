using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using COMrade.Controls;
using COMrade.Core.Sessions;
using COMrade.Core.Terminal;

namespace COMrade.Terminal;

/// <summary>
/// Connects a port session to a terminal view: received data is shown, typed text is sent.
/// Everything shown goes through the tab's <see cref="TerminalHistory"/>, so a new view (the tab
/// moved to another window) starts with what the old one showed.
/// </summary>
public sealed class DeviceTerminal : IDisposable
{
    // How long text that could begin a match waits for the rest of it.
    private static readonly TimeSpan HoldLimit = TimeSpan.FromMilliseconds(100);

    private readonly PortSession _session;
    private readonly TerminalView _view;
    private readonly TerminalHistory _history;
    private readonly ReceivedLineEndings _lineEndings;
    private readonly Lock _lock = new();
    private readonly Timer _release;
    private TerminalSettings _settings;

    // The search the terminal shows highlighted, and what marks it in new output; null for none.
    private (string Query, bool MatchCase)? _search;
    private StreamingHighlight? _marks;

    public DeviceTerminal(PortSession session, TerminalView view, TerminalSettings settings, TerminalHistory history)
    {
        _session = session;
        _view = view;
        _history = history;
        _settings = settings;
        _lineEndings = new ReceivedLineEndings(settings);
        _release = new Timer(_ => Release());

        _history.Attach(Write);
        _session.DataReceived += OnDataReceived;
        _view.InputReceived += OnInputReceived;
    }

    public TerminalSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            _lineEndings.Settings = value;
        }
    }

    /// <summary>Shows a line from COMrade itself, such as the reconnect marker.</summary>
    public void Show(string text) => _history.Write(Encoding.UTF8.GetBytes(text));

    /// <summary>Clears the screen, the scrollback and the history.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _history.Clear();
            _view.Clear();
            _marks = NewMarks();
        }
    }

    /// <summary>
    /// Shows the output again, with the occurrences of <paramref name="query"/> highlighted, or
    /// none when it is null, and the one at <paramref name="selected"/> apart: a line numbered from
    /// the first output (see <see cref="TerminalHistory.DroppedLines"/>) and a column. Output that
    /// arrives later is highlighted too. The view stays where it was.
    /// </summary>
    public void Repaint(string? query, bool matchCase, (int Line, int Column)? selected = null) =>
        _history.Replay((output, dropped) =>
        {
            lock (_lock)
            {
                _search = query is null ? null : (query, matchCase);
                _marks = NewMarks();
                var mark = selected is var (line, column) ? ((int)(line - 1 - dropped), column) : ((int, int)?)null;
                _view.Repaint(query is null ? output : OutputHighlight.Mark(output, query, matchCase, mark));
            }
        });

    /// <summary>
    /// Scrolls the terminal to line <paramref name="index"/> of <paramref name="lines"/>, the
    /// output kept now; false when the terminal no longer keeps that line.
    /// </summary>
    public bool Reveal(IReadOnlyList<string> lines, int index)
    {
        if (_view.Columns <= 0 || TerminalLayout.RowOf(lines, index, _view.Columns, _view.BufferRows) is not { } row)
            return false;
        _view.ShowRow(row);
        return true;
    }

    public void Dispose()
    {
        _session.DataReceived -= OnDataReceived;
        _view.InputReceived -= OnInputReceived;
        _history.Detach();
        lock (_lock)
            _marks = null;
        _release.Dispose();
    }

    // Output on its way to the terminal, from the history: marked while a search is highlighted.
    private void Write(byte[] data)
    {
        lock (_lock)
        {
            if (_marks is null)
            {
                _view.Write(data);
                return;
            }

            _view.Write(_marks.Mark(data));
            if (_marks.IsHolding)
                _release.Change(HoldLimit, Timeout.InfiniteTimeSpan);
        }
    }

    private StreamingHighlight? NewMarks() => _search is var (query, matchCase) ? new StreamingHighlight(query, matchCase) : null;

    // Text held for a match shows as it is when the rest of the match does not come.
    private void Release()
    {
        lock (_lock)
        {
            if (_marks is not null)
                _view.Write(_marks.Flush());
        }
    }

    private void OnDataReceived(object? sender, byte[] data) => _history.Write(_lineEndings.Convert(data));

    private void OnInputReceived(object? sender, string text)
    {
        if (_settings.LocalEcho)
            _history.Write(KeyboardInput.Echo(text));

        _ = _session.SendAsync(KeyboardInput.Encode(text, _session.Settings.SendLineEnding));
    }
}
