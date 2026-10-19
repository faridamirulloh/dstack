using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MiniCodex.Presentation;

public static class DarkTitleBar
{
    public static void Apply(Window window)
    {
        var dark = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
