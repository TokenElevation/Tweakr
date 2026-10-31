using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Tweakr.App;

/// <summary>
/// A WindowStyle=None window has no caption / min / max style bits, so Windows (DWM) skips its native
/// minimize, restore, maximize and open/close animations. Adding the bits back brings them back; WindowChrome
/// still removes the actual title bar. WS_SYSMENU is deliberately NOT set (and is cleared): it is the bit that
/// makes Windows 11 draw its own min/max/close buttons on top of the custom title bar.
/// </summary>
public static class NativeWindow
{
    const int GWL_STYLE = -16;
    const int WS_CAPTION = 0x00C00000, WS_SYSMENU = 0x00080000, WS_THICKFRAME = 0x00040000;
    const int WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;
    const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void EnableSystemAnimations(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        int style = GetWindowLong(hwnd, GWL_STYLE);
        style |= WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
        style &= ~WS_SYSMENU;   // no native caption buttons
        SetWindowLong(hwnd, GWL_STYLE, style);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        int enabled = 0;   // make sure DWM transitions are not force-disabled for this window
        DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref enabled, sizeof(int));
    }
}