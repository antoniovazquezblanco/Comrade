using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using COMrade.Core.Serial;
using COMrade.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace COMrade.ViewModels;

/// <summary>
/// The fields of a device's settings, for the port settings of a tab and the new-device defaults of
/// the settings page. Every change is passed to <c>apply</c>; when it refuses the change (e.g. the
/// driver rejects the speed), the fields go back to the previous settings.
/// </summary>
public sealed partial class DeviceSettingsEditor(DeviceSettings settings, Func<DeviceSettings, Task<bool>> apply) : ObservableObject
{
    private static readonly int[] DataBitsValues = [8, 7, 6, 5];
    private static readonly LineEnding[] SendLineEndingValues = [LineEnding.Cr, LineEnding.Lf, LineEnding.CrLf];

    public static IReadOnlyList<string> BaudRateOptions { get; } =
        ["300", "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200", "230400", "460800", "921600", "1000000", "2000000"];

    public static IReadOnlyList<string> DataBitsOptions { get; } = ["8", "7", "6", "5"];

    public static IReadOnlyList<string> ParityOptions { get; } = ["None", "Odd", "Even", "Mark", "Space"];

    public static IReadOnlyList<string> StopBitsOptions { get; } = ["1", "1.5", "2"];

    public static IReadOnlyList<string> FlowControlOptions { get; } = ["None", "RTS/CTS", "XON/XOFF"];

    public static IReadOnlyList<string> SendLineEndingOptions { get; } = ["CR", "LF", "CR+LF"];

    public DeviceSettings Settings { get; private set; } = settings;

    public string BaudRateText
    {
        get => Settings.Serial.BaudRate.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var baudRate) && baudRate > 0)
                _ = ChangeAsync(Settings with { Serial = Settings.Serial with { BaudRate = baudRate } });
        }
    }

    public int DataBitsIndex
    {
        get => Array.IndexOf(DataBitsValues, Settings.Serial.DataBits);
        set
        {
            if (value >= 0)
                _ = ChangeAsync(Settings with { Serial = Settings.Serial with { DataBits = DataBitsValues[value] } });
        }
    }

    public int ParityIndex
    {
        get => (int)Settings.Serial.Parity;
        set
        {
            if (value >= 0)
                _ = ChangeAsync(Settings with { Serial = Settings.Serial with { Parity = (Parity)value } });
        }
    }

    public int StopBitsIndex
    {
        get => (int)Settings.Serial.StopBits;
        set
        {
            if (value >= 0)
                _ = ChangeAsync(Settings with { Serial = Settings.Serial with { StopBits = (StopBits)value } });
        }
    }

    public int FlowControlIndex
    {
        get => (int)Settings.Serial.FlowControl;
        set
        {
            if (value >= 0)
                _ = ChangeAsync(Settings with { Serial = Settings.Serial with { FlowControl = (FlowControl)value } });
        }
    }

    public int SendLineEndingIndex
    {
        get => Math.Max(0, Array.IndexOf(SendLineEndingValues, Settings.Serial.SendLineEnding));
        set
        {
            if (value >= 0)
                _ = ChangeAsync(Settings with { Serial = Settings.Serial with { SendLineEnding = SendLineEndingValues[value] } });
        }
    }

    public bool LocalEcho
    {
        get => Settings.Terminal.LocalEcho;
        set => _ = ChangeAsync(Settings with { Terminal = Settings.Terminal with { LocalEcho = value } });
    }

    public bool NewLineOnLf
    {
        get => Settings.Terminal.NewLineOnLf;
        set => _ = ChangeAsync(Settings with { Terminal = Settings.Terminal with { NewLineOnLf = value } });
    }

    public bool NewLineOnCr
    {
        get => Settings.Terminal.NewLineOnCr;
        set => _ = ChangeAsync(Settings with { Terminal = Settings.Terminal with { NewLineOnCr = value } });
    }

    public bool ReopenAutomatically
    {
        get => Settings.ReopenAutomatically;
        set => _ = ChangeAsync(Settings with { ReopenAutomatically = value });
    }

    /// <summary>Applies a change made elsewhere, such as the tab's name, as the fields do.</summary>
    public async Task ChangeAsync(DeviceSettings changed)
    {
        if (changed == Settings)
            return;

        if (await apply(changed))
            Settings = changed;
        OnPropertyChanged(string.Empty);
    }
}
