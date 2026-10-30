using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace BeeDPI.App.Services;

/// <summary>
/// Notification-area icon built on <c>Shell_NotifyIcon</c>. It owns a message
/// window for callbacks, a themed context menu and the loaded icon handle; no
/// third-party tray dependency is required.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int WmApp = 0x8000;
    private const int TrayCallbackMessage = WmApp + 1;

    private const int WmContextMenu = 0x007B;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmLeftButtonDoubleClick = 0x0203;
    private const int WmRightButtonUp = 0x0205;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;

    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;

    private const uint NiifInfo = 0x00000001;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;

    private const int SmCxSmIcon = 49;
    private const int SmCySmIcon = 50;

    private readonly HwndSource _source;
    private readonly IntPtr _icon;
    private readonly ContextMenu _menu;
    private readonly MenuItem _toggleItem;
    private readonly uint _id = 1;

    private NotifyIconData _data;
    private bool _hasBalloon;
    private bool _disposed;

    public TrayIcon()
    {
        var parameters = new HwndSourceParameters("BeeDPITrayWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        _icon = LoadIconFromAssets();
        _menu = BuildMenu(out _toggleItem);

        _data = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _source.Handle,
            uID = _id,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _icon,
            szTip = "BEE DPI",
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };

        if (Shell_NotifyIcon(NimAdd, ref _data))
        {
            AppLog.Info("tray icon ready (window 0x" + _source.Handle.ToString("X") + ")");
        }
        else
        {
            AppLog.Warn("tray icon could not be added");
        }
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ToggleRequested;

    public event EventHandler? ExitRequested;

    public void UpdateToggle(string text, bool enabled)
    {
        _toggleItem.Header = text;
        _toggleItem.IsEnabled = enabled;
    }

    /// <summary>Shows one informational balloon per session.</summary>
    public void ShowRunningBalloon()
    {
        if (_hasBalloon || _disposed)
        {
            return;
        }

        _hasBalloon = true;
        var data = _data;
        data.uFlags = NifInfo;
        data.szInfoTitle = "BEE DPI";
        data.szInfo = "BEE DPI vẫn đang chạy trong khay hệ thống.";
        data.dwInfoFlags = NiifInfo;
        _ = Shell_NotifyIcon(NimModify, ref data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = Shell_NotifyIcon(NimDelete, ref _data);
        _menu.IsOpen = false;

        if (_source.Handle != IntPtr.Zero)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
        }

        if (_icon != IntPtr.Zero)
        {
            _ = DestroyIcon(_icon);
        }
    }

    private ContextMenu BuildMenu(out MenuItem toggleItem)
    {
        var open = new MenuItem { Header = "Mở BEE DPI", Style = FindStyle("TrayMenuItemStyle") };
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        toggleItem = new MenuItem { Header = "Kết nối", Style = FindStyle("TrayMenuItemStyle") };
        toggleItem.Click += (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty);

        var separator = new Separator { Style = FindStyle("TraySeparatorStyle") };

        var exit = new MenuItem { Header = "Thoát BEE DPI", Style = FindStyle("TrayMenuItemStyle") };
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint,
            Items = { open, toggleItem, separator, exit }
        };

        return menu;
    }

    private static Style? FindStyle(string key) =>
        Application.Current?.TryFindResource(key) as Style;

    private static IntPtr LoadIconFromAssets()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "bee.ico");
            if (!File.Exists(path))
            {
                return IntPtr.Zero;
            }

            var width = GetSystemMetrics(SmCxSmIcon);
            var height = GetSystemMetrics(SmCySmIcon);
            return LoadImage(IntPtr.Zero, path, ImageIcon, width, height, LrLoadFromFile);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != TrayCallbackMessage)
        {
            return IntPtr.Zero;
        }

        var mouseMessage = unchecked((int)(long)lParam) & 0xFFFF;
        switch (mouseMessage)
        {
            case WmLeftButtonUp:
            case WmLeftButtonDoubleClick:
                OpenRequested?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;

            case WmRightButtonUp:
            case WmContextMenu:
                ShowMenu();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        _menu.IsOpen = false;
        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr instance,
        string name,
        uint type,
        int cx,
        int cy,
        uint load);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
