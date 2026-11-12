using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;

namespace COMrade.Controls;

/// <summary>Puts text on the clipboard.</summary>
internal static class ClipboardText
{
    public static void Set(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        try
        {
            Clipboard.SetContent(package);
        }
        catch (COMException)
        {
            // Another program holds the clipboard; the copy is lost, as in any app.
        }
    }
}
