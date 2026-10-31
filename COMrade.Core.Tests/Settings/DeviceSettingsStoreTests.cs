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

    [Fact]
    public void The_device_name_and_port_are_remembered_for_the_settings_page()
    {
        var store = new DeviceSettingsStore(FilePath);
        store.Save(Ch340, DeviceSettings.Default, Now, "USB-SERIAL CH340");
        store.Save(Ch340, DeviceSettings.Default, Now.AddHours(1));

        var remembered = new DeviceSettingsStore(FilePath).Find(Ch340);
        Assert.Equal("USB-SERIAL CH340", remembered?.Description);
        Assert.Equal("COM5", remembered?.PortName);
    }

    [Fact]
    public void Devices_are_listed_most_recently_used_first()
    {
        var store = new DeviceSettingsStore(FilePath);
        store.Save(Ch340, DeviceSettings.Default, Now);
        store.Save(Ftdi, DeviceSettings.Default, Now.AddHours(1));

        Assert.Equal([Ftdi.Key, Ch340.Key], store.Devices.Select(device => device.Key));
    }

    [Fact]
    public void A_forgotten_device_gets_the_defaults_again()
    {
        var store = new DeviceSettingsStore(FilePath);
        store.Save(Ch340, DeviceSettings.Default, Now);
        store.Save(Ftdi, DeviceSettings.Default, Now);

        store.Forget(Ch340.Key);

        Assert.Null(new DeviceSettingsStore(FilePath).Find(Ch340));
        Assert.NotNull(new DeviceSettingsStore(FilePath).Find(Ftdi));
    }

    [Fact]
    public void Forget_all_clears_every_device()
    {
        var store = new DeviceSettingsStore(FilePath);
        store.Save(Ch340, DeviceSettings.Default, Now);
        store.Save(Ftdi, DeviceSettings.Default, Now);

        store.ForgetAll();

        Assert.Empty(new DeviceSettingsStore(FilePath).Devices);
    }

    [Fact]
    public void Changes_are_announced()
    {
        var store = new DeviceSettingsStore(FilePath);
        var changes = 0;
        store.Changed += (_, _) => changes++;

        store.Save(Ch340, DeviceSettings.Default, Now);
        store.Forget(Ch340.Key);
        store.Forget(Ch340.Key);
        store.ForgetAll();

        Assert.Equal(2, changes);
    }

    [Fact]
    public void A_device_keeps_the_name_given_to_its_tab()
    {
        new DeviceSettingsStore(FilePath).Save(Ch340, DeviceSettings.Default with { Name = "Router console" }, Now);

        Assert.Equal("Router console", new DeviceSettingsStore(FilePath).Find(Ch340)?.Settings.Name);
    }

    [Fact]
    public void Devices_saved_before_names_existed_have_no_name()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, $$"""
            {
              "{{Ch340.Key}}": {
                "settings": {
                  "serial": { "baudRate": 9600, "dataBits": 8, "parity": "None", "stopBits": "One", "flowControl": "None", "sendLineEnding": "Cr" },
                  "terminal": { "localEcho": false, "newLineOnLf": true, "newLineOnCr": false },
                  "reopenAutomatically": true
                },
                "lastUsed": "2026-10-31T12:00:00+00:00"
              }
            }
            """);

        var remembered = new DeviceSettingsStore(FilePath).Find(Ch340);

        Assert.Equal(9600, remembered?.Settings.Serial.BaudRate);
        Assert.Null(remembered?.Settings.Name);
    }
}
