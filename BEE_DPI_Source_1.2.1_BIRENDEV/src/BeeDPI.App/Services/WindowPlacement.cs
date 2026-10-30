using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace BeeDPI.App.Services;

/// <summary>
/// Keeps the borderless windows inside the work area of the monitor they are
/// shown on, including high-DPI layouts where the logical work area is smaller
/// than the preferred window size.
/// </summary>
internal static class WindowPlacement
{
    private const uint MonitorDefaultToNearest = 2;
    private const double EdgeMargin = 12;

    public static void FitToWorkArea(
        Window window,
        double preferredWidth,
        double preferredHeight,
        double minimumWidth,
        double minimumHeight)
    {
        var (workWidth, workHeight) = GetWorkArea(window);

        window.MaxWidth = Math.Max(minimumWidth, workWidth - EdgeMargin);
        window.MaxHeight = Math.Max(minimumHeight, workHeight - EdgeMargin);
        window.MinWidth = Math.Min(minimumWidth, window.MaxWidth);
        window.MinHeight = Math.Min(minimumHeight, window.MaxHeight);

        window.Width = Math.Clamp(window.Width, window.MinWidth, Math.Min(preferredWidth, window.MaxWidth));
        window.Height = Math.Clamp(window.Height, window.MinHeight, Math.Min(preferredHeight, window.MaxHeight));
    }

    public static void CenterOnOwner(Window window)
    {
        var owner = window.Owner;
        if (owner is null)
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;

        var (workWidth, workHeight) = GetWorkArea(window);
        var left = owner.Left + ((owner.ActualWidth - window.Width) / 2);
        var top = owner.Top + ((owner.ActualHeight - window.Height) / 2);

        left = Math.Clamp(left, EdgeMargin, Math.Max(EdgeMargin, workWidth - window.Width - EdgeMargin));
        top = Math.Clamp(top, EdgeMargin, Math.Max(EdgeMargin, workHeight - window.Height - EdgeMargin));

        window.Left = left;
        window.Top = top;
    }

    private static (double Width, double Height) GetWorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero)
            {
                var info = new MonitorInfo
                {
                    cbSize = Marshal.SizeOf<MonitorInfo>()
                };

                if (GetMonitorInfo(monitor, ref info))
                {
                    var dpi = VisualTreeHelper.GetDpi(window);
                    return (
                        (info.rcWork.Right - info.rcWork.Left) / dpi.DpiScaleX,
                        (info.rcWork.Bottom - info.rcWork.Top) / dpi.DpiScaleY);
                }
            }
        }

        var workArea = SystemParameters.WorkArea;
        return (workArea.Width, workArea.Height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
