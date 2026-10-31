using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using COMrade.Core.Ports;
using COMrade.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace COMrade.ViewModels;

/// <summary>The live list of ports offered for selection.</summary>
public sealed partial class PortListViewModel : ObservableObject, IDisposable
{
    private readonly PortList _ports;
    private readonly DeviceSettingsStore _store;
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private readonly HashSet<string> _arrivedWhileRunning = new(StringComparer.OrdinalIgnoreCase);
    private bool _enumerated;
    private bool _disposed;

    public PortListViewModel(PortList ports, DeviceSettingsStore store)
    {
        _ports = ports;
        _store = store;

        // Port list events arrive on the watcher thread; the list is rebuilt on the UI thread.
        _ports.PortArrived += OnPortArrived;
        _ports.PortRemoved += OnPortRemoved;
        _ports.EnumerationCompleted += OnEnumerationCompleted;
        Rebuild();
    }

    public ObservableCollection<PortItemViewModel> Ports { get; } = [];

    public bool HasPorts => Ports.Count > 0;

    public string Summary => !_enumerated
        ? "Looking for serial ports…"
        : Ports.Count switch
        {
            0 => "No serial ports found · plug in an adapter and it will appear here",
            1 => "1 port found · the list updates as devices are plugged in or removed",
            var count => $"{count} ports found · the list updates as devices are plugged in or removed",
        };

    /// <summary>Refreshes the "last used" details after a device's settings were saved.</summary>
    public void Refresh() => Rebuild();

    /// <summary>Stops updating the list; updates already queued are dropped.</summary>
    public void Dispose()
    {
        _disposed = true;
        _ports.PortArrived -= OnPortArrived;
        _ports.PortRemoved -= OnPortRemoved;
        _ports.EnumerationCompleted -= OnEnumerationCompleted;
    }

    private void OnPortArrived(object? sender, SerialPortInfo port) => _ui.TryEnqueue(() =>
    {
        if (_enumerated)
            _arrivedWhileRunning.Add(port.DeviceId);
        Rebuild();
    });

    private void OnPortRemoved(object? sender, SerialPortInfo port) => _ui.TryEnqueue(Rebuild);

    private void OnEnumerationCompleted(object? sender, EventArgs e) => _ui.TryEnqueue(() =>
    {
        _enumerated = true;
        Rebuild();
    });

    private void Rebuild()
    {
        if (_disposed)
            return;

        var ports = _ports.Ports;
        var remembered = ports.ToDictionary(port => port.DeviceId, port => _store.Find(port.Identity), StringComparer.OrdinalIgnoreCase);
        var lastUsed = remembered.Values.Where(device => device is not null).MaxBy(device => device!.LastUsed);

        Ports.Clear();
        foreach (var port in ports)
        {
            var device = remembered[port.DeviceId];
            var isNew = _arrivedWhileRunning.Contains(port.DeviceId);
            var detail = device is not null
                ? $"Last used · {DisplayText.Settings(device.Settings.Serial)}"
                : $"{(isNew ? "Plugged in just now" : DisplayText.Kind(port))} · will use {DisplayText.Settings(DeviceSettings.Default.Serial)}";
            Ports.Add(new PortItemViewModel(port, detail, isNew, device is not null && device == lastUsed));
        }

        OnPropertyChanged(nameof(HasPorts));
        OnPropertyChanged(nameof(Summary));
    }
}
