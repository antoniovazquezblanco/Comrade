using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace COMrade.Terminal;

/// <summary>The fixed-pitch fonts installed on this machine, for the terminal font setting.</summary>
public static class MonospaceFonts
{
    private const byte DefaultCharset = 1;
    private const byte FixedPitch = 1;

    private static readonly Lazy<IReadOnlyList<string>> Installed = new(Enumerate);

    public static IReadOnlyList<string> All => Installed.Value;

    private static IReadOnlyList<string> Enumerate()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var screen = GetDC(IntPtr.Zero);
        try
        {
            var filter = new LogFont { Charset = DefaultCharset };
            EnumFontFamiliesEx(screen, ref filter, (ref LogFont font, IntPtr metrics, uint type, IntPtr data) =>
            {
                // Fixed pitch, and not the vertical "@" variants of East Asian fonts.
                if ((font.PitchAndFamily & 0x3) == FixedPitch && !font.FaceName.StartsWith('@'))
                    names.Add(font.FaceName);
                return 1;
            }, IntPtr.Zero, 0);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }

        return names.ToList();
    }

    private delegate int FontCallback(ref LogFont font, IntPtr metrics, uint type, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LogFont
    {
        public int Height;
        public int Width;
        public int Escapement;
        public int Orientation;
        public int Weight;
        public byte Italic;
        public byte Underline;
        public byte StrikeOut;
        public byte Charset;
        public byte OutPrecision;
        public byte ClipPrecision;
        public byte Quality;
        public byte PitchAndFamily;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumFontFamiliesExW")]
    private static extern int EnumFontFamiliesEx(IntPtr dc, ref LogFont filter, FontCallback callback, IntPtr data, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
}
