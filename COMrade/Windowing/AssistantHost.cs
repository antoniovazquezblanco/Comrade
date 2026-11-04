using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using COMrade.Core.Mcp;
using COMrade.Core.Ports;
using COMrade.ViewModels;
using Microsoft.UI.Dispatching;

namespace COMrade.Windowing;

/// <summary>
/// COMrade's windows as AI assistants use them. Assistants call from the MCP server's threads;
/// the tabs live on the UI thread, so every call goes there.
/// </summary>
public sealed class AssistantHost(WindowManager windows) : IAssistantHost
{
    // Created on the UI thread, in App.OnLaunched.
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();

    public Task<IReadOnlyList<IAssistantTab>> GetTabsAsync() =>
        _ui.InvokeAsync<IReadOnlyList<IAssistantTab>>(() => [.. windows.DeviceTabs]);

    public Task<IAssistantTab> OpenTabAsync(SerialPortInfo port) =>
        _ui.InvokeAsync<IAssistantTab>(() => windows.OpenTab(port));

    public Task ShowTabAsync(IAssistantTab tab) =>
        _ui.InvokeAsync(() => windows.Show((DeviceTabViewModel)tab));
}
