using Microsoft.Terminal.Wpf;
using Microsoft.UI.Xaml.Automation.Peers;

namespace COMrade.Controls;

/// <summary>
/// Windows Terminal's control with a working automation peer. The original asks
/// <see cref="FrameworkElementAutomationPeer.FromElement"/> for its own peer, which calls back into
/// <see cref="OnCreateAutomationPeer"/> until the stack overflows: Narrator, screen readers or any
/// other UI Automation client crashed the app as soon as they reached the terminal.
/// </summary>
public sealed partial class SafeTerminalControl : TerminalControl
{
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}
