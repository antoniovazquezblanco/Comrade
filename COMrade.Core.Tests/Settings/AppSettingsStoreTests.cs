using COMrade.Core.Serial;
using COMrade.Core.Settings;
using COMrade.Core.Shortcuts;
using Windows.System;

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
        new AppSettingsStore(FilePath).Save(AppSettings.Default with { TabMode = TabMode.Scrolling });

        Assert.Equal(TabMode.Scrolling, new AppSettingsStore(FilePath).Current.TabMode);
    }

    [Fact]
    public void Compact_tabs_are_the_default()
    {
        Assert.Equal(TabMode.Compact, new AppSettingsStore(FilePath).Current.TabMode);
    }

    [Fact]
    public void Settings_saved_before_the_tab_mode_existed_get_compact_tabs()
    {
        new AppSettingsStore(FilePath).Save(AppSettings.Default with { NewDevice = DeviceSettings.Default with { ReopenAutomatically = false } });
        File.WriteAllText(FilePath, File.ReadAllText(FilePath).Replace("\"tabMode\": \"Compact\"", "\"unknown\": 0"));

        var settings = new AppSettingsStore(FilePath).Current;

        Assert.Equal(TabMode.Compact, settings.TabMode);
        Assert.False(settings.NewDevice.ReopenAutomatically);
    }

    [Fact]
    public void Changed_shortcuts_are_remembered()
    {
        var keymap = Keymap.Default.With(ShortcutAction.CloseTab, new KeyChord(VirtualKey.Q, Shift: true)).With(ShortcutAction.Copy, null);

        new AppSettingsStore(FilePath).Save(AppSettings.Default with { Shortcuts = keymap.Changes });

        // As written, for editing by hand.
        Assert.Contains("\"CloseTab\": \"Ctrl+Shift+Q\"", File.ReadAllText(FilePath));
        var read = new AppSettingsStore(FilePath).Current.Keymap;
        Assert.Equal(new KeyChord(VirtualKey.Q, Shift: true), read.KeysFor(ShortcutAction.CloseTab));
        Assert.Null(read.KeysFor(ShortcutAction.Copy));
    }

    [Fact]
    public void Settings_saved_before_shortcuts_existed_get_the_default_shortcuts()
    {
        new AppSettingsStore(FilePath).Save(AppSettings.Default with { TabMode = TabMode.Scrolling });
        var file = File.ReadAllText(FilePath);
        Assert.Contains("\"shortcuts\": null", file);
        File.WriteAllText(FilePath, file.Replace("\"shortcuts\": null", "\"unknown\": 0"));

        var settings = new AppSettingsStore(FilePath).Current;

        Assert.False(settings.Keymap.HasChanges);
        Assert.Equal(TabMode.Scrolling, settings.TabMode);
    }
}
