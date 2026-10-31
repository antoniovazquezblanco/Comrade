using COMrade.Core.Settings;
using COMrade.Core.Terminal;

namespace COMrade.Core.Tests.Terminal;

public sealed class TerminalAppearanceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"comrade-tests-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Every_scheme_has_the_16_ansi_colours_and_a_unique_name()
    {
        Assert.All(ColorScheme.All, scheme => Assert.Equal(16, scheme.Palette.Count));
        Assert.Equal(ColorScheme.All.Count, ColorScheme.All.Select(scheme => scheme.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void A_scheme_no_longer_known_falls_back_to_campbell()
    {
        Assert.Same(ColorScheme.Campbell, new TerminalAppearance(ColorScheme: "Removed scheme").Scheme);
        Assert.Equal("Solarized Dark", new TerminalAppearance(ColorScheme: "solarized dark").Scheme.Name);
    }

    [Fact]
    public void The_appearance_is_saved_with_the_settings()
    {
        var appearance = new TerminalAppearance("Consolas", 14, "One Half Dark", CursorShape.Bar);

        new AppSettingsStore(FilePath).Save(AppSettings.Default with { Terminal = appearance });

        Assert.Equal(appearance, new AppSettingsStore(FilePath).Current.TerminalOrDefault);
    }

    [Fact]
    public void Settings_saved_before_the_appearance_existed_use_the_defaults()
    {
        new AppSettingsStore(FilePath).Save(AppSettings.Default with { TabMode = TabMode.Compact, Terminal = null });

        var settings = new AppSettingsStore(FilePath).Current;

        Assert.Equal(TerminalAppearance.Default, settings.TerminalOrDefault);
        Assert.Equal(TabMode.Compact, settings.TabMode);
    }

    [Fact]
    public void Only_the_choices_are_saved_not_the_colours_they_lead_to()
    {
        new AppSettingsStore(FilePath).Save(AppSettings.Default with { Terminal = new TerminalAppearance(FontSize: 14) });

        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"colorScheme\"", json);
        Assert.DoesNotContain("\"scheme\"", json);
        Assert.DoesNotContain("terminalOrDefault", json);
    }
}
