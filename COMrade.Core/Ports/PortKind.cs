namespace COMrade.Core.Ports;

/// <summary>What kind of port this is; also the order of the groups in the port list.</summary>
public enum PortKind
{
    /// <summary>USB adapter or board, e.g. CH340, FTDI, CP210x, ESP32 native USB.</summary>
    Usb,

    /// <summary>Port on the motherboard, e.g. COM1.</summary>
    BuiltIn,

    /// <summary>Port created by software, e.g. com0com pairs.</summary>
    Virtual,

    /// <summary>Bluetooth serial port profile; listed last, as it is rarely what the user plugged in.</summary>
    Bluetooth,
}
