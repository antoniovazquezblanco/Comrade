using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using COMrade.Core.Mcp;
using COMrade.Core.Settings;
using COMrade.Core.Shortcuts;
using COMrade.Core.Terminal;
using COMrade.Core.Updates;
using COMrade.Terminal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using VirtualKey = Windows.System.VirtualKey;

namespace COMrade.ViewModels;

/// <summary>The settings page, opened as a tab. Changes are saved as they are made.</summary>
public sealed partial class SettingsTabViewModel : TabViewModel
{
    private readonly AppSettingsStore _settings;
    private readonly DeviceSettingsStore _devices;
    private readonly UpdateService _updates;
    private readonly McpPipeServer _mcp;
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private int _mcpClientIndex;
    private ShortcutAction? _recording;

    public SettingsTabViewModel(AppSettingsStore settings, DeviceSettingsStore devices, UpdateService updates, McpPipeServer mcp)
    {
        _settings = settings;
        _devices = devices;
        _updates = updates;
        _mcp = mcp;
        NewDevice = new DeviceSettingsEditor(settings.Current.NewDevice, ApplyNewDeviceAsync);
        var keymap = settings.Current.Keymap;
        Shortcuts = [.. Keymap.Actions.Select(action => new ShortcutItem(action, keymap))];

        _devices.Changed += OnDevicesChanged;
        _updates.Changed += OnUpdatesChanged;
        _mcp.Changed += OnMcpChanged;
        RefreshDevices();
    }

    public override string Header => "Settings";

    public override string WindowTitle => "COMrade - Settings";

    public bool IsScrollingTabs
    {
        get => _settings.Current.TabMode is TabMode.Scrolling;
        set
        {
            if (value)
                SetTabMode(TabMode.Scrolling);
        }
    }

    public bool IsCompactTabs
    {
        get => _settings.Current.TabMode is TabMode.Compact;
        set
        {
            if (value)
                SetTabMode(TabMode.Compact);
        }
    }

    // Terminal: font, colours and cursor of every terminal, applied as they change.

    public static IReadOnlyList<string> FontFamilies => MonospaceFonts.All;

    public static IReadOnlyList<string> ColorSchemeNames { get; } = [.. ColorScheme.All.Select(scheme => scheme.Name)];

    public static IReadOnlyList<string> CursorOptions { get; } = ["Block", "Bar", "Underline"];

    public double MinimumFontSize => TerminalAppearance.MinimumFontSize;

    public double MaximumFontSize => TerminalAppearance.MaximumFontSize;

    public string FontFamily
    {
        get => Appearance.FontFamily;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                SaveAppearance(Appearance with { FontFamily = value.Trim() });
        }
    }

    public double FontSize
    {
        get => Appearance.FontSize;
        set
        {
            if (!double.IsNaN(value))
                SaveAppearance(Appearance with { FontSize = (int)Math.Clamp(Math.Round(value), MinimumFontSize, MaximumFontSize) });
        }
    }

    public int ColorSchemeIndex
    {
        get => Math.Max(0, ColorScheme.All.ToList().IndexOf(Appearance.Scheme));
        set
        {
            if (value >= 0)
                SaveAppearance(Appearance with { ColorScheme = ColorScheme.All[value].Name });
        }
    }

    public int CursorIndex
    {
        get => (int)Appearance.Cursor;
        set
        {
            if (value >= 0)
                SaveAppearance(Appearance with { Cursor = (CursorShape)value });
        }
    }

    public Microsoft.UI.Xaml.Media.FontFamily PreviewFontFamily => new(Appearance.FontFamily);

    /// <summary>The terminal's size is in points; XAML's in pixels at 96 DPI.</summary>
    public double PreviewFontSize => Appearance.FontSize * 96.0 / 72.0;

    public Color PreviewBackground => ToColor(Appearance.Scheme.Background);

    public Color PreviewForeground => ToColor(Appearance.Scheme.Foreground);

    /// <summary>The 16 ANSI colours of the scheme, normal then bright.</summary>
    public IReadOnlyList<Color> PreviewPalette => [.. Appearance.Scheme.Palette.Select(ToColor)];

    private TerminalAppearance Appearance => _settings.Current.TerminalOrDefault;

    // Shortcuts: the keys of each, changed by pressing the new ones.

    public IReadOnlyList<ShortcutItem> Shortcuts { get; }

    public bool CanRestoreDefaultShortcuts => Keymap.HasChanges;

    /// <summary>
    /// Whether a shortcut waits for its new keys. The window hands them to
    /// <see cref="RecordShortcut"/>: its keyboard hook sees keys pressed with Ctrl before any control.
    /// </summary>
    public bool IsRecordingShortcut => _recording is not null;

    /// <summary>What happened to the keys just pressed, when it needs telling.</summary>
    [ObservableProperty]
    public partial string? ShortcutNote { get; private set; }

    [ObservableProperty]
    public partial bool ShowShortcutNote { get; set; }

    public void StartRecordingShortcut(ShortcutAction action)
    {
        _recording = action;
        ShowShortcutNote = false;
        RefreshShortcuts();
    }

    public void StopRecordingShortcut()
    {
        if (_recording is null)
            return;
        _recording = null;
        ShowShortcutNote = false;
        RefreshShortcuts();
    }

    /// <summary>Keys pressed with Ctrl while a shortcut waits; returns whether they were taken.</summary>
    public bool RecordShortcut(VirtualKey key, bool shift)
    {
        if (_recording is not { } action || IsModifier(key))
            return false;

        if (KeyChord.From(key, shift) is { } keys)
        {
            _recording = null;
            SetShortcut(action, keys);
        }
        else
        {
            ShowNote("That key cannot be in a shortcut. Use a letter, digit, function key, arrow, Tab, Space, Enter, Backspace, Insert, Delete, Home, End, Page Up, Page Down, comma, period, plus or minus.");
        }
        return true;
    }

    /// <summary>A key pressed without Ctrl while a shortcut waits.</summary>
    public void RejectShortcutKey(VirtualKey key)
    {
        if (_recording is not null && !IsModifier(key))
            ShowNote("Press the keys with Ctrl, for example Ctrl+Shift+T. Esc cancels.");
    }

    public void ResetShortcut(ShortcutAction action) => SetShortcut(action, Keymap.DefaultFor(action));

    public void RemoveShortcut(ShortcutAction action) => SetShortcut(action, null);

    [RelayCommand]
    private void RestoreDefaultShortcuts()
    {
        ShowShortcutNote = false;
        SaveKeymap(Keymap.Default);
    }

    private Keymap Keymap => _settings.Current.Keymap;

    // Keys another shortcut had leave it without any, which is worth a word.
    private void SetShortcut(ShortcutAction action, KeyChord? keys)
    {
        var keymap = Keymap;
        if (keys is { } taken && keymap.ActionFor(taken) is { } previous && previous != action)
            ShowNote($"\"{ShortcutItem.NameOf(previous)}\" had {taken} and has no shortcut now.");
        else
            ShowShortcutNote = false;
        SaveKeymap(keymap.With(action, keys));
    }

    private void SaveKeymap(Keymap keymap)
    {
        _settings.Update(settings => settings with { Shortcuts = keymap.Changes });
        RefreshShortcuts();
    }

    private void RefreshShortcuts()
    {
        var keymap = Keymap;
        foreach (var item in Shortcuts)
            item.Update(keymap, item.Action == _recording);
        OnPropertyChanged(nameof(CanRestoreDefaultShortcuts));
    }

    private void ShowNote(string note)
    {
        ShortcutNote = note;
        ShowShortcutNote = true;
    }

    private static bool IsModifier(VirtualKey key) => key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu
        or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.LeftShift or VirtualKey.RightShift
        or VirtualKey.LeftMenu or VirtualKey.RightMenu or VirtualKey.LeftWindows or VirtualKey.RightWindows;

    // Logging: where and how sessions are logged.

    /// <summary>The log folder; empty for the default one, shown as the placeholder.</summary>
    public string LogFolder
    {
        get => Logging.Folder ?? string.Empty;
        set => SaveLogging(Logging with { Folder = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });
    }

    public string DefaultLogFolder => LogSettings.DefaultFolder;

    public bool LogTimestamps
    {
        get => Logging.Timestamps;
        set => SaveLogging(Logging with { Timestamps = value });
    }

    public bool LogKeepColourCodes
    {
        get => Logging.KeepColourCodes;
        set => SaveLogging(Logging with { KeepColourCodes = value });
    }

    public bool LogWhenPortOpens
    {
        get => Logging.StartWhenPortOpens;
        set => SaveLogging(Logging with { StartWhenPortOpens = value });
    }

    public bool LogAskWhereToSave
    {
        get => Logging.AskWhereToSave;
        set => SaveLogging(Logging with { AskWhereToSave = value });
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(Logging.FolderOrDefault);
            Process.Start(new ProcessStartInfo(Logging.FolderOrDefault) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            // The folder cannot be created or opened; logging will report it when it starts.
        }
    }

    private LogSettings Logging => _settings.Current.LoggingOrDefault;

    private void SaveLogging(LogSettings logging)
    {
        if (logging == Logging)
            return;
        _settings.Save(_settings.Current with { Logging = logging });
        OnPropertyChanged(string.Empty);
    }

    // AI assistants: what they may use, and how to connect one.

    public bool IsAiOff
    {
        get => _settings.Current.AiAccess is AiAccess.Off;
        set
        {
            if (value)
                SetAiAccess(AiAccess.Off);
        }
    }

    public bool IsAiPerTab
    {
        get => _settings.Current.AiAccess is AiAccess.PerTab;
        set
        {
            if (value)
                SetAiAccess(AiAccess.PerTab);
        }
    }

    public bool IsAiAllPorts
    {
        get => _settings.Current.AiAccess is AiAccess.AllPorts;
        set
        {
            if (value)
                SetAiAccess(AiAccess.AllPorts);
        }
    }

    public string AiStatus => _mcp.State switch
    {
        McpServerState.Listening => _mcp.Clients switch
        {
            0 => "Ready. No assistant is connected.",
            1 => "1 assistant connected.",
            var clients => $"{clients} assistants connected.",
        },
        McpServerState.Unavailable => "Another copy of COMrade is already serving AI assistants. Close it to serve them from this one.",
        _ => "Off. COMrade does not accept AI assistants.",
    };

    /// <summary>The program AI clients start; it relays MCP to the running COMrade.</summary>
    public static string RelayPath { get; } = Path.Combine(AppContext.BaseDirectory, "COMrade.Mcp.exe");

    /// <summary>How to add COMrade to each MCP client, with this installation's paths.</summary>
    public static IReadOnlyList<ClientSetup> McpClients { get; } =
        ClientSetup.All(RelayPath, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static IReadOnlyList<string> McpClientNames { get; } = [.. McpClients.Select(client => client.Name)];

    /// <summary>The client whose setup shows, Claude Code first.</summary>
    public int McpClientIndex
    {
        get => _mcpClientIndex;
        set
        {
            if (value >= 0 && SetProperty(ref _mcpClientIndex, value))
                OnPropertyChanged(nameof(McpClient));
        }
    }

    public ClientSetup McpClient => McpClients[_mcpClientIndex];

    [RelayCommand]
    private void CopyMcpClientSnippet() => CopyToClipboard(McpClient.Snippet);

    private void SetAiAccess(AiAccess access)
    {
        _settings.Update(settings => settings with { AiAccess = access });
        OnPropertyChanged(nameof(IsAiOff));
        OnPropertyChanged(nameof(IsAiPerTab));
        OnPropertyChanged(nameof(IsAiAllPorts));
        OnPropertyChanged(nameof(AiStatus));
    }

    // The server reports from its own threads.
    private void OnMcpChanged(object? sender, EventArgs e) => _ui.TryEnqueue(() => OnPropertyChanged(nameof(AiStatus)));

    private static void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    /// <summary>Settings for a device COMrade has not seen before.</summary>
    public DeviceSettingsEditor NewDevice { get; }

    public ObservableCollection<RememberedDeviceItem> RememberedDevices { get; } = [];

    public bool HasRememberedDevices => RememberedDevices.Count > 0;

    public bool HasNoRememberedDevices => RememberedDevices.Count == 0;

    /// <summary>For example "COMrade 0.2.0".</summary>
    public string Version { get; } = $"COMrade {App.Version}";

    // Updates: the automatic checks and a check on demand.

    public bool CheckForUpdatesAutomatically
    {
        get => _settings.Current.UpdatesOrDefault.CheckAutomatically;
        set => _settings.Update(settings => settings with { Updates = settings.UpdatesOrDefault with { CheckAutomatically = value } });
    }

    public bool CanCheckForUpdates => _updates.State is not UpdateState.Checking;

    public string UpdateStatus => _updates.State switch
    {
        UpdateState.Checking => "Checking for updates...",
        UpdateState.UpToDate => "COMrade is up to date.",
        UpdateState.Available => $"COMrade {_updates.Update?.Version} is available.",
        UpdateState.Failed => $"Could not check for updates. {_updates.Error}",
        _ => _settings.Current.UpdatesOrDefault.LastChecked is { } last ? $"Last checked {last.ToLocalTime():g}." : string.Empty,
    };

    public bool HasUpdate => _updates.Update is not null;

    public Uri? UpdatePage => _updates.Update?.Page;

    [RelayCommand]
    private Task CheckForUpdatesAsync() => _updates.CheckAsync();

    public Uri RepositoryUri { get; } = new("https://github.com/antoniovazquezblanco/Comrade");

    public Uri IssuesUri { get; } = new("https://github.com/antoniovazquezblanco/Comrade/issues");

    // Online rather than the LICENSE.md next to COMrade.exe: Windows has no default app for .md files.
    public Uri LicenseUri { get; } = new("https://github.com/antoniovazquezblanco/Comrade/blob/main/LICENSE.md");

    [RelayCommand]
    private void Forget(string key) => _devices.Forget(key);

    [RelayCommand]
    private void ForgetAll() => _devices.ForgetAll();

    [RelayCommand]
    private static void OpenThirdPartyNotices() =>
        Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "ThirdPartyNotices.txt")) { UseShellExecute = true });

    public override void Dispose()
    {
        _devices.Changed -= OnDevicesChanged;
        _updates.Changed -= OnUpdatesChanged;
        _mcp.Changed -= OnMcpChanged;
    }

    // Raised on the UI thread.
    private void OnUpdatesChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CanCheckForUpdates));
        OnPropertyChanged(nameof(UpdateStatus));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdatePage));
    }

    private void SetTabMode(TabMode mode)
    {
        _settings.Save(_settings.Current with { TabMode = mode });
        OnPropertyChanged(nameof(IsScrollingTabs));
        OnPropertyChanged(nameof(IsCompactTabs));
    }

    private void SaveAppearance(TerminalAppearance appearance)
    {
        if (appearance == Appearance)
            return;
        _settings.Save(_settings.Current with { Terminal = appearance });
        OnPropertyChanged(string.Empty);
    }

    private static Color ToColor(uint rgb) => Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private Task<bool> ApplyNewDeviceAsync(DeviceSettings newDevice)
    {
        _settings.Save(_settings.Current with { NewDevice = newDevice });
        return Task.FromResult(true);
    }

    // Device tabs save as their settings change; the store may report from any thread.
    private void OnDevicesChanged(object? sender, EventArgs e) => _ui.TryEnqueue(RefreshDevices);

    private void RefreshDevices()
    {
        RememberedDevices.Clear();
        foreach (var (key, device) in _devices.Devices)
        {
            var title = device.Description is { } description
                ? device.PortName is { } port ? $"{description} · last on {port}" : description
                : DisplayText.DeviceKey(key);
            var detail = $"Used {device.LastUsed.ToLocalTime():d} · {DisplayText.Settings(device.Settings.Serial)}";
            RememberedDevices.Add(new RememberedDeviceItem(key, title, detail));
        }

        OnPropertyChanged(nameof(HasRememberedDevices));
        OnPropertyChanged(nameof(HasNoRememberedDevices));
    }
}
