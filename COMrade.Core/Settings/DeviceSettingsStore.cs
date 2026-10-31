using System.Text.Json;
using System.Text.Json.Serialization;
using COMrade.Core.Ports;

namespace COMrade.Core.Settings;

/// <summary>
/// Remembers the settings of each device by its <see cref="DeviceIdentity.Key"/>, in a JSON file.
/// </summary>
/// <remarks>
/// The file is read once and rewritten on every save through a temporary file, so a crash never
/// leaves it half written. A file that cannot be read is set aside and COMrade starts afresh.
/// </remarks>
public sealed class DeviceSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, RememberedDevice> _devices;

    public DeviceSettingsStore(string path)
    {
        _path = path;
        _devices = Load(path);
    }

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "COMrade", "devices.json");

    /// <summary>The device's remembered settings, or null for a device not seen before.</summary>
    public RememberedDevice? Find(DeviceIdentity identity)
    {
        lock (_lock)
            return _devices.GetValueOrDefault(identity.Key);
    }

    public void Save(DeviceIdentity identity, DeviceSettings settings, DateTimeOffset usedAt)
    {
        lock (_lock)
        {
            _devices[identity.Key] = new RememberedDevice(settings, usedAt);

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_devices, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
    }

    private static Dictionary<string, RememberedDevice> Load(string path)
    {
        if (!File.Exists(path))
            return new(StringComparer.OrdinalIgnoreCase);

        try
        {
            var devices = JsonSerializer.Deserialize<Dictionary<string, RememberedDevice>>(File.ReadAllText(path), JsonOptions);
            return new(devices ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            File.Move(path, path + ".unreadable", overwrite: true);
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
