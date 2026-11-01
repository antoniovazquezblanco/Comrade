namespace COMrade.Core.Serial;

/// <summary>The input lines the device drives. Ring is left out: System.IO.Ports does not report it.</summary>
[Flags]
public enum ModemLines
{
    None = 0,
    Cts = 1,
    Dsr = 2,
    CarrierDetect = 4,
}
