using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace GasGuard.Desktop.Services;

internal static class WindowTheme
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;

    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                var enabled = 1;
                if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
                }
            }
            catch
            {
            }
        };
    }

    public static void FitToMonitorWorkArea(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                var monitor = MonitorFromWindow(handle, 2);
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(monitor, ref info))
                {
                    return;
                }

                var dpi = VisualTreeHelper.GetDpi(window);
                var workWidth = (info.Work.Right - info.Work.Left) / dpi.DpiScaleX;
                var workHeight = (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY;
                var targetWidth = Math.Min(1440, Math.Max(window.MinWidth, workWidth - 32));
                var targetHeight = Math.Min(900, Math.Max(window.MinHeight, workHeight - 32));
                window.Width = targetWidth;
                window.Height = targetHeight;
                window.Left = info.Work.Left / dpi.DpiScaleX + Math.Max(0, (workWidth - targetWidth) / 2);
                window.Top = info.Work.Top / dpi.DpiScaleY + Math.Max(0, (workHeight - targetHeight) / 2);
            }
            catch
            {
            }
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
