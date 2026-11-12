using System;
using System.Collections.Generic;
using System.Text;
using COMrade.Core.Sessions;
using COMrade.Core.Terminal;
using COMrade.Core.TrafficView;
using COMrade.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace COMrade.Controls;

/// <summary>
/// The analysis view: what went over a port both ways, a row per line and sender with the time it
/// arrived and its bytes in hex and as text, see <see cref="TrafficRows"/>. Only the rows on screen exist, filled from the traffic
/// as they show. It follows the newest rows until scrolled up, and keeps its rows in view as old
/// ones go or the rows are laid out again.
/// </summary>
public sealed partial class TrafficPane : UserControl
{
    private const double SidePadding = 8;

    // Wheel delta for a row: three rows a notch.
    private const int WheelPerRow = 40;

    private readonly List<Border> _pool = [];
    private TrafficViewModel? _model;
    private TerminalAppearance _appearance = TerminalAppearance.Default;
    private FontFamily _font = new(TerminalAppearance.DefaultFontFamily);
    private double _fontSize;
    private double _cellWidth = 8;
    private double _rowHeight = 16;
    private Brush _textBrush = new SolidColorBrush(Colors.LightGray);
    private Brush _sentBrush = new SolidColorBrush(Colors.Cyan);
    private Brush _dimBrush = new SolidColorBrush(Colors.Gray);
    private Brush _selectionBrush = new SolidColorBrush(Colors.SteelBlue);

    // Rows that fit, the first one shown and its first byte, to find it again when the rows change.
    private int _visible = 1;
    private int _top;
    private long? _topOffset;
    private bool _follows = true;
    private bool _settingScroll;
    private int _wheel;

    // The columns shown and how they share a row, see TrafficLayout.
    private TrafficLayout _layout = TrafficLayout.Fit(AnalysisColumns.All, 80);

    // The selection: the rows of these bytes and those between them.
    private long? _anchor;
    private long? _active;
    private bool _selecting;

    public TrafficPane()
    {
        InitializeComponent();
        ApplyAppearance();
    }

    /// <summary>The rows to show.</summary>
    public TrafficViewModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.Updated -= OnUpdated;
                _model.ColumnsChanged -= OnColumnsChanged;
            }

            _model = value;
            if (value is not null)
            {
                value.Updated += OnUpdated;
                value.ColumnsChanged += OnColumnsChanged;
            }

            _follows = true;
            _anchor = _active = null;
            Fit();
        }
    }

    /// <summary>The terminal's font, size and colours, so the analysis looks like the terminal.</summary>
    public TerminalAppearance Appearance
    {
        get => _appearance;
        set
        {
            _appearance = value;
            ApplyAppearance();
        }
    }

    public void ScrollToTop() => ScrollTo(0);

    public void ScrollToBottom() => ScrollTo(int.MaxValue);

    public void ScrollPages(int pages) => ScrollTo(_top + pages * Math.Max(1, _visible - 1));

    /// <summary>Copies the selected rows as shown; false when none is selected.</summary>
    public bool Copy() => CopySelection(CopyKind.Rows);

    private TrafficRows? RowsModel => _model?.Rows;

    private void OnUpdated(object? sender, EventArgs e) => Refresh();

    private void OnColumnsChanged(object? sender, EventArgs e) => Fit();

    private void ApplyAppearance()
    {
        var scheme = _appearance.Scheme;
        Rows.Background = Brush(scheme.Background);
        _textBrush = Brush(scheme.Foreground);
        _sentBrush = Brush(scheme.Palette[6]);
        _dimBrush = Brush(Mix(scheme.Foreground, scheme.Background, 0.55));
        _selectionBrush = Brush(scheme.SelectionBackground);

        // The terminal's size is in points.
        _font = new FontFamily(_appearance.FontFamily);
        _fontSize = Math.Clamp(_appearance.FontSize, TerminalAppearance.MinimumFontSize, TerminalAppearance.MaximumFontSize) * 96.0 / 72;
        Probe.FontFamily = Header.FontFamily = _font;
        Probe.FontSize = Header.FontSize = _fontSize;
        ShowEachCharacter(Header);
        Probe.Text = new string('0', 64);
        Probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _cellWidth = Math.Max(1, Probe.DesiredSize.Width / 64);
        _rowHeight = Math.Max(1, Math.Ceiling(Probe.DesiredSize.Height));

        for (var index = 0; index < _pool.Count; index++)
            StyleRow(_pool[index], index);
        Fit();
    }

    // The rows that fit the pane, the columns shown and the room each has.
    private void Fit()
    {
        var (width, height) = (Surface.ActualWidth, Surface.ActualHeight);
        if (width <= 0 || height <= 0)
            return;

        Surface.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
        _visible = Math.Max(1, (int)(height / _rowHeight));
        while (_pool.Count < _visible + 1)
        {
            var row = new Border();
            StyleRow(row, _pool.Count);
            Surface.Children.Add(row);
            _pool.Add(row);
        }

        foreach (var row in _pool)
            row.Width = width;

        // Laid out again only when the bytes or the text's columns change.
        _layout = TrafficLayout.Fit(_model?.Columns ?? AnalysisColumns.All, Math.Max(0, (int)((width - 2 * SidePadding) / _cellWidth)));
        Header.Text = _layout.Header;
        RowsModel?.Resize(_layout.TextColumns, _layout.RowBytes);
        Refresh();
    }

    private void StyleRow(Border row, int index)
    {
        row.Height = _rowHeight;
        row.Padding = new Thickness(SidePadding, 0, SidePadding, 0);
        row.Child = new TextBlock
        {
            FontFamily = _font,
            FontSize = _fontSize,
            LineHeight = _rowHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.NoWrap,
            IsHitTestVisible = false,
        };
        ShowEachCharacter((TextBlock)row.Child);
        Canvas.SetTop(row, index * _rowHeight);
    }

    // Fonts such as Cascadia Code join "=>" or "!=" into one sign: the traffic shows the characters.
    private static void ShowEachCharacter(TextBlock block)
    {
        Typography.SetStandardLigatures(block, false);
        Typography.SetContextualAlternates(block, false);
    }

    // After the rows changed or the pane scrolled: the rows shown, the scroll bar and the way down.
    private void Refresh()
    {
        var count = RowsModel?.Count ?? 0;
        var last = Math.Max(0, count - _visible);
        if (_follows)
            _top = last;
        else if (_topOffset is { } offset)
            _top = RowsModel!.IndexOf(offset);
        _top = Math.Clamp(_top, 0, last);
        _topOffset = _top < count ? RowsModel![_top].Offset : null;

        _settingScroll = true;
        Scroll.Maximum = last;
        Scroll.ViewportSize = _visible;
        Scroll.LargeChange = Math.Max(1, _visible - 1);
        Scroll.Value = _top;
        Scroll.Visibility = last > 0 ? Visibility.Visible : Visibility.Collapsed;
        _settingScroll = false;
        BottomButton.Visibility = _top < last ? Visibility.Visible : Visibility.Collapsed;
        Render();
    }

    private void Render()
    {
        var rows = RowsModel;
        var selected = Selected();
        for (var i = 0; i < _pool.Count; i++)
        {
            var border = _pool[i];
            var index = _top + i;
            if (rows is null || index >= rows.Count)
            {
                border.Visibility = Visibility.Collapsed;
                continue;
            }

            border.Visibility = Visibility.Visible;
            border.Background = selected is { } range && index >= range.First && index <= range.Last ? _selectionBrush : null;
            Fill((TextBlock)border.Child, rows[index]);
        }
    }

    private void Fill(TextBlock block, TrafficRow row)
    {
        var colour = row.Sender is TrafficSender.Device ? _textBrush : _sentBrush;
        var bytes = Bytes(row);
        block.Inlines.Clear();
        block.Inlines.Add(new Run { Text = _layout.Time(Time(row)), Foreground = _dimBrush });
        block.Inlines.Add(new Run { Text = _layout.Who(row) + _layout.Hex(bytes), Foreground = colour });
        if (!_layout.Shown.HasFlag(AnalysisColumns.Text))
            return;
        foreach (var (text, isEscape) in TrafficText.Pieces(bytes))
            block.Inlines.Add(new Run { Text = text, Foreground = isEscape ? _dimBrush : colour });
    }

    // When the row's first byte arrived; none for the rest of a wrapped row.
    private DateTimeOffset? Time(TrafficRow row) => row.Continues ? null : _model?.Traffic.ArrivedAt(row.Offset);

    // Empty when the row's traffic went since it was laid out: the next update drops the row.
    private byte[] Bytes(TrafficRow row)
    {
        if (_model is null)
            return [];
        var (start, runs) = _model.Traffic.Read(row.Offset, row.Length, maxRuns: 1);
        return start == row.Offset && runs.Count == 1 ? runs[0].Bytes : [];
    }

    private void ScrollTo(int top)
    {
        var count = RowsModel?.Count ?? 0;
        var last = Math.Max(0, count - _visible);
        _top = Math.Clamp(top, 0, last);
        _follows = _top >= last;
        _topOffset = _top < count ? RowsModel![_top].Offset : null;
        Refresh();
    }

    private void OnScrollChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_settingScroll)
            ScrollTo((int)Math.Round(e.NewValue));
    }

    private void OnBottomClick(object sender, RoutedEventArgs e)
    {
        Focus(FocusState.Programmatic);
        ScrollToBottom();
    }

    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e) => Fit();

    private void OnSurfaceWheel(object sender, PointerRoutedEventArgs e)
    {
        _wheel += e.GetCurrentPoint(Surface).Properties.MouseWheelDelta;
        var rows = _wheel / WheelPerRow;
        _wheel %= WheelPerRow;
        if (rows != 0)
            ScrollTo(_top - rows);
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Up:
                ScrollTo(_top - 1);
                break;
            case VirtualKey.Down:
                ScrollTo(_top + 1);
                break;
            case VirtualKey.PageUp:
                ScrollPages(-1);
                break;
            case VirtualKey.PageDown:
                ScrollPages(1);
                break;
            case VirtualKey.Home:
                ScrollToTop();
                break;
            case VirtualKey.End:
                ScrollToBottom();
                break;
            case VirtualKey.Escape when _anchor is not null:
                _anchor = _active = null;
                Render();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // Click selects a row, Shift+click and dragging select those between; right-click keeps the
    // selection it is in.
    private void OnSurfacePressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        var point = e.GetCurrentPoint(Surface);
        if (RowAt(point.Position.Y) is not { } offset)
        {
            if (!point.Properties.IsRightButtonPressed)
                _anchor = _active = null;
            Render();
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            if (!IsSelected(offset))
                _anchor = _active = offset;
        }
        else
        {
            var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
            if (!shift || _anchor is null)
                _anchor = offset;
            _active = offset;
            _selecting = Surface.CapturePointer(e.Pointer);
        }

        Render();
    }

    private void OnSurfaceMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_selecting || RowAt(e.GetCurrentPoint(Surface).Position.Y) is not { } offset || offset == _active)
            return;
        _active = offset;
        Render();
    }

    private void OnSurfaceReleased(object sender, PointerRoutedEventArgs e)
    {
        _selecting = false;
        Surface.ReleasePointerCapture(e.Pointer);
    }

    private void OnSurfaceCaptureLost(object sender, PointerRoutedEventArgs e) => _selecting = false;

    // The first byte of the row at y in the pane, the nearest row for a y outside it.
    private long? RowAt(double y)
    {
        if (RowsModel is not { Count: > 0 } rows)
            return null;
        var index = _top + (int)Math.Floor(y / _rowHeight);
        if (index >= rows.Count && !_selecting)
            return null;
        return rows[Math.Clamp(index, 0, rows.Count - 1)].Offset;
    }

    // The rows selected, first and last; null for none.
    private (int First, int Last)? Selected()
    {
        if (RowsModel is not { Count: > 0 } rows || _anchor is not { } anchor || _active is not { } active)
            return null;
        var (from, to) = (rows.IndexOf(anchor), rows.IndexOf(active));
        return (Math.Clamp(Math.Min(from, to), 0, rows.Count - 1), Math.Clamp(Math.Max(from, to), 0, rows.Count - 1));
    }

    private bool IsSelected(long offset) =>
        Selected() is { } range && RowsModel!.IndexOf(offset) is var index && index >= range.First && index <= range.Last;

    private void OnMenuOpening(object sender, object e)
    {
        CopyRowsItem.IsEnabled = CopyTextItem.IsEnabled = CopyHexItem.IsEnabled = Selected() is not null;
        ShowColumnChoices(ColumnsItem.Items);
    }

    private void OnColumnsMenuOpening(object sender, object e) => ShowColumnChoices(((MenuFlyout)sender).Items);

    // Each column checked when it shows; the last of Hex and Text cannot be unchecked.
    private void ShowColumnChoices(IEnumerable<MenuFlyoutItemBase> items)
    {
        var shown = _model?.Columns ?? AnalysisColumns.All;
        foreach (var item in items)
        {
            if (item is not ToggleMenuFlyoutItem { Tag: string tag } toggle)
                continue;
            var column = Enum.Parse<AnalysisColumns>(tag);
            toggle.IsChecked = shown.HasFlag(column);
            toggle.IsEnabled = TrafficLayout.Show(shown, column, !toggle.IsChecked) != shown;
        }
    }

    private void OnColumnClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem { Tag: string tag } toggle)
            _model?.ShowColumn(Enum.Parse<AnalysisColumns>(tag), toggle.IsChecked);
    }

    private void OnCopyRowsClick(object sender, RoutedEventArgs e) => CopySelection(CopyKind.Rows);

    private void OnCopyTextClick(object sender, RoutedEventArgs e) => CopySelection(CopyKind.Text);

    private void OnCopyHexClick(object sender, RoutedEventArgs e) => CopySelection(CopyKind.Hex);

    // The rows as shown, or only their text or hex, a line each.
    private bool CopySelection(CopyKind kind)
    {
        if (Selected() is not { } range)
            return false;

        var copied = new StringBuilder();
        for (var index = range.First; index <= range.Last; index++)
        {
            var row = RowsModel![index];
            var bytes = Bytes(row);
            copied.Append(kind switch
            {
                CopyKind.Text => TrafficText.Format(bytes),
                CopyKind.Hex => TrafficText.Hex(bytes),
                _ => _layout.Format(Time(row), row, bytes),
            }).Append("\r\n");
        }

        ClipboardText.Set(copied.ToString());
        return true;
    }

    private enum CopyKind
    {
        Rows,
        Text,
        Hex,
    }

    private static SolidColorBrush Brush(uint rgb) =>
        new(Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    // A colour part of the way from one to the other: the dim text of the time and the escapes.
    private static uint Mix(uint from, uint to, double part)
    {
        uint Channel(int shift) => (uint)Math.Round(((from >> shift) & 0xFF) * (1 - part) + ((to >> shift) & 0xFF) * part) << shift;
        return Channel(16) | Channel(8) | Channel(0);
    }
}
