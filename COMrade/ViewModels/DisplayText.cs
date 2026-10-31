using System;
using System.Globalization;
using COMrade.Core.Ports;
using COMrade.Core.Serial;

namespace COMrade.ViewModels;

/// <summary>Short texts shown for ports, settings and counters.</summary>
public static class DisplayText
{
    /// <summary>For example "115200 8N1 · CR".</summary>
    public static string Settings(SerialSettings settings) =>
        $"{settings.BaudRate} {settings.FrameNotation} · {LineEnding(settings.SendLineEnding)}";

    public static string LineEnding(LineEnding ending) => ending switch
    {
        Core.Serial.LineEnding.Lf => "LF",
        Core.Serial.LineEnding.CrLf => "CR+LF",
        _ => "CR",
    };

    /// <summary>What kind of port this is, for example "USB 1A86:7523", "Bluetooth" or "Built-in".</summary>
    public static string Kind(SerialPortInfo port) => port.Kind switch
    {
        PortKind.Usb => $"USB {port.Identity.VendorId:X4}:{port.Identity.ProductId:X4}",
        PortKind.Bluetooth => "Bluetooth",
        PortKind.BuiltIn => "Built-in",
        _ => "Virtual",
    };

    public static string Bytes(long count) => count switch
    {
        < 1024 => $"{count} B",
        < 1024 * 1024 => string.Format(CultureInfo.CurrentCulture, "{0:0.0} KB", count / 1024.0),
        _ => string.Format(CultureInfo.CurrentCulture, "{0:0.0} MB", count / (1024.0 * 1024)),
    };

    public static string Duration(TimeSpan duration) =>
        $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
}
