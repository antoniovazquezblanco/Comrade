using COMrade.Core.Ports;
using COMrade.Core.Serial;
using COMrade.Core.Settings;
using COMrade.Core.Terminal;

namespace COMrade.Core.Tests.Settings;

public sealed class DeviceSettingsStoreTests : IDisposable
{
    private static readonly DeviceIdentity Ch340 = new("COM5", 0x1A86, 0x7523, Location: "USB(3)");
    private static readonly DeviceIdentity Ftdi = new("COM3", 0x0403, 0x6001, "A50285BIA");
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"comrade-tests-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(_folder, "devices.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Unknown_device_has_no_remembered_settings()
    {
        Assert.Null(new DeviceSettingsStore(FilePath).Find(Ch340));
    }

    [Fact]
    public void Saved_settings_survive_a_restart()
    {
        var settings = new DeviceSettings(
            SerialSettings.Default with { BaudRate = 9600, Parity = Parity.Even, SendLineEnding = LineEnding.CrLf },
            new TerminalSettings(LocalEcho: true, NewLineOnLf: false, NewLineOnCr: true),
            ReopenAutomatically: false);

        new DeviceSettingsStore(FilePath).Save(Ch340, settings, Now);
        var remembered = new DeviceSettingsStore(FilePath).Find(Ch340);

        Assert.Equal(settings, remembered?.Settings);
        Assert.Equal(Now, remembered?.LastUsed);
    }

    [Fact]
    public void Devices_are_remembered_by_identity_not_com_number()
    {
        var store = new DeviceSettingsStore(FilePath);
        store.Save(Ftdi, DeviceSettings.Default, Now);

        Assert.NotNull(store.Find(Ftdi with { PortName = "COM9" }));
    }

    [Fact]
    public void Each_device_keeps_its_own_settings()
    {
        var store = new DeviceSettingsStore(FilePath);
        var slow = DeviceSettings.Default with { Serial = SerialSettings.Default with { BaudRate = 9600 } };

        store.Save(Ch340, slow, Now);
        store.Save(Ftdi, DeviceSettings.Default, Now);

        Assert.Equal(9600, store.Find(Ch340)?.Settings.Serial.BaudRate);
        Assert.Equal(115200, store.Find(Ftdi)?.Settings.Serial.BaudRate);
    }

    [Fact]
    public void Saving_again_replaces_the_previous_settings()
    {
        var store = new DeviceSettingsStore(FilePath);
        store.Save(Ch340, DeviceSettings.Default, Now);

        store.Save(Ch340, DeviceSettings.Default with { ReopenAutomatically = false }, Now.AddHours(1));

        var remembered = new DeviceSettingsStore(FilePath).Find(Ch340);
        Assert.False(remembered?.Settings.ReopenAutomatically);
        Assert.Equal(Now.AddHours(1), remembered?.LastUsed);
    }

    [Fact]
    public void Unreadable_file_is_set_aside_and_the_store_starts_empty()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ not json");

        var store = new DeviceSettingsStore(FilePath);

        Assert.Null(store.Find(Ch340));
        Assert.True(File.Exists(FilePath + ".unreadable"));
        store.Save(Ch340, DeviceSettings.Default, Now);
        Assert.NotNull(new DeviceSettingsStore(FilePath).Find(Ch340));
    }
}
