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
        services.AddSingleton(new AppSettingsStore(AppSettingsStore.DefaultPath));

        services.AddSingleton<PortListViewModel>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
