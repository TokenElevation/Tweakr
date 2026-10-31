using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Tweakr.App;

public enum BackdropKind { Mica, Acrylic, Solid }

public static class Backdrop
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public static bool IsSupported => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>Win11 22H2+: Mica/Acrylic. Otherwise (or for Solid) a solid dark window.</summary>
    public static bool Apply(Window window, BackdropKind kind)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        int v = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
        if (Environment.OSVersion.Version.Build >= 22000)
        {
            int round = 2;   // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        }

        if (kind == BackdropKind.Solid || !IsSupported) return Solid(window, hwnd);

        int type = kind == BackdropKind.Acrylic ? 3 : 2;   // 2 = Mica, 3 = Acrylic (transient)
        if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int)) != 0) return Solid(window, hwnd);

        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is not null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
        window.Background = Brushes.Transparent;
        return true;
    }

    static bool Solid(Window window, IntPtr hwnd)
    {
        if (IsSupported)
        {
            int none = 1;   // DWMSBT_NONE
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
        }
        window.SetResourceReference(Window.BackgroundProperty, "WindowSolidBrush");
        return false;
    }
}