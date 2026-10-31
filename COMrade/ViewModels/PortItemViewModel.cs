using COMrade.Core.Ports;

namespace COMrade.ViewModels;

/// <summary>One port in the port list of the empty state and the new-tab menu.</summary>
public sealed class PortItemViewModel(SerialPortInfo port, string? name, string detail, bool isNew, bool isLastUsed)
{
    public SerialPortInfo Port { get; } = port;

    /// <summary>
    /// For example "COM5 · USB-SERIAL CH340", "COM5 · Router console" for a renamed device, or
    /// "COM25 · Prolific PL2303HXA" for an adapter its driver refuses.
    /// </summary>
    public string Title { get; } =
        $"{port.PortName} · {(port.IsRefusedByDriver ? DisplayText.RefusedAdapterName(port) : name ?? port.Description)}";

    /// <summary>For example "Last used · 115200 8N1 · CR" or "Plugged in just now · will use 115200 8N1 · CR".</summary>
    public string Detail { get; } = detail;

    /// <summary>Plugged in while COMrade is running.</summary>
    public bool IsNew { get; } = isNew;

    /// <summary>An adapter its driver refuses: offers the driver help instead of Select.</summary>
    public bool NeedsDriverHelp => Port.IsRefusedByDriver;

    /// <summary>The device used most recently, offered as the main choice.</summary>
    public bool ShowMainSelect => isLastUsed && !NeedsDriverHelp;

    public bool ShowSelect => !isLastUsed && !NeedsDriverHelp;
}
