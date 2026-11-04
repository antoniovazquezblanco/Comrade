using COMrade.Core.Mcp;
using COMrade.Core.Ports;
using COMrade.Core.Serial;
using COMrade.Core.Sessions;
using COMrade.Core.Settings;

namespace COMrade.Core.Tests.Fakes;

/// <summary>COMrade's windows for the tools: tabs over real sessions on the fake driver.</summary>
internal sealed class FakeAssistantHost(PortList ports, FakeSerialDriver driver, DeviceSettingsStore devices, AppSettingsStore settings) : IAsyncDisposable
{
    private readonly Lock _lock = new();
    private readonly List<FakeAssistantTab> _tabs = [];

    public IReadOnlyList<FakeAssistantTab> Tabs
    {
        get
        {
            lock (_lock)
                return [.. _tabs];
        }
    }

    public IAssistantHost Host => new Adapter(this);

    /// <summary>Opens a tab as the user would, with the device's remembered settings.</summary>
    public FakeAssistantTab AddTab(SerialPortInfo port, DeviceSettings? deviceSettings = null)
    {
        deviceSettings ??= devices.Find(port.Identity)?.Settings ?? settings.Current.NewDevice;
        var tab = new FakeAssistantTab(new PortSession(port, deviceSettings.Serial, ports, driver.OpenAsync), deviceSettings);
        lock (_lock)
            _tabs.Add(tab);
        return tab;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var tab in Tabs)
            await tab.Session.DisposeAsync();
    }

    private sealed class Adapter(FakeAssistantHost host) : IAssistantHost
    {
        public Task<IReadOnlyList<IAssistantTab>> GetTabsAsync() => Task.FromResult<IReadOnlyList<IAssistantTab>>(host.Tabs);

        public Task<IAssistantTab> OpenTabAsync(SerialPortInfo port) => Task.FromResult<IAssistantTab>(
            host.Tabs.FirstOrDefault(tab => tab.Session.Port.Identity.IsSameDeviceAs(port.Identity)) ?? host.AddTab(port));

        public Task ShowTabAsync(IAssistantTab tab)
        {
            ((FakeAssistantTab)tab).Shown++;
            return Task.CompletedTask;
        }
    }
}

/// <summary>A device tab that records what the tools asked of it.</summary>
internal sealed class FakeAssistantTab(PortSession session, DeviceSettings settings) : IAssistantTab
{
    public PortSession Session { get; } = session;

    public DeviceSettings Settings { get; private set; } = settings;

    public string? LogPath { get; private set; }

    /// <summary>What local echo showed.</summary>
    public List<string> Echoed { get; } = [];

    public int Shown { get; set; }

    public int Cleared { get; private set; }

    public AssistantStatus AssistantStatus { get; private set; } = AssistantStatus.None;

    public Exception? LogFailure { get; set; }

    public Task OpenAsync() => Session.OpenAsync();

    public async Task<bool> SendAsync(byte[] data, string? typed)
    {
        if (typed is not null && Settings.Terminal.LocalEcho)
            Echoed.Add(typed);
        return await Session.SendAsync(data);
    }

    public async Task<bool> ChangeSettingsAsync(DeviceSettings settings)
    {
        if (settings.Serial != Settings.Serial && !await Session.ApplySettingsAsync(settings.Serial))
            return false;
        Settings = settings;
        return true;
    }

    public Task<bool> SetControlLinesAsync(ControlLines lines) => Session.SetControlLinesAsync(lines);

    public Task ClearAsync()
    {
        Cleared++;
        return Task.CompletedTask;
    }

    public Task<string> StartLogAsync()
    {
        if (LogFailure is not null)
            return Task.FromException<string>(LogFailure);
        LogPath = $@"C:\Logs\{Session.Port.PortName}.log";
        return Task.FromResult(LogPath);
    }

    public Task StopLogAsync()
    {
        LogPath = null;
        return Task.CompletedTask;
    }

    public void ShowAssistantStatus(AssistantStatus status) => AssistantStatus = status;
}
