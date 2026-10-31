using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using COMrade.Core.Ports;
using COMrade.Core.Terminal;
using COMrade.ViewModels;
using COMrade.Views;
using COMrade.Windowing;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using WinUIEx.Messaging;

namespace COMrade;

public sealed partial class MainWindow : Window
{
    // Like Windows Terminal (460 × 0 DIP): just enough for the toolbar plus a few terminal rows.
    private const double MinimumWidth = 500;
    private const double MinimumHeight = 300;

    private const uint WM_SIZING = 0x0214;
    private const int WMSZ_LEFT = 1;
    private const int WMSZ_RIGHT = 2;
    private const int WMSZ_TOP = 3;
    private const int WMSZ_TOPLEFT = 4;
    private const int WMSZ_TOPRIGHT = 5;
    private const int WMSZ_BOTTOM = 6;
    private const int WMSZ_BOTTOMLEFT = 7;

    // Marks COMrade's own tab drags; the tab itself is in WindowManager.DraggedTab.
    private const string TabDragFormat = "COMradeTab";

    private readonly WindowManager _windows;
    private readonly ThreadKeyboardHook _keyboard;
    private readonly WindowMessageMonitor _messages;
    private const VirtualKey Comma = (VirtualKey)0xBC;

    private readonly Dictionary<TabViewModel, FrameworkElement> _views = [];
    private bool _closed;

    public MainWindow(MainViewModel viewModel, WindowManager windows)
    {
        ViewModel = viewModel;
        _windows = windows;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);

        // Taskbar, Alt+Tab and title bar icon for the unpackaged app.
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "COMrade.ico"));
        AppWindow.Resize(new SizeInt32(1100, 720));
        TabContents.Loaded += (_, _) =>
        {
            UpdateForScale();
            // Moving to a monitor with another scale changes the sizes in pixels; recompute them,
            // as Windows Terminal does on every WM_GETMINMAXINFO.
            Content.XamlRoot.Changed += (_, _) => UpdateForScale();
        };

        _keyboard = new ThreadKeyboardHook(OnShortcut);
        _messages = new WindowMessageMonitor(this);
        _messages.WindowMessageReceived += OnWindowMessage;

        ViewModel.Tabs.CollectionChanged += OnTabsChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
    }

    public MainViewModel ViewModel { get; }

    private void UpdateForScale()
    {
        if (_closed)
            return;

        var scale = Content.XamlRoot.RasterizationScale;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(MinimumWidth * scale);
            presenter.PreferredMinimumHeight = (int)(MinimumHeight * scale);
        }

        // Keep room to drag the window between the last tab and the caption buttons.
        DragRegion.MinWidth = AppWindow.TitleBar.RightInset / scale + 48;
    }

    // Windows Terminal's shortcuts. Ctrl+Shift, because Ctrl+letter is a control character that
    // devices use (Ctrl+T, Ctrl+W, Ctrl+C...), so it must reach the terminal.
    private bool OnShortcut(VirtualKey key, bool shift)
    {
        // Every window of the app hooks the same thread: only the one in front takes the keys.
        if (_closed || GetForegroundWindow() != WinRT.Interop.WindowNative.GetWindowHandle(this))
            return false;

        var selected = SelectedView as DeviceTabView;
        switch (key, shift)
        {
            case (Comma, false):
                ViewModel.OpenSettings();
                return true;
            case (VirtualKey.T, true):
                ShowPortMenu();
                return true;
            case (VirtualKey.W, true):
                if (ViewModel.SelectedTab is { } tab)
                    _ = ViewModel.CloseTabAsync(tab);
                return true;
            case (VirtualKey.Tab, _):
                SelectNextTab(shift ? -1 : 1);
                return true;
            // Text boxes keep their own copy and paste.
            case (VirtualKey.C, true) when selected is not null && !IsEditingText:
                selected.Copy();
                return true;
            case (VirtualKey.V, true) when selected is not null && !IsEditingText:
                selected.Paste();
                return true;
            default:
                return false;
        }
    }

    private FrameworkElement? SelectedView =>
        ViewModel.SelectedTab is { } tab && _views.TryGetValue(tab, out var view) ? view : null;

    private bool IsEditingText => Content.XamlRoot is { } root && FocusManager.GetFocusedElement(root) is TextBox;

    private void SelectNextTab(int step)
    {
        if (ViewModel.Tabs.Count == 0)
            return;
        var index = ViewModel.SelectedTab is { } tab ? ViewModel.Tabs.IndexOf(tab) : -1;
        ViewModel.SelectedTab = ViewModel.Tabs[(index + step + ViewModel.Tabs.Count) % ViewModel.Tabs.Count];
    }

    // While a terminal is shown, the window is resized in whole character cells, as in Windows Terminal.
    private void OnWindowMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message.MessageId != WM_SIZING || (SelectedView as DeviceTabView)?.GetGrid() is not var (cellWidth, cellHeight, gridWidth, gridHeight))
            return;

        // lParam points to the proposed window RECT: left, top, right, bottom.
        var rect = e.Message.LParam;
        int Read(int field) => Marshal.ReadInt32(rect, field * 4);
        void Write(int field, int value) => Marshal.WriteInt32(rect, field * 4, value);
        const int left = 0, top = 1, right = 2, bottom = 3;

        var edge = (int)e.Message.WParam;
        var size = AppWindow.Size;
        var presenter = AppWindow.Presenter as OverlappedPresenter;

        if (edge is not (WMSZ_TOP or WMSZ_BOTTOM))
        {
            var width = CharacterGrid.Snap(Read(right) - Read(left), size.Width - gridWidth, cellWidth, presenter?.PreferredMinimumWidth ?? 0);
            if (edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT)
                Write(left, Read(right) - width);
            else
                Write(right, Read(left) + width);
        }

        if (edge is not (WMSZ_LEFT or WMSZ_RIGHT))
        {
            var height = CharacterGrid.Snap(Read(bottom) - Read(top), size.Height - gridHeight, cellHeight, presenter?.PreferredMinimumHeight ?? 0);
            if (edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT)
                Write(top, Read(bottom) - height);
            else
                Write(bottom, Read(top) + height);
        }

        e.Result = 1;
        e.Handled = true;
    }

    private void OnPortSelected(object? sender, PortItemViewModel port) => Select(port);

    // A port its driver refuses cannot be opened: show how to fix the driver instead. A device has
    // one tab at most, and another window may already show it.
    private void Select(PortItemViewModel port)
    {
        if (port.NeedsDriverHelp)
            DriverHelpWindow.Show(port);
        else if (!_windows.ShowExisting(port.Port.Identity))
            ViewModel.OpenTab(port.Port);
    }

    private void OnAddTabButtonClick(TabView sender, object args) => ShowPortMenu();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ViewModel.OpenSettings();

    private void ShowPortMenu()
    {
        var menu = (MenuFlyout)Tabs.Resources["PortMenu"];
        menu.ShowAt(FindChild(Tabs, "AddButton") ?? Tabs);
    }

    private void OnPortMenuOpening(object? sender, object e)
    {
        var menu = (MenuFlyout)sender!;
        menu.Items.Clear();

        foreach (var port in ViewModel.PortList.Ports)
        {
            var item = new MenuFlyoutItem { Text = port.Title };
            if (port.NeedsDriverHelp)
                item.Icon = new FontIcon { Glyph = "\uE7BA" };
            ToolTipService.SetToolTip(item, port.Detail);
            item.Click += (_, _) => Select(port);
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuFlyoutItem { Text = "No serial ports found", IsEnabled = false });
    }

    private async void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args) =>
        await ViewModel.CloseTabAsync((TabViewModel)args.Item);

    private async void OnCloseTabClick(object sender, RoutedEventArgs e) =>
        await ViewModel.CloseTabAsync((TabViewModel)((FrameworkElement)sender).Tag);

    // Renaming: double-click a tab title, or Rename tab in its context menu.
    private void OnTabDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is DeviceTabViewModel tab)
            tab.IsRenaming = true;
    }

    // The rename box takes the focus, with the title selected, when renaming starts. The title under
    // it stays (invisible) and keeps the tab's size; the box is sized from it and reaches over the
    // tab's close button, where its own clear button goes. A longer name scrolls inside the box.
    private void OnRenameBoxLoaded(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        var title = (TextBlock)((Panel)box.Parent).Children[0];
        box.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) =>
        {
            var renaming = box.Visibility is Visibility.Visible;
            title.Opacity = renaming ? 0 : 1;
            if (!renaming)
                return;

            box.Width = title.ActualWidth - box.Margin.Left - box.Margin.Right;
            box.Text = ((DeviceTabViewModel)box.Tag).Header;
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        });
    }

    private void OnRenameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var box = (TextBox)sender;
        var tab = (DeviceTabViewModel)box.Tag;
        if (e.Key is VirtualKey.Enter)
            tab.Rename(box.Text);
        else if (e.Key is VirtualKey.Escape)
            tab.IsRenaming = false;
        else
            return;

        e.Handled = true;
        FocusSelectedTerminal();
    }

    private void OnRenameLostFocus(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        var tab = (DeviceTabViewModel)box.Tag;
        if (tab.IsRenaming)
            tab.Rename(box.Text);
    }

    private void FocusSelectedTerminal()
    {
        if (ViewModel.SelectedTab is { } tab && _views.TryGetValue(tab, out var view) && view is DeviceTabView device)
            device.FocusTerminal();
    }

    private void OnMoveToNewWindowClick(object sender, RoutedEventArgs e) =>
        _windows.MoveToNewWindow((DeviceTabViewModel)((FrameworkElement)sender).Tag);

    // Dragging tabs between windows, as in Windows Terminal: drop on another window's tab strip to
    // move the tab there, or anywhere else to give it a window of its own. Reordering within the
    // window is TabView's own.
    private void OnTabDragStarting(TabView sender, TabViewTabDragStartingEventArgs args)
    {
        // The settings tab stays in its window.
        if (args.Item is not DeviceTabViewModel tab)
        {
            args.Cancel = true;
            return;
        }

        _windows.DraggedTab = tab;
        args.Data.Properties.Add(TabDragFormat, true);
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void OnTabStripDragOver(object sender, DragEventArgs e)
    {
        if (IsTabFromAnotherWindow(e))
            e.AcceptedOperation = DataPackageOperation.Move;
    }

    private void OnTabStripDrop(object sender, DragEventArgs e)
    {
        if (!IsTabFromAnotherWindow(e) || _windows.DraggedTab is not { } tab)
            return;

        _windows.DraggedTab = null;
        _windows.Move(tab, this, DropIndex(e.GetPosition(Tabs)));
    }

    private bool IsTabFromAnotherWindow(DragEventArgs e) =>
        e.DataView.Properties.ContainsKey(TabDragFormat) && _windows.DraggedTab is { } tab && !ViewModel.Tabs.Contains(tab);

    private void OnTabDroppedOutside(TabView sender, TabViewTabDroppedOutsideEventArgs args)
    {
        // Dropped on another window's tab strip: already moved there.
        if (args.Item is not DeviceTabViewModel tab || !ViewModel.Tabs.Contains(tab))
            return;

        GetCursorPos(out var cursor);
        _windows.MoveToNewWindow(tab, new PointInt32(cursor.X - 120, cursor.Y - 16));
    }

    private void OnTabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args) => _windows.DraggedTab = null;

    // Where a dropped tab goes: before the first tab whose middle is right of the drop point.
    private int DropIndex(Point position)
    {
        for (var index = 0; index < ViewModel.Tabs.Count; index++)
        {
            if (Tabs.ContainerFromIndex(index) is not FrameworkElement item)
                continue;
            var left = item.TransformToVisual(Tabs).TransformPoint(new Point(0, 0)).X;
            if (position.X < left + item.ActualWidth / 2)
                return index;
        }

        return ViewModel.Tabs.Count;
    }

    // Each tab's contents are created once and only hidden while another tab is selected.
    // Dragging a tab removes and inserts it again, so views are matched to tabs afterwards.
    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_closed)
            return;

        foreach (var (tab, view) in _views)
        {
            if (!ViewModel.Tabs.Contains(tab))
            {
                _views.Remove(tab);
                (view as IDisposable)?.Dispose();
                TabContents.Children.Remove(view);
            }
        }

        foreach (var tab in ViewModel.Tabs)
        {
            if (_views.ContainsKey(tab))
                continue;
            FrameworkElement view = tab switch
            {
                DeviceTabViewModel device => new DeviceTabView(device),
                SettingsTabViewModel settings => new SettingsView(settings),
                _ => throw new InvalidOperationException($"No view for {tab.GetType().Name}."),
            };
            view.Visibility = Visibility.Collapsed;
            _views.Add(tab, view);
            TabContents.Children.Add(view);
        }

        ShowSelectedTab();
    });

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedTab))
            ShowSelectedTab();
        if (e.PropertyName is nameof(MainViewModel.Title) or nameof(MainViewModel.SelectedTab))
            Title = ViewModel.Title;
    }

    private void ShowSelectedTab()
    {
        foreach (var (tab, view) in _views)
            view.Visibility = tab == ViewModel.SelectedTab ? Visibility.Visible : Visibility.Collapsed;

        if (ViewModel.SelectedTab is { } selected && _views.TryGetValue(selected, out var shown) && shown is DeviceTabView selectedView)
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_closed)
                    selectedView.FocusTerminal();
            });
    }

    private static FrameworkElement? FindChild(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element && element.Name == name)
                return element;
            if (FindChild(child, name) is { } found)
                return found;
        }

        return null;
    }

    // Unhook everything that could call back into the closed window before closing the ports.
    private async void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _keyboard.Dispose();
        _messages.Dispose();
        ViewModel.Tabs.CollectionChanged -= OnTabsChanged;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        foreach (var view in _views.Values)
            (view as IDisposable)?.Dispose();

        await ViewModel.CloseAllAsync();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out PointInt32 point);
}
