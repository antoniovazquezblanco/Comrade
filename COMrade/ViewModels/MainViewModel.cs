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

/// <summary>A window: the port list of the empty state, one tab per device and the settings tab.</summary>
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
        _settings.Changed += OnSettingsChanged;
    }

    public PortListViewModel PortList { get; }

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial TabViewModel? SelectedTab { get; set; }

    public bool ShowEmptyState => Tabs.Count == 0;

    public string Title => SelectedTab?.WindowTitle ?? "COMrade";

    partial void OnSelectedTabChanged(TabViewModel? value) => UpdateTabTitles();

    /// <summary>The device's tab in this window, if it has one.</summary>
    public DeviceTabViewModel? FindTab(DeviceIdentity identity) =>
        Tabs.OfType<DeviceTabViewModel>().FirstOrDefault(tab => tab.Session.Port.Identity.IsSameDeviceAs(identity));

    /// <summary>
    /// Shows the device's tab, creating it with the settings remembered for the device, or the
    /// new-device settings for a device not seen before.
    /// </summary>
    public DeviceTabViewModel OpenTab(SerialPortInfo port)
    {
        var tab = FindTab(port.Identity);
        if (tab is null)
        {
            var settings = _devices.Find(port.Identity)?.Settings ?? _settings.Current.NewDevice;
            tab = new DeviceTabViewModel(_sessions.GetOrCreate(port, settings.Serial), settings, _devices);
            Attach(tab, Tabs.Count);
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
            Attach(tab, Tabs.Count);
        }

        SelectedTab = tab;
    }

    /// <summary>Adds a tab, also one taken from another window with its port open, and selects it.</summary>
    public void Attach(TabViewModel tab, int index)
    {
        tab.PropertyChanged += OnTabPropertyChanged;
        Tabs.Insert(Math.Clamp(index, 0, Tabs.Count), tab);
        OnPropertyChanged(nameof(ShowEmptyState));
        SelectedTab = tab;
        UpdateTabTitles();
    }

    /// <summary>Gives the tab up, e.g. to another window; a device tab's port stays open.</summary>
    public void Detach(TabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return;

        tab.PropertyChanged -= OnTabPropertyChanged;
        Tabs.RemoveAt(index);
        if (SelectedTab is null || SelectedTab == tab)
            SelectedTab = Tabs.Count > 0 ? Tabs[Math.Min(index, Tabs.Count - 1)] : null;
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    /// <summary>Removes the tab; a device tab also closes its port.</summary>
    public async Task CloseTabAsync(TabViewModel tab)
    {
        if (!Tabs.Contains(tab))
            return;

        Detach(tab);
        tab.Dispose();
        if (tab is DeviceTabViewModel device)
            await _sessions.CloseAsync(device.Session);
    }

    /// <summary>Closes the ports of this window's tabs when the window closes.</summary>
    public async Task CloseAllAsync()
    {
        _settings.Changed -= OnSettingsChanged;
        var tabs = Tabs.ToList();
        foreach (var tab in tabs)
        {
            tab.PropertyChanged -= OnTabPropertyChanged;
            tab.Dispose();
        }

        await Task.WhenAll(tabs.OfType<DeviceTabViewModel>().Select(tab => _sessions.CloseAsync(tab.Session)));
    }

    // The settings page changes the tab mode; it saves on the UI thread.
    private void OnSettingsChanged(object? sender, EventArgs e) => UpdateTabTitles();

    // In compact mode, every tab but the selected one shows its short title.
    private void UpdateTabTitles()
    {
        var compact = _settings.Current.TabMode is TabMode.Compact;
        foreach (var tab in Tabs)
            tab.IsCompact = compact && tab != SelectedTab;
    }

    // The COM number of a device can change while its tab is open.
    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == SelectedTab)
            OnPropertyChanged(nameof(Title));
    }
}
