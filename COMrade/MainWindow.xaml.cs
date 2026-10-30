using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using COMrade.Core.Ports;
using COMrade.Core.Serial;
using COMrade.Core.Sessions;
using COMrade.Core.Terminal;
using COMrade.Terminal;
using COMrade.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace COMrade;

public sealed partial class MainWindow : Window
{
    // Like Windows Terminal (460 × 0 DIP): just enough for the toolbar plus a few terminal rows.
    private const double MinimumWidth = 500;
    private const double MinimumHeight = 300;

    private readonly PortList _ports;
    private readonly PortWatcher _watcher;
    private readonly PortSessions _sessions;
    private PortSession? _session;
    private DeviceTerminal? _terminal;
    private bool _closed;

    public MainWindow(MainViewModel viewModel, PortList ports, PortWatcher watcher, PortSessions sessions)
    {
        ViewModel = viewModel;
        _ports = ports;
        _watcher = watcher;
        _sessions = sessions;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Taskbar, Alt+Tab and title bar icon for the unpackaged app.
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "COMrade.ico"));
        AppWindow.Resize(new SizeInt32(1100, 720));
        Placeholder.Loaded += (_, _) =>
        {
            SetMinimumSize();
            // Moving to a monitor with another scale changes the size in pixels; recompute it,
            // as Windows Terminal does on every WM_GETMINMAXINFO.
            Content.XamlRoot.Changed += (_, _) => SetMinimumSize();
        };

        Closed += OnClosed;

        // Temporary until the M5 user interface: "COMrade --port COMx" opens that port in a terminal.
        var arguments = Environment.GetCommandLineArgs();
        var portIndex = Array.IndexOf(arguments, "--port");
        if (portIndex >= 0 && portIndex + 1 < arguments.Length)
            _ = OpenPortFromCommandLineAsync(arguments[portIndex + 1]);
    }

    public MainViewModel ViewModel { get; }

    private void SetMinimumSize()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
            return;

        var scale = Content.XamlRoot.RasterizationScale;
        presenter.PreferredMinimumWidth = (int)(MinimumWidth * scale);
        presenter.PreferredMinimumHeight = (int)(MinimumHeight * scale);
    }

    private async Task OpenPortFromCommandLineAsync(string portName)
    {
        var enumerated = new TaskCompletionSource();
        _ports.EnumerationCompleted += (_, _) => enumerated.TrySetResult();
        _watcher.Start();
        await enumerated.Task;

        var port = _ports.Ports.FirstOrDefault(p => string.Equals(p.PortName, portName, StringComparison.OrdinalIgnoreCase));
        if (port is null)
        {
            ViewModel.Title = $"COMrade — {portName} not found";
            return;
        }

        Placeholder.Visibility = Visibility.Collapsed;
        Terminal.Visibility = Visibility.Visible;

        _session = _sessions.GetOrCreate(port, SerialSettings.Default);
        _session.StateChanged += OnSessionStateChanged;
        // New line on CR until the M5 quick settings expose it: loopback tests echo bare CRs.
        _terminal = new DeviceTerminal(_session, Terminal, TerminalSettings.Default with { NewLineOnCr = true });

        await _session.OpenAsync();
        Terminal.FocusTerminal();
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        var session = (PortSession)sender!;
        var title = $"COMrade — {session.Port.PortName} · {session.State}";
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closed)
                ViewModel.Title = title;
        });
    }

    // Unhook everything that could call back into the closed window before closing the port.
    private async void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _terminal?.Dispose();

        if (_session is not null)
        {
            _session.StateChanged -= OnSessionStateChanged;
            await _sessions.CloseAsync(_session);
        }

        _watcher.Dispose();
    }
}
