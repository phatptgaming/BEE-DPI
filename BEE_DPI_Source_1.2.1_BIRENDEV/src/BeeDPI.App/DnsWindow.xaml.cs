using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BeeDPI.App.Services;
using BeeDPI.App.ViewModels;

namespace BeeDPI.App;

/// <summary>
/// Separate DNS Manager window. It shares the design tokens with the main
/// window and only exposes the network, provider, profile and the two actions.
/// </summary>
public partial class DnsWindow : Window
{
    private const double FrameCornerRadius = 21;

    private readonly DnsViewModel _viewModel;
    private bool _initialized;

    public DnsWindow(DnsViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        SourceInitialized += (_, _) =>
            WindowPlacement.FitToWorkArea(this, preferredWidth: 404, preferredHeight: 556, minimumWidth: 340, minimumHeight: 380);
        Loaded += OnLoaded;
        SizeChanged += (_, _) => UpdateFrameClip();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowPlacement.CenterOnOwner(this);
        UpdateFrameClip();

        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            AppLog.Warn("dns window initialize: " + exception.Message);
        }
    }

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
}
