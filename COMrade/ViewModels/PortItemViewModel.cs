using COMrade.Core.Ports;

namespace COMrade.ViewModels;

/// <summary>One port in the port list of the empty state and the new-tab menu.</summary>
public sealed class PortItemViewModel(SerialPortInfo port, string detail, bool isNew, bool isLastUsed)
{
    public SerialPortInfo Port { get; } = port;

    /// <summary>For example "COM5 · USB-SERIAL CH340".</summary>
    public string Title { get; } = $"{port.PortName} · {port.Description}";

    /// <summary>For example "Last used · 115200 8N1 · CR" or "Plugged in just now · will use 115200 8N1 · CR".</summary>
    public string Detail { get; } = detail;

    /// <summary>Plugged in while COMrade is running.</summary>
    public bool IsNew { get; } = isNew;

    /// <summary>The device used most recently, offered as the main choice.</summary>
    public bool IsLastUsed { get; } = isLastUsed;

    public bool IsNotLastUsed => !IsLastUsed;
}
