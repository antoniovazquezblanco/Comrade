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
    private readonly AppSettingsStore _settings;
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private readonly HashSet<string> _arrivedWhileRunning = new(StringComparer.OrdinalIgnoreCase);
    private bool _enumerated;
    private bool _disposed;

    public PortListViewModel(PortList ports, DeviceSettingsStore store, AppSettingsStore settings)
    {
        _ports = ports;
        _store = store;
        _settings = settings;

        // Port list events arrive on the watcher thread; the list is rebuilt on the UI thread.
        _ports.PortArrived += OnPortArrived;
        _ports.PortRemoved += OnPortRemoved;
        _ports.EnumerationCompleted += OnEnumerationCompleted;
        // "Last used" and "will use" details follow the device and new-device settings.
        _store.Changed += OnSettingsChanged;
        _settings.Changed += OnSettingsChanged;
        Rebuild();
    }

    public ObservableCollection<PortItemViewModel> Ports { get; } = [];

    public bool HasNoPorts => Ports.Count == 0;

    /// <summary>Shown instead of the list while there are no ports.</summary>
    public string NoPortsMessage => _enumerated
        ? "No serial ports found · plug in an adapter and it will appear here"
        : "Looking for serial ports…";

    /// <summary>Stops updating the list; updates already queued are dropped.</summary>
    public void Dispose()
    {
        _disposed = true;
        _ports.PortArrived -= OnPortArrived;
        _ports.PortRemoved -= OnPortRemoved;
        _ports.EnumerationCompleted -= OnEnumerationCompleted;
        _store.Changed -= OnSettingsChanged;
        _settings.Changed -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => _ui.TryEnqueue(Rebuild);

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
            var detail = port.IsRefusedByDriver
                ? "Its driver refuses this adapter · Fix driver shows how to use it"
                : device is null
                    ? $"{(isNew ? "Plugged in just now" : DisplayText.Kind(port))} · will use {DisplayText.Settings(_settings.Current.NewDevice.Serial)}"
                    : device.Settings.Name is null
                        ? $"Last used · {DisplayText.Settings(device.Settings.Serial)}"
                        : $"{port.Description} · last used {DisplayText.Settings(device.Settings.Serial)}";
            Ports.Add(new PortItemViewModel(port, device?.Settings.Name, detail, isNew, device is not null && device == lastUsed));
        }

        OnPropertyChanged(nameof(HasNoPorts));
        OnPropertyChanged(nameof(NoPortsMessage));
    }
}
