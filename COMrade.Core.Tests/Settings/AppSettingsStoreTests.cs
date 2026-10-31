using COMrade.Core.Serial;
using COMrade.Core.Settings;

namespace COMrade.Core.Tests.Settings;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"comrade-tests-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void First_run_uses_the_defaults()
    {
        Assert.Equal(AppSettings.Default, new AppSettingsStore(FilePath).Current);
    }

    [Fact]
    public void Saved_settings_survive_a_restart()
    {
        var settings = new AppSettings(DeviceSettings.Default with { Serial = SerialSettings.Default with { BaudRate = 9600 } });

        new AppSettingsStore(FilePath).Save(settings);

        Assert.Equal(settings, new AppSettingsStore(FilePath).Current);
    }

    [Fact]
    public void Saving_announces_the_change_once()
    {
        var store = new AppSettingsStore(FilePath);
        var changes = 0;
        store.Changed += (_, _) => changes++;
        var settings = new AppSettings(DeviceSettings.Default with { ReopenAutomatically = false });

        store.Save(settings);
        store.Save(settings);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void Unreadable_file_is_set_aside_and_the_defaults_are_used()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ not json");

        Assert.Equal(AppSettings.Default, new AppSettingsStore(FilePath).Current);
        Assert.True(File.Exists(FilePath + ".unreadable"));
    }

    [Fact]
    public void Tab_mode_is_remembered()
    {
        new AppSettingsStore(FilePath).Save(AppSettings.Default with { TabMode = TabMode.Compact });

        Assert.Equal(TabMode.Compact, new AppSettingsStore(FilePath).Current.TabMode);
    }

    [Fact]
    public void Settings_saved_before_the_tab_mode_existed_scroll()
    {
        new AppSettingsStore(FilePath).Save(AppSettings.Default with { NewDevice = DeviceSettings.Default with { ReopenAutomatically = false } });
        File.WriteAllText(FilePath, File.ReadAllText(FilePath).Replace("\"tabMode\": \"Scrolling\"", "\"unknown\": 0"));

        var settings = new AppSettingsStore(FilePath).Current;

        Assert.Equal(TabMode.Scrolling, settings.TabMode);
        Assert.False(settings.NewDevice.ReopenAutomatically);
    }
}
