using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using COMrade.Core.Logging;
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
public sealed partial class DeviceTabViewModel : TabViewModel
{
    private static readonly TimeSpan WaitingBarDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BreakDuration = TimeSpan.FromMilliseconds(250);

    private static readonly Color Green = Color.FromArgb(0xFF, 0x6C, 0xCB, 0x5F);
    private static readonly Color Amber = Color.FromArgb(0xFF, 0xFC, 0xE1, 0x00);
    private static readonly Color Red = Color.FromArgb(0xFF, 0xFF, 0x99, 0xA4);
    private static readonly Color Grey = Color.FromArgb(0xFF, 0x9E, 0x9E, 0x9E);

    private readonly DeviceSettingsStore _store;
    private readonly DispatcherQueue _ui;
    private readonly TimeProvider _time;
    private readonly DispatcherQueueTimer _clock;
    private PortSessionState _state;
    private bool _settingsRejected;
    private bool _isRenaming;
    private DateTimeOffset? _disconnectedAt;
    private DateTimeOffset? _waitingSince;
    private string? _portNameAtDisconnect;
    private readonly AppSettingsStore _appSettings;
    private SessionLog? _log;
    private string? _logError;

    public DeviceTabViewModel(PortSession session, DeviceSettings settings, DeviceSettingsStore store, AppSettingsStore appSettings, TimeProvider? time = null)
    {
        Session = session;
        Editor = new DeviceSettingsEditor(settings, ApplyAsync);
        _store = store;
        _appSettings = appSettings;
        _ui = DispatcherQueue.GetForCurrentThread();
        _time = time ?? TimeProvider.System;
        _state = session.State;

        session.ReopenAutomatically = settings.ReopenAutomatically;
        session.StateChanged += OnSessionChanged;
        session.PortChanged += OnSessionChanged;
        session.ModemLinesChanged += OnModemLinesChanged;

        _clock = _ui.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) => RefreshStatus();
        _clock.Start();

        History.Written += OnHistoryWritten;
    }

    /// <summary>A line to show in the terminal, such as the reconnect marker. Raised on the UI thread.</summary>
    public event EventHandler<string>? TerminalMessage;

    public event EventHandler? ClearRequested;

    /// <summary>The terminal settings changed; read <see cref="Settings"/>.</summary>
    public event EventHandler? TerminalSettingsChanged;

    public PortSession Session { get; }

    /// <summary>The port settings of the toolbar's flyout.</summary>
    public DeviceSettingsEditor Editor { get; }

    public DeviceSettings Settings => Editor.Settings;

    /// <summary>What the tab's terminal has shown; replayed when the tab moves to another window.</summary>
    public TerminalHistory History { get; } = new();

    // Tab, toolbar and settings card.

    /// <summary>The name the user gave the tab, or the port and device, e.g. "COM5 · USB-SERIAL CH340".</summary>
    public override string Header => Name ?? DefaultHeader;

    /// <summary>In compact tab mode: the name, or the port.</summary>
    public override string ShortHeader => Name ?? PortName;

    public override string WindowTitle => $"COMrade - {Name ?? PortName}";

    /// <summary>Tab tooltip: a renamed tab still shows its port and device.</summary>
    public string ToolTip => Name is null ? DefaultHeader : $"{Name} · {DefaultHeader}";

    /// <summary>The name the user gave the tab, remembered for the device; null for the default title.</summary>
    public string? Name => Settings.Name;

    public bool IsNamed => Name is not null;

    /// <summary>The tab title is being edited in place.</summary>
    public bool IsRenaming
    {
        get => _isRenaming;
        set => SetProperty(ref _isRenaming, value);
    }

    private string DefaultHeader => $"{PortName} · {Session.Port.Description}";

    public string PortName => Session.Port.PortName;

    public string Description => Session.Port.Description;

    /// <summary>The port button's settings, for example "115200 · 8N1 · CR"; Open/Close port shows the state.</summary>
    public string ToolbarSummary =>
        $"{Settings.Serial.BaudRate} · {Settings.Serial.FrameNotation} · {DisplayText.LineEnding(Settings.Serial.SendLineEnding)}";

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

    // Control lines.

    public bool IsOpen => _state is PortSessionState.Open;

    /// <summary>DTR, remembered while the tab is open and set again when the port reopens.</summary>
    public bool IsDtr
    {
        get => Session.ControlLines.Dtr;
        set => _ = SetControlLinesAsync(Session.ControlLines with { Dtr = value });
    }

    public bool IsRts
    {
        get => Session.ControlLines.Rts;
        set => _ = SetControlLinesAsync(Session.ControlLines with { Rts = value });
    }

    /// <summary>RTS/CTS flow control drives RTS itself.</summary>
    public bool CanSetRts => Settings.Serial.FlowControl is not FlowControl.RtsCts;

    public double CtsOpacity => LineOpacity(ModemLines.Cts);

    public double DsrOpacity => LineOpacity(ModemLines.Dsr);

    public double CdOpacity => LineOpacity(ModemLines.CarrierDetect);

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

    /// <summary>Open port, Try again and Reconnect now.</summary>
    [RelayCommand]
    private async Task OpenAsync()
    {
        await Session.OpenAsync();
        if (Session.State is PortSessionState.Open)
            _store.Save(Session.Port.Identity, Settings, _time.GetUtcNow(), Session.Port.Description);
    }

    [RelayCommand]
    private Task CloseAsync() => Session.CloseAsync();

    [RelayCommand]
    private void StartRename() => IsRenaming = true;

    /// <summary>Gives the tab a name; an empty name goes back to the default title.</summary>
    public void Rename(string? name)
    {
        IsRenaming = false;
        name = string.IsNullOrWhiteSpace(name) || name.Trim() == DefaultHeader ? null : name.Trim();
        if (name == Name)
            return;

        // Saved like any other change of the device's settings.
        _ = Editor.ChangeAsync(Settings with { Name = name });
    }

    [RelayCommand]
    private void ResetName() => Rename(null);

    [RelayCommand]
    private Task BreakAsync() => Session.SendBreakAsync(BreakDuration);

    [RelayCommand]
    private void Clear() => ClearRequested?.Invoke(this, EventArgs.Empty);

    // Logging: what the terminal shows, to a file, while the toolbar's Log is on.

    public bool IsLogging
    {
        get => _log is not null;
        set
        {
            if (value)
                _ = StartLogAsync();
            else
                StopLog();
        }
    }

    /// <summary>
    /// Asks where to save a log, given the suggested file name and folder; null when cancelled.
    /// Set by the view, which owns the window the dialog belongs to.
    /// </summary>
    public Func<string, string, Task<string?>>? PickLogFile { get; set; }

    /// <summary>For example "Logging to COM5-2026-11-01_12-34-56.log · 12.0 KB".</summary>
    public string LogText => _log is { } log ? $"Logging to {Path.GetFileName(log.Path)} · {DisplayText.Bytes(log.Length)}" : string.Empty;

    public bool ShowLogErrorBar
    {
        get => _logError is not null;
        set
        {
            if (!value && _logError is not null)
            {
                _logError = null;
                OnPropertyChanged(nameof(ShowLogErrorBar));
            }
        }
    }

    public string LogError => _logError ?? string.Empty;

    [RelayCommand]
    private void OpenLogFolder()
    {
        if (_log is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_log.Path}\"") { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // Explorer could not be started; the status bar still names the file.
        }
    }

    // Turned on from the toolbar: asks for the file first when the settings say so.
    private async Task StartLogAsync()
    {
        var settings = _appSettings.Current.LoggingOrDefault;
        if (_log is not null || !settings.AskWhereToSave || PickLogFile is null)
        {
            StartLog();
            return;
        }

        var path = await PickLogFile(SessionLog.FileName(Name ?? PortName), settings.FolderOrDefault);
        if (path is null || _log is not null)
        {
            // Cancelled: the Log button goes back to off.
            OnPropertyChanged(nameof(IsLogging));
            return;
        }

        StartLog(path);
    }

    // Into the given file, or a new one in the log folder.
    private void StartLog(string? path = null)
    {
        if (_log is not null)
            return;

        var settings = _appSettings.Current.LoggingOrDefault;
        try
        {
            _log = path is null ? SessionLog.Start(Name ?? PortName, settings) : SessionLog.StartAt(path, settings);
            _logError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logError = path is null
                ? $"COMrade could not create a log file in {settings.FolderOrDefault}: {ex.Message} Choose another folder in the settings."
                : $"COMrade could not write the log to {path}: {ex.Message} Choose another file.";
        }

        OnPropertyChanged(string.Empty);
    }

    private void StopLog()
    {
        if (_log is not { } log)
            return;
        _log = null;
        log.Dispose();
        OnPropertyChanged(string.Empty);
    }

    // History events arrive on whichever thread wrote the output.
    private void OnHistoryWritten(object? sender, byte[] data) => Volatile.Read(ref _log)?.Write(data);

    public override void Dispose()
    {
        History.Written -= OnHistoryWritten;
        StopLog();
        _clock.Stop();
        Session.StateChanged -= OnSessionChanged;
        Session.PortChanged -= OnSessionChanged;
        Session.ModemLinesChanged -= OnModemLinesChanged;
    }

    // Applies a change from the port settings to the session and remembers it for the device.
    private async Task<bool> ApplyAsync(DeviceSettings settings)
    {
        if (settings.Serial != Settings.Serial && !await Session.ApplySettingsAsync(settings.Serial))
        {
            ShowSettingsRejectedBar = true;
            return false;
        }

        var terminalChanged = settings.Terminal != Settings.Terminal;
        Session.ReopenAutomatically = settings.ReopenAutomatically;
        ShowSettingsRejectedBar = false;
        _store.Save(Session.Port.Identity, settings, _time.GetUtcNow(), Session.Port.Description);

        // The editor takes the new settings when this returns; refresh what shows them afterwards.
        _ui.TryEnqueue(() =>
        {
            if (terminalChanged)
                TerminalSettingsChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(string.Empty);
        });
        return true;
    }

    // Session events arrive on background threads.
    private void OnSessionChanged(object? sender, EventArgs e) => _ui.TryEnqueue(UpdateState);

    private void OnModemLinesChanged(object? sender, EventArgs e) => _ui.TryEnqueue(() =>
    {
        OnPropertyChanged(nameof(CtsOpacity));
        OnPropertyChanged(nameof(DsrOpacity));
        OnPropertyChanged(nameof(CdOpacity));
    });

    // A line that is on shows bright, one that is off dim.
    private double LineOpacity(ModemLines line) => Session.ModemLines.HasFlag(line) ? 1 : 0.35;

    // The toggles show what the session holds, also when the port failed to take it.
    private async Task SetControlLinesAsync(ControlLines lines)
    {
        await Session.SetControlLinesAsync(lines);
        OnPropertyChanged(nameof(IsDtr));
        OnPropertyChanged(nameof(IsRts));
    }

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
        {
            _disconnectedAt = null;
            StopLog();
        }

        if (_state is PortSessionState.Open && previous is not PortSessionState.Open
            && _appSettings.Current.LoggingOrDefault.StartWhenPortOpens)
            StartLog();

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
        OnPropertyChanged(nameof(LogText));
    }
}
