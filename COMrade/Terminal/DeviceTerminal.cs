using System;
using System.Text;
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
    private readonly PortSession _session;
    private readonly TerminalView _view;
    private readonly TerminalHistory _history;
    private readonly ReceivedLineEndings _lineEndings;
    private TerminalSettings _settings;

    public DeviceTerminal(PortSession session, TerminalView view, TerminalSettings settings, TerminalHistory history)
    {
        _session = session;
        _view = view;
        _history = history;
        _settings = settings;
        _lineEndings = new ReceivedLineEndings(settings);

        _history.Attach(data => _view.Write(data));
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
        _history.Clear();
        _view.Clear();
    }

    public void Dispose()
    {
        _session.DataReceived -= OnDataReceived;
        _view.InputReceived -= OnInputReceived;
        _history.Detach();
    }

    private void OnDataReceived(object? sender, byte[] data) => _history.Write(_lineEndings.Convert(data));

    private void OnInputReceived(object? sender, string text)
    {
        if (_settings.LocalEcho)
            _history.Write(KeyboardInput.Echo(text));

        _ = _session.SendAsync(KeyboardInput.Encode(text, _session.Settings.SendLineEnding));
    }
}
