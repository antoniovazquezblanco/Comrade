namespace COMrade.Core.Ports;

/// <summary>Live list of the COM ports on this machine, kept up to date by <see cref="PortWatcher"/>.</summary>
/// <remarks>
/// Events are raised in the order the changes happen, on the thread that reported the change.
/// Handlers must return quickly and must not block waiting for another thread.
/// </remarks>
public sealed class PortList
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, SerialPortInfo> _ports = new(StringComparer.OrdinalIgnoreCase);
    private bool _enumerationComplete;

    public event EventHandler<SerialPortInfo>? PortArrived;

    public event EventHandler<SerialPortInfo>? PortRemoved;

    public event EventHandler? EnumerationCompleted;

    /// <summary>Current ports: USB first and Bluetooth last (<see cref="PortKind"/>), then by COM number.</summary>
    public IReadOnlyList<SerialPortInfo> Ports
    {
        get
        {
            lock (_lock)
            {
                return _ports.Values
                    .OrderBy(port => port.Kind)
                    .ThenBy(port => port.PortNumber)
                    .ThenBy(port => port.DeviceId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
    }

    /// <summary>True once the ports present at start have all been reported.</summary>
    public bool IsEnumerationComplete
    {
        get
        {
            lock (_lock)
                return _enumerationComplete;
        }
    }

    // Events are raised while holding the lock so that subscribers see changes in the order they happened.

    /// <summary>Records a device that appeared or whose details changed.</summary>
    public void AddOrUpdate(PortDevice device)
    {
        var port = SerialPortInfo.FromDevice(device);

        lock (_lock)
        {
            _ports.TryGetValue(device.DeviceId, out var existing);
            if (existing == port)
                return;

            if (existing is not null)
            {
                _ports.Remove(device.DeviceId);
                PortRemoved?.Invoke(this, existing);
            }

            if (port is not null)
            {
                _ports[device.DeviceId] = port;
                PortArrived?.Invoke(this, port);
            }
        }
    }

    /// <summary>Records that the device with this ID disappeared.</summary>
    public void Remove(string deviceId)
    {
        lock (_lock)
        {
            if (_ports.Remove(deviceId, out var existing))
                PortRemoved?.Invoke(this, existing);
        }
    }

    /// <summary>Records that all devices present at start have been reported.</summary>
    public void CompleteEnumeration()
    {
        lock (_lock)
        {
            if (_enumerationComplete)
                return;
            _enumerationComplete = true;
            EnumerationCompleted?.Invoke(this, EventArgs.Empty);
        }
    }
}
