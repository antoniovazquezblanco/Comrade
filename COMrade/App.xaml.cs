using System;
using COMrade.Core.Ports;
using COMrade.Core.Serial;
using COMrade.Core.Sessions;
using COMrade.Core.Settings;
using COMrade.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace COMrade;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();

        // TEMPORARY: crash diagnostics.
        if (Environment.GetEnvironmentVariable("COMRADE_DIAG") is { } diag)
        {
            UnhandledException += (_, e) => System.IO.File.AppendAllText(diag, $"UI {e.Exception}{Environment.NewLine}");
            AppDomain.CurrentDomain.UnhandledException += (_, e) => System.IO.File.AppendAllText(diag, $"DOMAIN {e.ExceptionObject}{Environment.NewLine}");
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => System.IO.File.AppendAllText(diag, $"TASK {e.Exception}{Environment.NewLine}");
        }
    }

    public static new App Current => (App)Application.Current;

    public IServiceProvider Services { get; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<PortList>();
        services.AddSingleton<PortWatcher>();
        services.AddSingleton<OpenSerialConnection>(SerialConnection.OpenAsync);
        services.AddSingleton<PortSessions>();
        services.AddSingleton(new DeviceSettingsStore(DeviceSettingsStore.DefaultPath));

        services.AddSingleton<PortListViewModel>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
