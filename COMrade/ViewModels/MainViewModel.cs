using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using COMrade.Core.Ports;
using COMrade.Core.Sessions;
using COMrade.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace COMrade.ViewModels;

/// <summary>The window: the port list of the empty state, one tab per device and the settings tab.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly PortSessions _sessions;
    private readonly DeviceSettingsStore _devices;
    private readonly AppSettingsStore _settings;

    public MainViewModel(PortListViewModel portList, PortSessions sessions, DeviceSettingsStore devices, AppSettingsStore settings)
    {
        PortList = portList;
        _sessions = sessions;
        _devices = devices;
        _settings = settings;
    }

    public PortListViewModel PortList { get; }

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial TabViewModel? SelectedTab { get; set; }

    public bool ShowEmptyState => Tabs.Count == 0;

    public string Title => SelectedTab?.WindowTitle ?? "COMrade";

    /// <summary>
    /// Shows the device's tab, creating it with the settings remembered for the device, or the
    /// new-device settings for a device not seen before.
    /// </summary>
    public DeviceTabViewModel OpenTab(SerialPortInfo port)
    {
        var tab = Tabs.OfType<DeviceTabViewModel>().FirstOrDefault(tab => tab.Session.Port.Identity.IsSameDeviceAs(port.Identity));
        if (tab is null)
        {
            var settings = _devices.Find(port.Identity)?.Settings ?? _settings.Current.NewDevice;
            tab = new DeviceTabViewModel(_sessions.GetOrCreate(port, settings.Serial), settings, _devices);
            Add(tab);
        }

        SelectedTab = tab;
        return tab;
    }

    /// <summary>Shows the settings tab, creating it the first time.</summary>
    public void OpenSettings()
    {
        var tab = Tabs.OfType<SettingsTabViewModel>().FirstOrDefault();
        if (tab is null)
        {
            tab = new SettingsTabViewModel(_settings, _devices);
            Add(tab);
        }

        SelectedTab = tab;
    }

    /// <summary>Removes the tab; a device tab also closes its port.</summary>
    public async Task CloseTabAsync(TabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return;

        tab.PropertyChanged -= OnTabPropertyChanged;
        tab.Dispose();
        Tabs.RemoveAt(index);
        if (SelectedTab is null || SelectedTab == tab)
            SelectedTab = Tabs.Count > 0 ? Tabs[Math.Min(index, Tabs.Count - 1)] : null;
        OnPropertyChanged(nameof(ShowEmptyState));

        if (tab is DeviceTabViewModel device)
            await _sessions.CloseAsync(device.Session);
    }

    /// <summary>Closes every port when the window closes.</summary>
    public async Task CloseAllAsync()
    {
        foreach (var tab in Tabs.ToList())
        {
            tab.PropertyChanged -= OnTabPropertyChanged;
            tab.Dispose();
        }

        await Task.WhenAll(_sessions.Sessions.ToList().Select(_sessions.CloseAsync));
    }

    private void Add(TabViewModel tab)
    {
        tab.PropertyChanged += OnTabPropertyChanged;
        Tabs.Add(tab);
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    // The COM number of a device can change while its tab is open.
    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == SelectedTab)
            OnPropertyChanged(nameof(Title));
    }
}
