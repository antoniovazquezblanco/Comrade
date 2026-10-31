using COMrade.Core.Ports;

namespace COMrade.Core.Settings;

/// <summary>
/// Remembers the settings of each device by its <see cref="DeviceIdentity.Key"/>, in a JSON file.
/// </summary>
/// <remarks>The file is read once and rewritten on every change (see <see cref="JsonFile"/>).</remarks>
public sealed class DeviceSettingsStore
{
    private readonly string _path;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, RememberedDevice> _devices;

    public DeviceSettingsStore(string path)
    {
        _path = path;
        _devices = new(JsonFile.Read<Dictionary<string, RememberedDevice>>(path) ?? [], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A device was saved or forgotten. Raised on the thread that made the change.</summary>
    public event EventHandler? Changed;

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "COMrade", "devices.json");

    /// <summary>Every remembered device by identity key, most recently used first.</summary>
    public IReadOnlyList<KeyValuePair<string, RememberedDevice>> Devices
    {
        get
        {
            lock (_lock)
                return _devices.OrderByDescending(device => device.Value.LastUsed).ToList();
        }
    }

    /// <summary>The device's remembered settings, or null for a device not seen before.</summary>
    public RememberedDevice? Find(DeviceIdentity identity)
    {
        lock (_lock)
            return _devices.GetValueOrDefault(identity.Key);
    }

    /// <param name="description">Device name to show in the settings; the previous one is kept when null.</param>
    public void Save(DeviceIdentity identity, DeviceSettings settings, DateTimeOffset usedAt, string? description = null)
    {
        lock (_lock)
        {
            description ??= _devices.GetValueOrDefault(identity.Key)?.Description;
            _devices[identity.Key] = new RememberedDevice(settings, usedAt, description, identity.PortName);
            JsonFile.Write(_path, _devices);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forgets one device by its identity key; it gets the new-device defaults next time.</summary>
    public void Forget(string key)
    {
        lock (_lock)
        {
            if (!_devices.Remove(key))
                return;
            JsonFile.Write(_path, _devices);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ForgetAll()
    {
        lock (_lock)
        {
            if (_devices.Count == 0)
                return;
            _devices.Clear();
            JsonFile.Write(_path, _devices);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
