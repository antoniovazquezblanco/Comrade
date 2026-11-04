using System;
using System.Net.Http;
using System.Reflection;
using COMrade.Core.Mcp;
using COMrade.Core.Ports;
using COMrade.Core.Serial;
using COMrade.Core.Sessions;
using COMrade.Core.Settings;
using COMrade.Core.Updates;
using COMrade.ViewModels;
using COMrade.Windowing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace COMrade;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    public static new App Current => (App)Application.Current;

    public IServiceProvider Services { get; }

    /// <summary>This build's version, for example 0.3.0, or 0.3.1-alpha.0.3 after it.</summary>
    public static ReleaseVersion Version { get; } =
        ReleaseVersion.TryParse(typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? new ReleaseVersion(0, 0, 0);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var windows = Services.GetRequiredService<WindowManager>();
        windows.Start();
        // Created here, on the UI thread, where it then does its work.
        Services.GetRequiredService<UpdateService>().Start();

        // AI assistants are let in once there is a window to show their tabs, and sent away with the last one.
        var mcp = Services.GetRequiredService<McpPipeServer>();
        mcp.Start();
        windows.LastWindowClosed += async (_, _) => await mcp.DisposeAsync();
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
        services.AddSingleton(new UpdateChecker(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, Version));
        services.AddSingleton<UpdateService>();
        services.AddSingleton<AssistantHost>();
        services.AddSingleton(provider => new McpPipeServer(
            provider.GetRequiredService<AssistantHost>(),
            provider.GetRequiredService<PortList>(),
            provider.GetRequiredService<DeviceSettingsStore>(),
            provider.GetRequiredService<AppSettingsStore>(),
            Version.ToString()));

        services.AddSingleton<PortListViewModel>();

        // One view model per window, created by the window manager.
        services.AddTransient<MainViewModel>();
        services.AddSingleton<WindowManager>();

        return services.BuildServiceProvider();
    }
}
