using System;
using System.IO;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace COMrade;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Taskbar, Alt+Tab and title bar icon for the unpackaged app.
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "COMrade.ico"));
        AppWindow.Resize(new SizeInt32(1100, 720));
    }
}
