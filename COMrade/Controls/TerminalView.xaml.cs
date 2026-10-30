using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Terminal.Wpf;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.ApplicationModel.DataTransfer;

namespace COMrade.Controls;

/// <summary>Interactive terminal: Windows Terminal's control fed by a device instead of a shell.</summary>
/// <remarks>
/// The terminal engine stays behind this control so it can be replaced without touching the rest
/// of the app (see the terminal engine decision). The engine draws in a native child window, so
/// nothing in the app can be shown on top of the terminal area.
/// </remarks>
public sealed partial class TerminalView : UserControl
{
    private const string CtrlC = "\u0003";
    private const string CtrlV = "\u0016";
    private const string TerminalFont = "Cascadia Mono";
    private const short TerminalFontSize = 11;

    private readonly TerminalConnection _connection = new();
    private bool _themed;

    public TerminalView()
    {
        InitializeComponent();
        _connection.InputReceived += OnTerminalInput;
        Term.Connection = _connection;
        Loaded += (_, _) =>
        {
            ApplyTheme();
            ShowScrollBar();
        };
    }

    /// <summary>Text typed or pasted by the user.</summary>
    public event EventHandler<string>? InputReceived;

    /// <summary>Shows bytes received from the device. Can be called from any thread.</summary>
    public void Write(ReadOnlySpan<byte> data) => _connection.Write(data);

    /// <summary>Clears the screen and the scrollback.</summary>
    public void Clear() => _connection.Write("\e[2J\e[3J\e[H"u8);

    public void FocusTerminal() => Term.Focus(FocusState.Programmatic);

    // Ctrl+C copies when text is selected and otherwise reaches the device as 0x03, like in
    // Windows Terminal; Ctrl+V pastes the clipboard as typed text.
    private void OnTerminalInput(object? sender, string data)
    {
        if (data == CtrlC && Term.GetSelectedText() is { Length: > 0 } selection)
        {
            CopyToClipboard(selection);
            return;
        }

        if (data == CtrlV)
        {
            _ = PasteFromClipboardAsync();
            return;
        }

        InputReceived?.Invoke(this, data);
    }

    private void ApplyTheme()
    {
        if (_themed)
            return;
        _themed = true;

        var theme = new TerminalTheme
        {
            DefaultBackground = Rgb(0x12, 0x12, 0x12),
            DefaultForeground = Rgb(0xCC, 0xCC, 0xCC),
            DefaultSelectionBackground = Rgb(0x26, 0x4F, 0x78),
            CursorStyle = CursorStyle.BlinkingBlock,
            // Campbell, Windows Terminal's default scheme.
            ColorTable =
            [
                Rgb(0x0C, 0x0C, 0x0C), Rgb(0xC5, 0x0F, 0x1F), Rgb(0x13, 0xA1, 0x0E), Rgb(0xC1, 0x9C, 0x00),
                Rgb(0x00, 0x37, 0xDA), Rgb(0x88, 0x17, 0x98), Rgb(0x3A, 0x96, 0xDD), Rgb(0xCC, 0xCC, 0xCC),
                Rgb(0x76, 0x76, 0x76), Rgb(0xE7, 0x48, 0x56), Rgb(0x16, 0xC6, 0x0C), Rgb(0xF9, 0xF1, 0xA5),
                Rgb(0x3B, 0x78, 0xFF), Rgb(0xB4, 0x00, 0x9E), Rgb(0x61, 0xD6, 0xD6), Rgb(0xF2, 0xF2, 0xF2),
            ],
        };
        Term.SetTheme(theme, TerminalFont, TerminalFontSize, System.Drawing.Color.FromArgb(0x12, 0x12, 0x12));
    }

    // The control's scroll bar for the scrollback is a plain WinUI ScrollBar, which draws nothing
    // until it is given an indicator mode.
    private void ShowScrollBar()
    {
        if (Term.FindName("scrollbar") is ScrollBar scrollBar)
            scrollBar.IndicatorMode = ScrollingIndicatorMode.MouseIndicator;
    }

    // The control takes colours as Win32 COLORREF values: 0x00BBGGRR.
    private static uint Rgb(byte red, byte green, byte blue) => (uint)(red | (green << 8) | (blue << 16));

    private static void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        try
        {
            Clipboard.SetContent(package);
        }
        catch (COMException)
        {
            // Another program holds the clipboard; the copy is lost, as in any app.
        }
    }

    private async Task PasteFromClipboardAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
                InputReceived?.Invoke(this, (await content.GetTextAsync()).ReplaceLineEndings("\r"));
        }
        catch (COMException)
        {
            // The clipboard is busy or holds no text.
        }
    }
}
