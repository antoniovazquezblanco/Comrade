namespace COMrade.Core.Serial;

/// <summary>The output control lines COMrade drives.</summary>
/// <remarks>
/// DTR is on and RTS off by default, as most terminals do: many USB CDC devices (RP2040, Arduino)
/// only send data once DTR is set, while on ESP32 native USB and on boards with the classic
/// auto-reset circuit RTS alone holds the chip in reset.
/// </remarks>
public readonly record struct ControlLines(bool Dtr, bool Rts)
{
    public static ControlLines Default { get; } = new(Dtr: true, Rts: false);
}
