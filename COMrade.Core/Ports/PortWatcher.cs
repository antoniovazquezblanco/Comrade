using System.Text.RegularExpressions;
using Microsoft.Win32;
using Windows.Devices.Enumeration;

namespace COMrade.Core.Ports;

/// <summary>Watches Windows' "Ports (COM &amp; LPT)" device class and keeps a <see cref="PortList"/> up to date.</summary>
public sealed partial class PortWatcher : IDisposable
{
    private const string PortsClassGuid = "{4d36e978-e325-11ce-bfc1-08002be10318}";
    private const string ParentProperty = "System.Devices.Parent";
    private const string LocationPathsProperty = "System.Devices.LocationPaths";
    private const string PresentProperty = "System.Devices.Present";

    private readonly PortList _ports;
    private readonly DeviceWatcher _watcher;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, DeviceInformation> _devices = new(StringComparer.OrdinalIgnoreCase);

    public PortWatcher(PortList ports)
    {
        _ports = ports;
        _watcher = DeviceInformation.CreateWatcher(
            $"System.Devices.ClassGuid:=\"{PortsClassGuid}\" AND System.Devices.Present:=System.StructuredQueryType.Boolean#True",
            [ParentProperty, LocationPathsProperty, PresentProperty],
            DeviceInformationKind.Device);

        _watcher.Added += OnAdded;
        _watcher.Updated += OnUpdated;
        _watcher.Removed += OnRemoved;
        _watcher.EnumerationCompleted += OnEnumerationCompleted;
    }

    public void Start() => _watcher.Start();

    public void Dispose()
    {
        _watcher.Added -= OnAdded;
        _watcher.Updated -= OnUpdated;
        _watcher.Removed -= OnRemoved;
        _watcher.EnumerationCompleted -= OnEnumerationCompleted;

        if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            _watcher.Stop();
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation device)
    {
        lock (_lock)
            _devices[device.Id] = device;

        Report(device);
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        DeviceInformation? device;
        lock (_lock)
        {
            if (!_devices.TryGetValue(update.Id, out device))
                return;
            device.Update(update);
        }

        if (device.Properties.TryGetValue(PresentProperty, out var present) && present is false)
            OnRemoved(sender, update);
        else
            Report(device);
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_lock)
            _devices.Remove(update.Id);

        _ports.Remove(update.Id);
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args) => _ports.CompleteEnumeration();

    private void Report(DeviceInformation device)
    {
        device.Properties.TryGetValue(ParentProperty, out var parent);
        device.Properties.TryGetValue(LocationPathsProperty, out var locationPaths);

        _ports.AddOrUpdate(new PortDevice(
            device.Id,
            ReadPortName(device),
            device.Name,
            parent as string,
            (locationPaths as string[])?.FirstOrDefault()));
    }

    // The port name lives in the device's registry parameters; the "(COMx)" at the end of
    // the friendly name is only a fallback because drivers are free to name devices differently.
    private static string? ReadPortName(DeviceInformation device)
    {
        using var parameters = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{device.Id}\Device Parameters");
        if (parameters?.GetValue("PortName") is string portName && portName.Length > 0)
            return portName;

        var match = PortNameInFriendlyName().Match(device.Name);
        return match.Success ? match.Groups["port"].Value : null;
    }

    [GeneratedRegex(@"\((?<port>(COM|LPT)\d+)\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PortNameInFriendlyName();
}
