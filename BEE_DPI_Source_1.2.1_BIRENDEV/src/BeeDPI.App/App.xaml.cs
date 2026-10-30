using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using BeeDPI.App.Services;
using BeeDPI.App.ViewModels;

namespace BeeDPI.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\BeeDPI.App.v1";

    private Mutex? _singleInstance;
    private bool _ownsSingleInstance;
    private MainWindow? _mainWindow;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        _ownsSingleInstance = createdNew;
        if (!createdNew)
        {
            MessageBox.Show(
                "BEE DPI đang chạy.",
                "BEE DPI",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();

        base.OnStartup(e);

        try
        {
            _mainWindow = new MainWindow();
            MainWindow = _mainWindow;

            _tray = new TrayIcon();
            _tray.OpenRequested += (_, _) => _mainWindow?.RestoreFromTray();
            _tray.ToggleRequested += (_, _) => ToggleFromTray();
            _tray.ExitRequested += (_, _) => _mainWindow?.RequestExit();

            _mainWindow.HiddenToTray += (_, _) => _tray?.ShowRunningBalloon();
            _mainWindow.ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            UpdateTrayState();
            _mainWindow.Show();
        }
        catch (Exception ex)
        {
            AppLog.LogStartupFailure("MainWindow initialization", "BeeMarkImage", ex);
            Shutdown();
            return;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mainWindow is not null)
        {
            _mainWindow.ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        // Remove the notification-area icon before the process disappears so it
        // never lingers as a dead icon in the tray.
        _tray?.Dispose();
        _tray = null;

        if (_ownsSingleInstance)
        {
            _singleInstance?.ReleaseMutex();
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void ToggleFromTray()
    {
        var command = _mainWindow?.ViewModel.ToggleCommand;
        if (command is not null && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.State))
        {
            UpdateTrayState();
        }
    }

    private void UpdateTrayState()
    {
        var viewModel = _mainWindow?.ViewModel;
        if (viewModel is null || _tray is null)
        {
            return;
        }

        _tray.UpdateToggle(viewModel.TrayActionText, !viewModel.IsBusy);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.LogStartupFailure("dispatcher", null, e.Exception);
        MessageBox.Show(
            "BEE DPI gặp lỗi không mong muốn khi khởi động. Vui lòng mở lại ứng dụng.",
            "BEE DPI",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown();
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Error("unhandled exception", e.ExceptionObject as Exception);
    }
}

internal static class AppLog
{
    private static readonly object Sync = new();
    private static readonly string? Path = CreatePath();

    public static void Info(string message) => Write("INFO  " + message);

    public static void Warn(string message) => Write("WARN  " + message);

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR " + message);
        if (exception is not null)
        {
            Write(exception.GetType().Name + ": " + exception.Message);
            Write(exception.StackTrace ?? string.Empty);
        }
    }

    public static void LogStartupFailure(string phase, string? resourceKey, Exception exception)
    {
        Write("STARTUP FAILURE  phase=" + phase);
        if (resourceKey is not null)
        {
            Write("STARTUP FAILURE  resourceKey=" + resourceKey);
        }
        Write("STARTUP FAILURE  exceptionType=" + exception.GetType().FullName);
        Write("STARTUP FAILURE  message=" + exception.Message);
        Write("STARTUP FAILURE  toString=" + exception.ToString());

        if (exception is XamlParseException xamlEx)
        {
            Write("STARTUP FAILURE  xamlLineNumber=" + xamlEx.LineNumber);
            Write("STARTUP FAILURE  xamlLinePosition=" + xamlEx.LinePosition);
            Write("STARTUP FAILURE  xamlBaseUri=" + xamlEx.BaseUri);
        }

        var inner = exception.InnerException;
        var depth = 0;
        while (inner is not null)
        {
            Write("STARTUP FAILURE  inner[" + depth + "]  type=" + inner.GetType().FullName);
            Write("STARTUP FAILURE  inner[" + depth + "]  message=" + inner.Message);
            Write("STARTUP FAILURE  inner[" + depth + "]  toString=" + inner.ToString());
            inner = inner.InnerException;
            depth++;
        }
    }

    private static void Write(string line)
    {
        if (Path is null)
        {
            return;
        }

        try
        {
            lock (Sync)
            {
                File.AppendAllText(
                    Path,
                    DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + "  " + line + Environment.NewLine,
                    Encoding.UTF8);

                var info = new FileInfo(Path);
                if (info.Exists && info.Length > 512 * 1024)
                {
                    var trimmed = File.ReadAllLines(Path).TakeLast(2000);
                    File.WriteAllLines(Path, trimmed, Encoding.UTF8);
                }
            }
        }
        catch
        {
            // Diagnostics must never crash the app.
        }
    }

    private static string? CreatePath()
    {
        try
        {
            var folder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BeeDPI",
                "logs");
            Directory.CreateDirectory(folder);
            return System.IO.Path.Combine(folder, "app.log");
        }
        catch
        {
            return null;
        }
    }
}
