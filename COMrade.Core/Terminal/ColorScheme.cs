namespace COMrade.Core.Terminal;

/// <summary>
/// The terminal's colours: background, text, selection and the 16 ANSI colours that devices use
/// (black, red, green, yellow, blue, purple, cyan, white, then their bright versions). Colours are
/// 0xRRGGBB. The schemes are Windows Terminal's.
/// </summary>
public sealed record ColorScheme(string Name, uint Background, uint Foreground, uint SelectionBackground, IReadOnlyList<uint> Palette)
{
    private static readonly uint[] TangoPalette =
    [
        0x000000, 0xCC0000, 0x4E9A06, 0xC4A000, 0x3465A4, 0x75507B, 0x06989A, 0xD3D7CF,
        0x555753, 0xEF2929, 0x8AE234, 0xFCE94F, 0x729FCF, 0xAD7FA8, 0x34E2E2, 0xEEEEEC,
    ];

    private static readonly uint[] SolarizedPalette =
    [
        0x002B36, 0xDC322F, 0x859900, 0xB58900, 0x268BD2, 0xD33682, 0x2AA198, 0xEEE8D5,
        0x073642, 0xCB4B16, 0x586E75, 0x657B83, 0x839496, 0x6C71C4, 0x93A1A1, 0xFDF6E3,
    ];

    /// <summary>Windows Terminal's default scheme, and COMrade's.</summary>
    public static ColorScheme Campbell { get; } = new("Campbell", 0x0C0C0C, 0xCCCCCC, 0x264F78,
    [
        0x0C0C0C, 0xC50F1F, 0x13A10E, 0xC19C00, 0x0037DA, 0x881798, 0x3A96DD, 0xCCCCCC,
        0x767676, 0xE74856, 0x16C60C, 0xF9F1A5, 0x3B78FF, 0xB4009E, 0x61D6D6, 0xF2F2F2,
    ]);

    public static IReadOnlyList<ColorScheme> All { get; } =
    [
        Campbell,
        new("One Half Dark", 0x282C34, 0xDCDFE4, 0x3E4451,
        [
            0x282C34, 0xE06C75, 0x98C379, 0xE5C07B, 0x61AFEF, 0xC678DD, 0x56B6C2, 0xDCDFE4,
            0x5A6374, 0xE06C75, 0x98C379, 0xE5C07B, 0x61AFEF, 0xC678DD, 0x56B6C2, 0xDCDFE4,
        ]),
        new("One Half Light", 0xFAFAFA, 0x383A42, 0xBFCEFF,
        [
            0x383A42, 0xE45649, 0x50A14F, 0xC18301, 0x0184BC, 0xA626A4, 0x0997B3, 0xFAFAFA,
            0x4F525D, 0xDF6C75, 0x98C379, 0xE4C07A, 0x61AFEF, 0xC577DD, 0x56B5C1, 0xFFFFFF,
        ]),
        new("Solarized Dark", 0x002B36, 0x839496, 0x073642, SolarizedPalette),
        new("Solarized Light", 0xFDF6E3, 0x657B83, 0xEEE8D5, SolarizedPalette),
        new("Tango Dark", 0x000000, 0xD3D7CF, 0x3465A4, TangoPalette),
        new("Tango Light", 0xFFFFFF, 0x555753, 0xB5D5FF, TangoPalette),
        new("Vintage", 0x000000, 0xC0C0C0, 0x000080,
        [
            0x000000, 0x800000, 0x008000, 0x808000, 0x000080, 0x800080, 0x008080, 0xC0C0C0,
            0x808080, 0xFF0000, 0x00FF00, 0xFFFF00, 0x0000FF, 0xFF00FF, 0x00FFFF, 0xFFFFFF,
        ]),
    ];

    /// <summary>The scheme with this name; <see cref="Campbell"/> for a name no longer known.</summary>
    public static ColorScheme Find(string? name) =>
        All.FirstOrDefault(scheme => string.Equals(scheme.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Campbell;
}
