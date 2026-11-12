using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Terminal.Wpf;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace COMrade.Controls;

/// <summary>Windows Terminal's control with two crashes fixed and ways to scroll it and clear its selection.</summary>
public sealed partial class SafeTerminalControl : TerminalControl
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private readonly MethodInfo? _passFocus;
    private readonly MethodInfo? _userScroll;
    private readonly FieldInfo? _terminal;
    private readonly object? _container;

    public SafeTerminalControl()
    {
        // The control hands focus to its native window from an async void GettingFocus handler
        // that sets args.Cancel, which throws when the focus move cannot be cancelled (e.g. the
        // focused "Try again" button disappears as the port opens), and the throw ends the app.
        var original = typeof(TerminalControl).GetMethod("TerminalControl_GettingFocus", Private);
        _container = typeof(TerminalControl).GetField("termContainer", Private)?.GetValue(this);
        _passFocus = _container?.GetType().GetMethod("PassFocus", Private);
        _userScroll = _container?.GetType().GetMethod("UserScroll", Private);
        _terminal = _container?.GetType().GetField("terminal", Private);
        if (original is null || _passFocus is null)
            return;

        GettingFocus -= original.CreateDelegate<TypedEventHandler<UIElement, GettingFocusEventArgs>>(this);
        GettingFocus += OnGettingFocus;
    }

    // The original asks FrameworkElementAutomationPeer.FromElement for its own peer, which calls
    // back into OnCreateAutomationPeer until the stack overflows: Narrator, screen readers or any
    // other UI Automation client crashed the app as soon as they reached the terminal.
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    /// <summary>
    /// Shows the scrollback from line <paramref name="top"/>, as dragging the scroll bar there does.
    /// Any top past the last screen shows the newest output.
    /// </summary>
    public void ScrollTo(int top)
    {
        // Nothing to scroll before the terminal has been laid out.
        if (_userScroll is null || Rows <= 0 || FindName("scrollbar") is not ScrollBar scrollBar)
            return;

        // The scroll bar keeps its value within its range. The terminal is told the top as asked:
        // the scroll bar's maximum can lag behind output still being written, and would stop a
        // few lines short of the newest.
        scrollBar.Value = top;
        _userScroll.Invoke(_container, [Math.Max(top, 0)]);
    }

    /// <summary>
    /// Clears the selection, as typing does. New output does not move the view while text is
    /// selected, and the selection stays until the user clicks or types.
    /// </summary>
    public void ClearSelection()
    {
        // The native terminal exists while the control is loaded. Escape clears the selection and
        // goes no further, but without a selection it would reach the device as typed.
        if (_terminal?.GetValue(_container) is nint terminal && terminal != 0 && TerminalIsSelectionActive(terminal))
            TerminalSendCharEvent(terminal, '\e', 0, 0);
    }

    private void OnGettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        if (args.TryCancel())
            PassFocus();
        else
            // Focus lands on the control for now; move it to the native window once the move is over.
            DispatcherQueue.TryEnqueue(PassFocus);
    }

    private void PassFocus() => _passFocus!.Invoke(_container, null);

    [DllImport("Microsoft.Terminal.Control.dll")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool TerminalIsSelectionActive(IntPtr terminal);

    [DllImport("Microsoft.Terminal.Control.dll", CharSet = CharSet.Unicode)]
    private static extern void TerminalSendCharEvent(IntPtr terminal, char ch, ushort scanCode, ushort flags);
}
