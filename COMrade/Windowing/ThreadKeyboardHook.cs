using System;
using System.Runtime.InteropServices;
using Windows.System;

namespace COMrade.Windowing;

/// <summary>
/// Sees every key pressed in the app's UI thread before any window does. The terminal is a native
/// window that keeps keys from WinUI's keyboard accelerators, so app shortcuts are handled here.
/// </summary>
public sealed class ThreadKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD = 2;
    private const int HC_ACTION = 0;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const long KeyUpFlag = 1L << 31;

    private readonly Func<VirtualKey, bool, bool> _onKeyDown;
    // Kept in a field: the native hook calls it for as long as the hook is installed.
    private readonly HookProc _proc;
    private IntPtr _hook;

    /// <param name="onKeyDown">
    /// Called with the key and whether Shift is down, for keys pressed with Ctrl (and without Alt);
    /// returns true when it handled the key, which then reaches no window.
    /// </param>
    public ThreadKeyboardHook(Func<VirtualKey, bool, bool> onKeyDown)
    {
        _onKeyDown = onKeyDown;
        _proc = OnKey;
        _hook = SetWindowsHookEx(WH_KEYBOARD, _proc, IntPtr.Zero, GetCurrentThreadId());
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException($"Could not install the keyboard hook (error {Marshal.GetLastWin32Error()}).");
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero)
            return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == HC_ACTION
            && (lParam.ToInt64() & KeyUpFlag) == 0
            && IsDown(VK_CONTROL)
            && !IsDown(VK_MENU)
            && _onKeyDown((VirtualKey)wParam.ToInt32(), IsDown(VK_SHIFT)))
            return 1;

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool IsDown(int key) => GetKeyState(key) < 0;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hmod, uint dwThreadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
