using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using COMrade.Core.Settings;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

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
