using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using COMrade.Core.Mcp;
using COMrade.Core.Serial;
using COMrade.Core.Sessions;
using COMrade.Core.Settings;
using COMrade.Core.Terminal;
using COMrade.Windowing;

namespace COMrade.ViewModels;

/// <summary>
/// The tab as AI assistants use it (see <see cref="IAssistantTab"/>): their calls arrive on the MCP
/// server's threads and run on the UI thread, as if the user had used the toolbar.
/// </summary>
public sealed partial class DeviceTabViewModel : IAssistantTab
{
    private AssistantStatus _assistant = AssistantStatus.None;

    /// <summary>An AI assistant is sending to the device, as if typing. Raised on the UI thread.</summary>
    public event EventHandler? AssistantSending;

    /// <summary>MCP in the toolbar, shown while AI access is set to per tab.</summary>
    public bool ShowMcpToggle => _appSettings.Current.AiAccess is AiAccess.PerTab;

    /// <summary>AI assistants may use the device while AI access is set to per tab; remembered for the device.</summary>
    public bool IsMcpEnabled
    {
        get => Settings.McpEnabled;
        set => _ = Editor.ChangeAsync(Settings with { McpEnabled = value });
    }

    /// <summary>An AI assistant connected now has used the tab.</summary>
    public bool IsAssistantConnected => _assistant.Clients > 0;

    /// <summary>For example "AI assistant · Sent "printenv" · 12:34:56".</summary>
    public string AssistantText
    {
        get
        {
            if (_assistant is not { Clients: > 0 and var clients })
                return string.Empty;

            var who = clients == 1 ? "AI assistant" : $"{clients} AI assistants";
            return _assistant is { LastAction: { } action, LastActionAt: { } at }
                ? $"{who} · {action} · {at.ToLocalTime():HH:mm:ss}"
                : who;
        }
    }

    string? IAssistantTab.LogPath => Volatile.Read(ref _log)?.Path;

    Task IAssistantTab.OpenAsync() => _ui.InvokeAsync(OpenAsync);

    // Shown like typed text, before it goes out; nothing is shown when it cannot go out. Like
    // typing, it brings the terminal to the newest output.
    async Task<bool> IAssistantTab.SendAsync(byte[] data, string? typed)
    {
        if (Session.State is not PortSessionState.Open)
            return false;
        _ui.TryEnqueue(() => AssistantSending?.Invoke(this, EventArgs.Empty));
        if (typed is not null && Settings.Terminal.LocalEcho)
            History.Write(KeyboardInput.Echo(typed));
        return await Session.SendAsync(data, TrafficSender.Assistant);
    }

    Task<bool> IAssistantTab.ChangeSettingsAsync(DeviceSettings settings) => _ui.InvokeAsync(async () =>
    {
        await Editor.ChangeAsync(settings);
        return Editor.Settings == settings;
    });

    Task<bool> IAssistantTab.SetControlLinesAsync(ControlLines lines) => _ui.InvokeAsync(() => SetControlLinesAsync(lines));

    Task IAssistantTab.ClearAsync() => _ui.InvokeAsync(Clear);

    // Into the log folder: nobody may be there to answer a Save dialog.
    Task<string> IAssistantTab.StartLogAsync() => _ui.InvokeAsync(() =>
    {
        StartLog();
        return _log?.Path ?? throw new IOException(_logError ?? "The log file could not be created.");
    });

    Task IAssistantTab.StopLogAsync() => _ui.InvokeAsync(StopLog);

    void IAssistantTab.ShowAssistantStatus(AssistantStatus status) => _ui.TryEnqueue(() =>
    {
        _assistant = status;
        OnPropertyChanged(nameof(IsAssistantConnected));
        OnPropertyChanged(nameof(AssistantText));
    });

    // The settings page saves on the UI thread.
    private void OnAppSettingsChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(ShowMcpToggle));
}
