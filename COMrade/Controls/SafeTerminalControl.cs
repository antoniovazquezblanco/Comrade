using System.Reflection;
using Microsoft.Terminal.Wpf;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace COMrade.Controls;

/// <summary>Windows Terminal's control with two crashes fixed.</summary>
public sealed partial class SafeTerminalControl : TerminalControl
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private readonly MethodInfo? _passFocus;
    private readonly object? _container;

    public SafeTerminalControl()
    {
        // The control hands focus to its native window from an async void GettingFocus handler
        // that sets args.Cancel, which throws when the focus move cannot be cancelled (e.g. the
        // focused "Try again" button disappears as the port opens), and the throw ends the app.
        var original = typeof(TerminalControl).GetMethod("TerminalControl_GettingFocus", Private);
        _container = typeof(TerminalControl).GetField("termContainer", Private)?.GetValue(this);
        _passFocus = _container?.GetType().GetMethod("PassFocus", Private);
        if (original is null || _passFocus is null)
            return;

        GettingFocus -= original.CreateDelegate<TypedEventHandler<UIElement, GettingFocusEventArgs>>(this);
        GettingFocus += OnGettingFocus;
    }

    // The original asks FrameworkElementAutomationPeer.FromElement for its own peer, which calls
    // back into OnCreateAutomationPeer until the stack overflows: Narrator, screen readers or any
    // other UI Automation client crashed the app as soon as they reached the terminal.
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    private void OnGettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        if (args.TryCancel())
            PassFocus();
        else
            // Focus lands on the control for now; move it to the native window once the move is over.
            DispatcherQueue.TryEnqueue(PassFocus);
    }

    private void PassFocus() => _passFocus!.Invoke(_container, null);
}
