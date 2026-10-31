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

/// <summary>The window: the port list of the empty state and one tab per device.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly PortSessions _sessions;
    private readonly DeviceSettingsStore _store;

    public MainViewModel(PortListViewModel portList, PortSessions sessions, DeviceSettingsStore store)
    {
        PortList = portList;
        _sessions = sessions;
        _store = store;
    }

    public PortListViewModel PortList { get; }

    public ObservableCollection<DeviceTabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial DeviceTabViewModel? SelectedTab { get; set; }

    public bool ShowEmptyState => Tabs.Count == 0;

    public string Title => SelectedTab is { } tab ? $"COMrade - {tab.PortName}" : "COMrade";

    /// <summary>Shows the device's tab, creating it with the settings remembered for the device.</summary>
    public DeviceTabViewModel OpenTab(SerialPortInfo port)
    {
        var tab = Tabs.FirstOrDefault(tab => tab.Session.Port.Identity.IsSameDeviceAs(port.Identity));
        if (tab is null)
        {
            var settings = _store.Find(port.Identity)?.Settings ?? DeviceSettings.Default;
            tab = new DeviceTabViewModel(_sessions.GetOrCreate(port, settings.Serial), settings, _store);
            tab.PropertyChanged += OnTabPropertyChanged;
            Tabs.Add(tab);
            OnPropertyChanged(nameof(ShowEmptyState));
        }

        SelectedTab = tab;
        return tab;
    }

    /// <summary>Closes the device's port and removes its tab.</summary>
    public async Task CloseTabAsync(DeviceTabViewModel tab)
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

        await _sessions.CloseAsync(tab.Session);
        PortList.Refresh();
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

    // The COM number of a device can change while its tab is open.
    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == SelectedTab)
            OnPropertyChanged(nameof(Title));
    }
}
