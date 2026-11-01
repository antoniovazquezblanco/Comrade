using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using COMrade.Core.Settings;
using COMrade.Core.Terminal;
using COMrade.Terminal;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Windows.UI;

namespace COMrade.ViewModels;

/// <summary>The settings page, opened as a tab. Changes are saved as they are made.</summary>
public sealed partial class SettingsTabViewModel : TabViewModel
{
    private readonly AppSettingsStore _settings;
    private readonly DeviceSettingsStore _devices;
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();

    public SettingsTabViewModel(AppSettingsStore settings, DeviceSettingsStore devices)
    {
        _settings = settings;
        _devices = devices;
        NewDevice = new DeviceSettingsEditor(settings.Current.NewDevice, ApplyNewDeviceAsync);

        _devices.Changed += OnDevicesChanged;
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

    /// <summary>Settings for a device COMrade has not seen before.</summary>
    public DeviceSettingsEditor NewDevice { get; }

    public ObservableCollection<RememberedDeviceItem> RememberedDevices { get; } = [];

    public bool HasRememberedDevices => RememberedDevices.Count > 0;

    public bool HasNoRememberedDevices => RememberedDevices.Count == 0;

    /// <summary>For example "COMrade 0.2.0".</summary>
    public string Version { get; } =
        $"COMrade {typeof(SettingsTabViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]}";

    public Uri RepositoryUri { get; } = new("https://github.com/antoniovazquezblanco/Comrade");

    public Uri IssuesUri { get; } = new("https://github.com/antoniovazquezblanco/Comrade/issues");

    [RelayCommand]
    private void Forget(string key) => _devices.Forget(key);

    [RelayCommand]
    private void ForgetAll() => _devices.ForgetAll();

    [RelayCommand]
    private static void OpenThirdPartyNotices() =>
        Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "ThirdPartyNotices.txt")) { UseShellExecute = true });

    public override void Dispose() => _devices.Changed -= OnDevicesChanged;

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
