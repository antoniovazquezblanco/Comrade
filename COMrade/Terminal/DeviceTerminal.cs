using System;
using COMrade.Controls;
using COMrade.Core.Sessions;
using COMrade.Core.Terminal;

namespace COMrade.Terminal;

/// <summary>Connects a port session to a terminal view: received data is shown, typed text is sent.</summary>
public sealed class DeviceTerminal : IDisposable
{
    private readonly PortSession _session;
    private readonly TerminalView _view;
    private readonly ReceivedLineEndings _lineEndings;
    private TerminalSettings _settings;

    public DeviceTerminal(PortSession session, TerminalView view, TerminalSettings settings)
    {
        _session = session;
        _view = view;
        _settings = settings;
        _lineEndings = new ReceivedLineEndings(settings);

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

    public void Dispose()
    {
        _session.DataReceived -= OnDataReceived;
        _view.InputReceived -= OnInputReceived;
    }

    private void OnDataReceived(object? sender, byte[] data) => _view.Write(_lineEndings.Convert(data));

    private void OnInputReceived(object? sender, string text)
    {
        if (_settings.LocalEcho)
            _view.Write(KeyboardInput.Echo(text));

        _ = _session.SendAsync(KeyboardInput.Encode(text, _session.Settings.SendLineEnding));
    }
}
