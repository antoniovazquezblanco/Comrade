namespace COMrade.Core.Serial;

/// <summary>
/// Opens the serial port <paramref name="portName"/> with <paramref name="settings"/>, setting the
/// control lines to <paramref name="lines"/> as it opens.
/// </summary>
/// <exception cref="SerialPortException">The port could not be opened.</exception>
public delegate Task<ISerialConnection> OpenSerialConnection(
    string portName,
    SerialSettings settings,
    ControlLines lines,
    CancellationToken cancellationToken);
