using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using COMrade.Core.Terminal;
using Microsoft.Terminal.Wpf;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
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

    private readonly TerminalConnection _connection = new();
    private bool _connected;
    private TerminalAppearance _appearance = TerminalAppearance.Default;
    private (int Width, int Height)? _cell;
    private double _cellScale;
    private ScrollBar? _scrollBar;

    public TerminalView()
    {
        InitializeComponent();
        _connection.InputReceived += OnTerminalInput;
        // The control shows or hides its native window by its own Visibility only: hidden with a
        // parent, it would still be shown again on top of whatever is visible at the next layout.
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Term.Visibility = Visibility);
        Loaded += (_, _) =>
        {
            ApplyAppearance();
            SetUpScrollBar();

            // Connected once loaded: the control starts the connection as soon as it is set and
            // drops output until its native terminal exists, which happens when it is loaded.
            if (!_connected)
            {
                Term.Connection = _connection;
                _connected = true;
            }
        };
    }

    /// <summary>Text typed or pasted by the user.</summary>
    public event EventHandler<string>? InputReceived;

    /// <summary>Raised when <see cref="IsScrolledUp"/> changes.</summary>
    public event EventHandler? IsScrolledUpChanged;

    /// <summary>Whether the user scrolled up, away from the newest output.</summary>
    public bool IsScrolledUp { get; private set; }

    /// <summary>Shows bytes received from the device. Can be called from any thread.</summary>
    public void Write(ReadOnlySpan<byte> data) => _connection.Write(data);

    /// <summary>Clears the screen and the scrollback.</summary>
    public void Clear() => _connection.Write("\e[2J\e[3J\e[H"u8);

    /// <summary>
    /// Shows <paramref name="output"/> in place of everything shown, as a new terminal would, and
    /// keeps the view where it was: for output that is the same text, in the same rows, as when
    /// only its colours change.
    /// </summary>
    public void Repaint(ReadOnlySpan<byte> output)
    {
        int? top = IsScrolledUp && _scrollBar is { } scrollBar ? (int)scrollBar.Value : null;

        // From the bottom: a view scrolled up stays on the same text as output arrives, and would
        // end at the top of the output shown again.
        ScrollToBottom();

        // A soft reset, the main screen rather than a full-screen program's, and nothing in the
        // screen or the scrollback.
        _connection.Write("\e[!p\e[?1049l\e[2J\e[3J\e[H"u8);
        _connection.Write(output);

        // Back where it was, at once and again when the scroll bar has caught up with the output,
        // which the control tells it of later.
        if (top is { } row)
        {
            Term.ScrollTo(row);
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => Term.ScrollTo(row));
        }
    }

    /// <summary>Characters in a row; 0 until the terminal has been laid out.</summary>
    public int Columns => Term.Columns;

    /// <summary>Rows of output the terminal keeps, those in view included.</summary>
    public int BufferRows => _scrollBar is { } scrollBar ? Math.Max(Term.Rows, (int)(scrollBar.Maximum + scrollBar.ViewportSize)) : Term.Rows;

    /// <summary>Scrolls row <paramref name="row"/> of the scrollback into view, a third of the way down.</summary>
    public void ShowRow(int row) => Term.ScrollTo(Math.Max(0, row - Term.Rows / 3));

    public void FocusTerminal() => Term.Focus(FocusState.Programmatic);

    /// <summary>Shows the oldest output the scrollback keeps.</summary>
    public void ScrollToTop() => Term.ScrollTo(0);

    /// <summary>Scrolls by whole screens: up for negative <paramref name="pages"/>, down for positive.</summary>
    public void ScrollPages(int pages)
    {
        if (_scrollBar is { } scrollBar)
            Term.ScrollTo((int)scrollBar.Value + pages * Term.Rows);
    }

    /// <summary>Shows the newest output.</summary>
    public void ScrollToBottom() => Term.ScrollTo(int.MaxValue);

    /// <summary>Font, colours and cursor; applied at once, also to text already shown.</summary>
    public TerminalAppearance Appearance
    {
        get => _appearance;
        set
        {
            if (value == _appearance)
                return;
            _appearance = value;
            if (IsLoaded)
                ApplyAppearance();
        }
    }

    /// <summary>Copies the selected text, if any.</summary>
    public void Copy()
    {
        if (Term.GetSelectedText() is { Length: > 0 } selection)
            CopyToClipboard(selection);
    }

    /// <summary>Sends the clipboard text as if it had been typed.</summary>
    public void Paste() => _ = PasteFromClipboardAsync();

    /// <summary>
    /// The size of a character cell and the space available to the cells, in physical pixels; null
    /// until the terminal has been laid out.
    /// </summary>
    public (int CellWidth, int CellHeight, int GridWidth, int GridHeight)? GetGrid()
    {
        if (XamlRoot is null || Term.Columns <= 0 || Term.Rows <= 0)
            return null;

        var scale = XamlRoot.RasterizationScale;
        if (scale != _cellScale)
        {
            _cell = null;
            _cellScale = scale;
        }

        // The scroll bar sits beside the cells.
        var scrollBarWidth = (Term.FindName("scrollbar") as ScrollBar)?.ActualWidth ?? 0;
        var gridWidth = (int)Math.Round((Term.ActualWidth - scrollBarWidth) * scale);
        var gridHeight = (int)Math.Round(Term.ActualHeight * scale);

        // The font never changes, so the cell is worked out once per scale, while the layout is
        // settled; during a resize the row and column counts can lag behind the size.
        if (_cell is null
            && CharacterGrid.InferCellSize(gridWidth, Term.Columns) is { } cellWidth
            && CharacterGrid.InferCellSize(gridHeight, Term.Rows) is { } cellHeight)
            _cell = (cellWidth, cellHeight);

        return _cell is var (width, height) ? (width, height, gridWidth, gridHeight) : null;
    }


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

    private void ApplyAppearance()
    {
        var scheme = _appearance.Scheme;
        var theme = new TerminalTheme
        {
            DefaultBackground = ColorRef(scheme.Background),
            DefaultForeground = ColorRef(scheme.Foreground),
            DefaultSelectionBackground = ColorRef(scheme.SelectionBackground),
            CursorStyle = _appearance.Cursor switch
            {
                CursorShape.Bar => CursorStyle.BlinkingBar,
                CursorShape.Underline => CursorStyle.BlinkingUnderline,
                _ => CursorStyle.BlinkingBlock,
            },
            ColorTable = [.. scheme.Palette.Select(ColorRef)],
        };
        var fontSize = (short)Math.Clamp(_appearance.FontSize, TerminalAppearance.MinimumFontSize, TerminalAppearance.MaximumFontSize);
        var background = System.Drawing.Color.FromArgb(unchecked((int)(0xFF000000 | scheme.Background)));
        Term.SetTheme(theme, _appearance.FontFamily, fontSize, background);
        Root.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, background.R, background.G, background.B));

        // Another font or size has other cells: work them out again for grid snapping.
        _cell = null;
    }

    // The control's scroll bar for the scrollback is a plain WinUI ScrollBar, which draws nothing
    // until it is given an indicator mode. The control keeps it on the part of the scrollback in
    // view, setting its maximum and then its value: it is read once both are set.
    private void SetUpScrollBar()
    {
        if (_scrollBar is not null || Term.FindName("scrollbar") is not ScrollBar scrollBar)
            return;

        _scrollBar = scrollBar;
        scrollBar.IndicatorMode = ScrollingIndicatorMode.MouseIndicator;
        scrollBar.ValueChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateIsScrolledUp);
        scrollBar.RegisterPropertyChangedCallback(RangeBase.MaximumProperty, (_, _) => DispatcherQueue.TryEnqueue(UpdateIsScrolledUp));
    }

    private void UpdateIsScrolledUp()
    {
        var isScrolledUp = _scrollBar is { } scrollBar && scrollBar.Value < scrollBar.Maximum;
        if (isScrolledUp == IsScrolledUp)
            return;

        IsScrolledUp = isScrolledUp;
        IsScrolledUpChanged?.Invoke(this, EventArgs.Empty);
    }

    // The control takes colours as Win32 COLORREF values, 0x00BBGGRR; COMrade keeps 0xRRGGBB.
    private static uint ColorRef(uint rgb) => ((rgb >> 16) & 0xFF) | (rgb & 0xFF00) | ((rgb & 0xFF) << 16);

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
