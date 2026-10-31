using System.Text.Json.Serialization;

namespace COMrade.Core.Terminal;

/// <summary>How every terminal looks: font, colours and cursor.</summary>
/// <param name="ColorScheme">Name of one of <see cref="Terminal.ColorScheme.All"/>.</param>
public sealed record TerminalAppearance(
    string FontFamily = TerminalAppearance.DefaultFontFamily,
    int FontSize = TerminalAppearance.DefaultFontSize,
    string ColorScheme = "Campbell",
    CursorShape Cursor = CursorShape.Block)
{
    public const string DefaultFontFamily = "Cascadia Mono";
    public const int DefaultFontSize = 11;
    public const int MinimumFontSize = 6;
    public const int MaximumFontSize = 36;

    public static TerminalAppearance Default { get; } = new();

    /// <summary>The colours to use; a scheme no longer known falls back to the default.</summary>
    [JsonIgnore]
    public ColorScheme Scheme => Terminal.ColorScheme.Find(ColorScheme);
}
