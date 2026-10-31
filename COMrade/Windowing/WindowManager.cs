using System;
using System.Collections.Generic;
using System.Linq;
using COMrade.Core.Ports;
using COMrade.ViewModels;
using COMrade.Views;
using Microsoft.Extensions.DependencyInjection;
using Windows.Graphics;

namespace COMrade.Windowing;

/// <summary>
/// The app's windows. Tabs can be dragged out into a new window or into another window; the port
/// watcher, the port list and the sessions are shared by all of them. The app exits when the last
/// window closes.
/// </summary>
public sealed class WindowManager(IServiceProvider services, PortWatcher watcher, PortListViewModel portList)
{
    private readonly List<MainWindow> _windows = [];

    /// <summary>The tab being dragged, while a drag is in progress; drags never leave the app.</summary>
    public DeviceTabViewModel? DraggedTab { get; set; }

    /// <summary>Starts watching ports and opens the first window.</summary>
    public void Start()
    {
        watcher.Start();
        OpenWindow().Activate();
    }

    /// <summary>Opens an empty window, with the same size as <paramref name="like"/> when given.</summary>
    public MainWindow OpenWindow(MainWindow? like = null, PointInt32? position = null)
    {
        var window = ActivatorUtilities.CreateInstance<MainWindow>(services, ActivatorUtilities.CreateInstance<MainViewModel>(services), this);
        if (like is not null)
            window.AppWindow.Resize(like.AppWindow.Size);
        if (position is { } at)
            window.AppWindow.Move(at);

        _windows.Add(window);
        window.Closed += (_, _) => OnClosed(window);
        return window;
    }

    /// <summary>Shows the device's tab if a window already has it; a device has one tab at most.</summary>
    public bool ShowExisting(DeviceIdentity identity)
    {
        foreach (var window in _windows)
        {
            if (window.ViewModel.FindTab(identity) is { } tab)
            {
                window.ViewModel.SelectedTab = tab;
                window.Activate();
                return true;
            }
        }

        return false;
    }

    /// <summary>The window that has the tab, if any.</summary>
    public MainWindow? WindowOf(DeviceTabViewModel tab) => _windows.FirstOrDefault(window => window.ViewModel.Tabs.Contains(tab));

    /// <summary>Moves a tab into another window at the given position; its port stays open.</summary>
    public void Move(DeviceTabViewModel tab, MainWindow to, int index)
    {
        var from = WindowOf(tab);
        if (from is null || from == to)
            return;

        from.ViewModel.Detach(tab);
        to.ViewModel.Attach(tab, index);
        to.Activate();
        CloseIfEmptied(from);
    }

    /// <summary>Moves a tab into a new window, placed at <paramref name="position"/> when given.</summary>
    public void MoveToNewWindow(DeviceTabViewModel tab, PointInt32? position = null)
    {
        var from = WindowOf(tab);
        if (from is null)
            return;

        // Dragging the only tab out of a window just moves the window, as in Windows Terminal.
        if (from.ViewModel.Tabs.Count == 1)
        {
            if (position is { } at)
                from.AppWindow.Move(at);
            return;
        }

        var window = OpenWindow(like: from, position: position);
        from.ViewModel.Detach(tab);
        window.ViewModel.Attach(tab, 0);
        window.Activate();
    }

    // A window whose last tab moved away has nothing left to show.
    private static void CloseIfEmptied(MainWindow window)
    {
        if (window.ViewModel.Tabs.Count == 0)
            window.Close();
    }

    private void OnClosed(MainWindow window)
    {
        _windows.Remove(window);
        if (_windows.Count > 0)
            return;

        DriverHelpWindow.CloseIfOpen();
        portList.Dispose();
        watcher.Dispose();
    }
}
