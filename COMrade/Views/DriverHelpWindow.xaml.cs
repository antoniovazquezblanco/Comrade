using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using COMrade.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace COMrade.Views;

/// <summary>
/// How to use a Prolific PL2303 adapter that its driver refuses. A window of its own rather than a
/// dialog: a dialog would be hidden behind the native terminal of an open tab.
/// </summary>
public sealed partial class DriverHelpWindow : Window
{
    private const int WindowWidth = 520;

    private static DriverHelpWindow? _open;

    private DriverHelpWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "COMrade.ico"));

        // A fixed-size guide sized to its text, like a dialog: nothing to scroll or resize.
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        Closed += (_, _) => _open = null;
    }

    /// <summary>Shows the help for this port, reusing the window if it is already open.</summary>
    public static void Show(PortItemViewModel port)
    {
        _open ??= new DriverHelpWindow();
        _open.ShowFor(port);
    }

    /// <summary>Closes the help with the main window, so the app does not keep running.</summary>
    public static void CloseIfOpen() => _open?.Close();

    private void ShowFor(PortItemViewModel port)
    {
        Heading.Text = $"{port.Port.PortName} is refused by its driver";
        Cause.Text = $"This {DisplayText.RefusedAdapterName(port.Port)} is an old or counterfeit chip, and the driver Windows Update installs refuses to work with it.";

        Activate();
        // Measured once the first layout has applied the text styles.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, FitToContent);
    }

    // The window is exactly as high as the text needs at this width and text size. The content also
    // covers the title bar, so the frame is measured as the difference between window and content.
    private void FitToContent()
    {
        if (Root.XamlRoot is null)
            return;

        var scale = Root.XamlRoot.RasterizationScale;
        var frameWidth = AppWindow.Size.Width - Root.ActualWidth * scale;
        var frameHeight = AppWindow.Size.Height - Root.ActualHeight * scale;
        Root.Measure(new Windows.Foundation.Size(WindowWidth, double.PositiveInfinity));
        AppWindow.Resize(new SizeInt32(
            (int)Math.Ceiling(WindowWidth * scale + frameWidth),
            (int)Math.Ceiling(Root.DesiredSize.Height * scale + frameHeight)));
    }

    private void OnOpenDeviceManagerClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // Opening it was cancelled or is not allowed; the steps still say how to get there.
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
