using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using COMrade.Core.Serial;
using COMrade.Core.Sessions;
using COMrade.Core.Settings;
using COMrade.Core.Terminal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Windows.UI;

namespace COMrade.ViewModels;

/// <summary>One device tab: its port session, settings, bars and status bar.</summary>
public sealed partial class DeviceTabViewModel : ObservableObject, IDisposable
{
    private static readonly int[] DataBitsValues = [8, 7, 6, 5];
    private static readonly LineEnding[] SendLineEndingValues = [Core.Serial.LineEnding.Cr, Core.Serial.LineEnding.Lf, Core.Serial.LineEnding.CrLf];

    private static readonly TimeSpan WaitingBarDelay = TimeSpan.FromSeconds(1);

    private static readonly Color Green = Color.FromArgb(0xFF, 0x6C, 0xCB, 0x5F);
    private static readonly Color Amber = Color.FromArgb(0xFF, 0xFC, 0xE1, 0x00);
    private static readonly Color Red = Color.FromArgb(0xFF, 0xFF, 0x99, 0xA4);
    private static readonly Color Grey = Color.FromArgb(0xFF, 0x9E, 0x9E, 0x9E);

    private readonly DeviceSettingsStore _store;
    private readonly DispatcherQueue _ui;
    private readonly TimeProvider _time;
    private readonly DispatcherQueueTimer _clock;
    private DeviceSettings _settings;
    private PortSessionState _state;
    private bool _settingsRejected;
    private DateTimeOffset? _disconnectedAt;
    private DateTimeOffset? _waitingSince;
    private string? _portNameAtDisconnect;

    public DeviceTabViewModel(PortSession session, DeviceSettings settings, DeviceSettingsStore store, TimeProvider? time = null)
    {
        Session = session;
        _settings = settings;
        _store = store;
        _ui = DispatcherQueue.GetForCurrentThread();
        _time = time ?? TimeProvider.System;
        _state = session.State;

        session.ReopenAutomatically = settings.ReopenAutomatically;
        session.StateChanged += OnSessionChanged;
        session.PortChanged += OnSessionChanged;

        _clock = _ui.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) => RefreshStatus();
        _clock.Start();
    }

    public static IReadOnlyList<string> BaudRateOptions { get; } =
        ["300", "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200", "230400", "460800", "921600", "1000000", "2000000"];

    public static IReadOnlyList<string> DataBitsOptions { get; } = ["8", "7", "6", "5"];

    public static IReadOnlyList<string> ParityOptions { get; } = ["None", "Odd", "Even", "Mark", "Space"];

    public static IReadOnlyList<string> StopBitsOptions { get; } = ["1", "1.5", "2"];

    public static IReadOnlyList<string> FlowControlOptions { get; } = ["None", "RTS/CTS", "XON/XOFF"];

    public static IReadOnlyList<string> SendLineEndingOptions { get; } = ["CR", "LF", "CR+LF"];

    /// <summary>A line to show in the terminal, such as the reconnect marker. Raised on the UI thread.</summary>
    public event EventHandler<string>? TerminalMessage;

    public event EventHandler? ClearRequested;

    /// <summary>The terminal settings changed; read <see cref="Settings"/>.</summary>
    public event EventHandler? TerminalSettingsChanged;

    public PortSession Session { get; }

    public DeviceSettings Settings => _settings;

    // Tab, toolbar and settings card.

    public string Header => $"{PortName} · {Session.Port.Description}";

    public string PortName => Session.Port.PortName;

    public string Description => Session.Port.Description;

    /// <summary>The port button's settings, for example "115200 · 8N1 · CR"; Open/Close port shows the state.</summary>
    public string ToolbarSummary =>
        $"{_settings.Serial.BaudRate} · {_settings.Serial.FrameNotation} · {DisplayText.LineEnding(_settings.Serial.SendLineEnding)}";

    // What is visible.

    public bool CanOpen => _state is PortSessionState.Closed or PortSessionState.Error;

    public bool CanClose => _state is PortSessionState.Open or PortSessionState.Opening or PortSessionState.Waiting;

    /// <summary>
    /// Shown once the device has been gone for a second: a device that resets itself comes back
    /// sooner, and the bar would only flash and make the terminal jump.
    /// </summary>
    public bool ShowWaitingBar =>
        _state is PortSessionState.Waiting && _waitingSince is { } since && _time.GetUtcNow() - since >= WaitingBarDelay;

    public bool ShowErrorBar => _state is PortSessionState.Error;

    public bool ShowSettingsRejectedBar
    {
        get => _settingsRejected;
        set => SetProperty(ref _settingsRejected, value);
    }

    public string WaitingTitle => $"{PortName} disconnected · waiting for the device to come back…";

    public string WaitingMessage => _disconnectedAt is { } at
        ? $"The device was unplugged at {at.ToLocalTime():HH:mm:ss}. COMrade will reopen it automatically when it reappears, even under a different COM number."
        : "The device is not connected. COMrade will open the port as soon as it appears.";

    public string ErrorTitle => Session.LastError switch
    {
        SerialPortError.InUse => $"{PortName} is being used by another program",
        SerialPortError.AccessDenied => $"Access to {PortName} was denied",
        SerialPortError.NotFound => $"{PortName} is not connected",
        SerialPortError.DeviceRemoved => $"{PortName} was disconnected",
        SerialPortError.InvalidSettings => "The driver rejected these settings",
        _ => $"{PortName} stopped working",
    };

    public string ErrorMessage => Session.LastError switch
    {
        SerialPortError.InUse => "Only one program can open a serial port at a time. Close the other program (for example PuTTY or the Arduino IDE serial monitor) and try again.",
        SerialPortError.AccessDenied => "Windows refused access to the port. Check that no other program or security software is blocking it, then try again.",
        SerialPortError.NotFound => "Plug the device in, then try again.",
        SerialPortError.DeviceRemoved => "The device was unplugged. Plug it back in and try again, or turn on \"Reopen automatically\".",
        SerialPortError.InvalidSettings => "Try another speed or frame setting.",
        _ => "The port reported an error. Unplug and replug the device, then try again.",
    };

    // Status bar.

    public Color StatusColor => _state switch
    {
        PortSessionState.Open => Green,
        PortSessionState.Waiting or PortSessionState.Opening => Amber,
        PortSessionState.Error => Red,
        _ => Grey,
    };

    public string StatusText => _state switch
    {
        PortSessionState.Open when Session.OpenedAt is { } openedAt => $"Open · {DisplayText.Duration(_time.GetUtcNow() - openedAt)}",
        PortSessionState.Opening => "Opening…",
        PortSessionState.Waiting when _disconnectedAt is { } at => $"Waiting for device · {DisplayText.Duration(_time.GetUtcNow() - at)}",
        PortSessionState.Waiting => "Waiting for device",
        PortSessionState.Error => "Could not use the port",
        _ => Session.IsDevicePresent ? "Port closed" : "Device not connected",
    };

    public string ReceivedText => $"RX {DisplayText.Bytes(Session.BytesReceived)}";

    public string SentText => $"TX {DisplayText.Bytes(Session.BytesSent)}";

    // Settings, in the toolbar's port settings; they apply at once, also while the port is open.

    public string BaudRateText
    {
        get => _settings.Serial.BaudRate.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var baudRate) && baudRate > 0)
                _ = ApplyAsync(_settings with { Serial = _settings.Serial with { BaudRate = baudRate } });
        }
    }

    public int DataBitsIndex
    {
        get => Array.IndexOf(DataBitsValues, _settings.Serial.DataBits);
        set
        {
            if (value >= 0)
                _ = ApplyAsync(_settings with { Serial = _settings.Serial with { DataBits = DataBitsValues[value] } });
        }
    }

    public int ParityIndex
    {
        get => (int)_settings.Serial.Parity;
        set
        {
            if (value >= 0)
                _ = ApplyAsync(_settings with { Serial = _settings.Serial with { Parity = (Parity)value } });
        }
    }

    public int StopBitsIndex
    {
        get => (int)_settings.Serial.StopBits;
        set
        {
            if (value >= 0)
                _ = ApplyAsync(_settings with { Serial = _settings.Serial with { StopBits = (StopBits)value } });
        }
    }

    public int FlowControlIndex
    {
        get => (int)_settings.Serial.FlowControl;
        set
        {
            if (value >= 0)
                _ = ApplyAsync(_settings with { Serial = _settings.Serial with { FlowControl = (FlowControl)value } });
        }
    }

    public int SendLineEndingIndex
    {
        get => Math.Max(0, Array.IndexOf(SendLineEndingValues, _settings.Serial.SendLineEnding));
        set
        {
            if (value >= 0)
                _ = ApplyAsync(_settings with { Serial = _settings.Serial with { SendLineEnding = SendLineEndingValues[value] } });
        }
    }

    public bool LocalEcho
    {
        get => _settings.Terminal.LocalEcho;
        set => _ = ApplyAsync(_settings with { Terminal = _settings.Terminal with { LocalEcho = value } });
    }

    public bool NewLineOnLf
    {
        get => _settings.Terminal.NewLineOnLf;
        set => _ = ApplyAsync(_settings with { Terminal = _settings.Terminal with { NewLineOnLf = value } });
    }

    public bool NewLineOnCr
    {
        get => _settings.Terminal.NewLineOnCr;
        set => _ = ApplyAsync(_settings with { Terminal = _settings.Terminal with { NewLineOnCr = value } });
    }

    public bool ReopenAutomatically
    {
        get => _settings.ReopenAutomatically;
        set => _ = ApplyAsync(_settings with { ReopenAutomatically = value });
    }

    /// <summary>Open port, Try again and Reconnect now.</summary>
    [RelayCommand]
    private async Task OpenAsync()
    {
        await Session.OpenAsync();
        if (Session.State is PortSessionState.Open)
            _store.Save(Session.Port.Identity, _settings, _time.GetUtcNow());
    }

    [RelayCommand]
    private Task CloseAsync() => Session.CloseAsync();

    [RelayCommand]
    private void Clear() => ClearRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _clock.Stop();
        Session.StateChanged -= OnSessionChanged;
        Session.PortChanged -= OnSessionChanged;
    }

    private async Task ApplyAsync(DeviceSettings settings)
    {
        if (settings == _settings)
            return;

        if (settings.Serial != _settings.Serial && !await Session.ApplySettingsAsync(settings.Serial))
        {
            ShowSettingsRejectedBar = true;
            OnPropertyChanged(string.Empty);
            return;
        }

        var terminalChanged = settings.Terminal != _settings.Terminal;
        _settings = settings;
        Session.ReopenAutomatically = settings.ReopenAutomatically;
        ShowSettingsRejectedBar = false;
        _store.Save(Session.Port.Identity, settings, _time.GetUtcNow());

        if (terminalChanged)
            TerminalSettingsChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(string.Empty);
    }

    // Session events arrive on background threads.
    private void OnSessionChanged(object? sender, EventArgs e) => _ui.TryEnqueue(UpdateState);

    private void UpdateState()
    {
        var previous = _state;
        _state = Session.State;

        if (_state is PortSessionState.Waiting && previous is PortSessionState.Open)
        {
            _disconnectedAt = _time.GetUtcNow();
            _portNameAtDisconnect = PortName;
        }

        if (_state is PortSessionState.Open)
        {
            if (_disconnectedAt is { } at)
                AnnounceReconnect(at);
            _disconnectedAt = null;
        }

        if (_state is PortSessionState.Closed)
            _disconnectedAt = null;

        if (_state is not PortSessionState.Waiting)
            _waitingSince = null;
        else if (previous is not PortSessionState.Waiting)
            _waitingSince = _time.GetUtcNow();

        OnPropertyChanged(string.Empty);
    }

    private void AnnounceReconnect(DateTimeOffset disconnectedAt)
    {
        var line = $"──────── disconnected {disconnectedAt.ToLocalTime():HH:mm:ss} · reconnected {_time.GetLocalNow():HH:mm:ss}";
        if (_portNameAtDisconnect is { } before && !string.Equals(before, PortName, StringComparison.OrdinalIgnoreCase))
            line += $" · {before} is now {PortName}";
        TerminalMessage?.Invoke(this, $"\r\n\e[90m{line} ────────\e[0m\r\n");
    }

    private void RefreshStatus()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ShowWaitingBar));
        OnPropertyChanged(nameof(ReceivedText));
        OnPropertyChanged(nameof(SentText));
    }
}
