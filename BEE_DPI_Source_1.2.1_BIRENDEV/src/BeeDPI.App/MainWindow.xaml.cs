using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BeeDPI.App.Services;
using BeeDPI.App.ViewModels;
using BeeDPI.Core;

namespace BeeDPI.App;

/// <summary>
/// Borderless, compact BEE DPI window. The close button hides the window to the
/// notification area; a real exit always stops the owned engine first.
/// </summary>
public partial class MainWindow : Window
{
    private const double FrameCornerRadius = 21;

    private readonly MainViewModel _viewModel = new();

    private DnsWindow? _dnsWindow;
    private bool _allowClose;
    private bool _isShuttingDown;

    public MainWindow()
    {
        InitializeComponent();

        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        SizeChanged += (_, _) => UpdateFrameClip();
    }

    /// <summary>Raised when the window is hidden instead of closed.</summary>
    public event EventHandler? HiddenToTray;

    /// <summary>Set by <see cref="App"/> before a real exit is requested.</summary>
    public bool IsExitRequested { get; set; }

    public MainViewModel ViewModel => _viewModel;

    /// <summary>Asks the window to stop the session and close for real.</summary>
    public void RequestExit()
    {
        IsExitRequested = true;
        AppLog.Info("exit requested");
        Close();
    }

    public void RestoreFromTray()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();

        // Nudge the window to the foreground; Windows may keep a background
        // process behind the current app otherwise.
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // Windows 11 rounds the transparent frame; older Windows ignores this.
        var preference = 2;
        _ = DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));

        // 150% scaling on a 768p screen leaves a very small logical work area.
        // Shrink the compact window instead of letting it clip off-screen.
        WindowPlacement.FitToWorkArea(this, preferredWidth: 424, preferredHeight: 560, minimumWidth: 380, minimumHeight: 430);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateFrameClip();

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        BeginAnimation(OpacityProperty, fadeIn);
    }

    /// <summary>
    /// Clips the content to the rounded frame so no square glow artifacts
    /// appear in the transparent window corners.
    /// </summary>
    private void UpdateFrameClip()
    {
        if (FrameContent.ActualWidth <= 0 || FrameContent.ActualHeight <= 0)
        {
            return;
        }

        FrameContent.Clip = new RectangleGeometry(
            new Rect(0, 0, FrameContent.ActualWidth, FrameContent.ActualHeight),
            FrameCornerRadius,
            FrameCornerRadius);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.State))
        {
            return;
        }

        var breathe = new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        StatusPanel.BeginAnimation(OpacityProperty, breathe);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The mouse was released before the drag started.
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DnsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dnsWindow is not null)
        {
            _dnsWindow.Activate();
            return;
        }

        var window = new DnsWindow(_viewModel.Dns)
        {
            Owner = this
        };

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_dnsWindow, window))
            {
                _dnsWindow = null;
            }
        };

        _dnsWindow = window;
        window.Show();
    }

    private void FacebookButton_Click(object sender, RoutedEventArgs e) =>
        OpenLink(Branding.FacebookUrl);

    private void TikTokButton_Click(object sender, RoutedEventArgs e) =>
        OpenLink(Branding.TikTokUrl);

    private void DiscordButton_Click(object sender, RoutedEventArgs e) =>
        OpenLink(Branding.DiscordUrl);

    private static void OpenLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Warn("open link: " + exception.Message);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_isShuttingDown)
        {
            return;
        }

        if (!IsExitRequested)
        {
            HideToTray();
            return;
        }

        _isShuttingDown = true;
        IsEnabled = false;
        AppLog.Info("window closing: stopping session");
        _ = ShutdownAndCloseAsync();
    }

    private void HideToTray()
    {
        _dnsWindow?.Hide();
        Hide();
        AppLog.Info("window hidden to tray");
        HiddenToTray?.Invoke(this, EventArgs.Empty);
    }

    private async Task ShutdownAndCloseAsync()
    {
        try
        {
            await _viewModel.ShutdownAsync();
        }
        catch (Exception exception)
        {
            AppLog.Error("shutdown", exception);
        }
        finally
        {
            AppLog.Info("window closing: session stopped");
            _allowClose = true;

            if (_dnsWindow is not null)
            {
                _dnsWindow.Close();
                _dnsWindow = null;
            }

            // This method can complete synchronously while the original Closing
            // event is still on the stack, so queue the final close.
            _ = Dispatcher.BeginInvoke(
                new Action(Close),
                System.Windows.Threading.DispatcherPriority.Normal);
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);
}
