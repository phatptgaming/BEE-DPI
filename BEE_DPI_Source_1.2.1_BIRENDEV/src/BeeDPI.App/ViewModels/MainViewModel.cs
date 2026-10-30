using System.Windows;
using System.Windows.Input;
using BeeDPI.App.Services;
using BeeDPI.Core;
using BeeDPI.Core.Connectivity;
using BeeDPI.Core.Dns;

namespace BeeDPI.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly TimeSpan SteamRefreshInterval = TimeSpan.FromSeconds(60);

    private readonly EngineSession _session = new();
    private readonly AsyncRelayCommand _toggleCommand;
    private readonly SteamProbe _steamProbe = new();

    private EngineConnectionState _state = EngineConnectionState.Disconnected;
    private string _statusDetail = "Nhấn KẾT NỐI để bắt đầu.";
    private CancellationTokenSource? _connectCts;
    private CancellationTokenSource? _steamCts;
    private CancellationTokenSource? _resetCts;
    private SteamProbeResult? _steamResult;
    private bool _steamChecking;

    public MainViewModel()
    {
        _toggleCommand = new AsyncRelayCommand(ToggleAsync, () => !IsBusy);
        _session.Faulted += OnSessionFaulted;
        Dns = new DnsViewModel(new DnsSession(), new FileDnsBackupStore());
    }

    public DnsViewModel Dns { get; }

    public string ProductName => Branding.ProductName;

    public string DeveloperCredit => "Developed by " + Branding.Developer;

    public string VersionLabel => Branding.VersionLabel;

    public EngineConnectionState State => _state;

    /// <summary>
    /// The only connection states an ordinary user needs. Steam probe results
    /// are written to the log, never to the window.
    /// </summary>
    public string StatusText => _state switch
    {
        EngineConnectionState.Connecting => ConnectionStatusText.Connecting,
        EngineConnectionState.Connected => ConnectionStatusText.Connected,
        EngineConnectionState.Disconnecting => ConnectionStatusText.Disconnecting,
        EngineConnectionState.Error => ConnectionStatusText.Failed,
        _ => ConnectionStatusText.Idle
    };

    public string StatusHint => _state switch
    {
        EngineConnectionState.Connecting => "Đang chuẩn bị kết nối...",
        EngineConnectionState.Disconnecting => "Đang dừng kết nối...",
        EngineConnectionState.Error => "Vui lòng thử lại.",
        _ => "Nhấn KẾT NỐI để bắt đầu."
    };

    public bool ShowStatusHint => _state != EngineConnectionState.Connected;

    public string ButtonText => _state switch
    {
        EngineConnectionState.Connected => "HỦY KẾT NỐI",
        EngineConnectionState.Connecting => "ĐANG KẾT NỐI...",
        EngineConnectionState.Disconnecting => "ĐANG HỦY KẾT NỐI...",
        _ => "KẾT NỐI"
    };

    /// <summary>Label used by the tray context menu.</summary>
    public string TrayActionText => _state switch
    {
        EngineConnectionState.Connected => "Hủy kết nối",
        EngineConnectionState.Connecting or EngineConnectionState.Disconnecting => "Đang xử lý...",
        _ => "Kết nối"
    };

    public string StatusDetail => _statusDetail;

    public bool IsBusy => _state is EngineConnectionState.Connecting or EngineConnectionState.Disconnecting;

    public bool IsConnected => _state == EngineConnectionState.Connected;

    public bool HasActiveSession => _session.HasHost || IsBusy || IsConnected;

    public ICommand ToggleCommand => _toggleCommand;

    public async Task ShutdownAsync()
    {
        AppLog.Info("viewmodel shutdown: begin");
        _resetCts?.Cancel();
        _connectCts?.Cancel();
        _steamCts?.Cancel();

        // DNS is intentionally independent of the engine. Finish any in-flight
        // DNS operation (never abort it midway) but never restore DNS here.
        try
        {
            await Dns.WaitForIdleAsync().ConfigureAwait(true);
            await Dns.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            AppLog.Warn("shutdown dns: " + exception.Message);
        }

        if (HasActiveSession)
        {
            SetState(EngineConnectionState.Disconnecting, "Đang dừng engine an toàn...");
        }

        try
        {
            await _session.DisconnectAsync(force: true).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            AppLog.Warn("shutdown: " + exception.Message);
        }

        await _session.DisposeAsync().ConfigureAwait(true);
        _steamCts?.Dispose();
        _steamCts = null;
        _steamProbe.Dispose();
        SetState(EngineConnectionState.Disconnected, "Nhấn KẾT NỐI để bắt đầu.");
        AppLog.Info("viewmodel shutdown: end");
    }

    private async Task ToggleAsync()
    {
        if (_state == EngineConnectionState.Connected)
        {
            await DisconnectAsync().ConfigureAwait(true);
            return;
        }

        if (_state is EngineConnectionState.Disconnected
            or EngineConnectionState.DisconnectedJustNow
            or EngineConnectionState.Error)
        {
            await ConnectAsync().ConfigureAwait(true);
        }
    }

    private async Task ConnectAsync()
    {
        _resetCts?.Cancel();
        _connectCts?.Dispose();
        _connectCts = new CancellationTokenSource();

        _steamCts?.Cancel();
        _steamCts?.Dispose();
        _steamCts = CancellationTokenSource.CreateLinkedTokenSource(_connectCts.Token);

        _steamResult = null;
        _steamChecking = false;

        SetState(EngineConnectionState.Connecting, "Đang khởi động engine và xin quyền quản trị...");

        try
        {
            await _session.ConnectAsync(_connectCts.Token).ConfigureAwait(true);

            // The engine is running, but that alone is not proof that Steam is
            // reachable. Keep the connecting state until the anonymous probe
            // finishes, then expose the single "connected" state required by
            // the product. Probe details stay in the log.
            SetState(
                EngineConnectionState.Connecting,
                "Engine đã khởi động. Đang kiểm tra kết nối...");

            await CheckSteamAsync(_steamCts.Token).ConfigureAwait(true);
            if (_state == EngineConnectionState.Connecting)
            {
                SetState(EngineConnectionState.Connected, BuildConnectedDetail());
            }

            if (_state == EngineConnectionState.Connected)
            {
                _ = RefreshSteamLoopAsync(_steamCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            SetState(EngineConnectionState.Disconnected, "Nhấn KẾT NỐI để bắt đầu.");
        }
        catch (ElevationCancelledException)
        {
            SetState(
                EngineConnectionState.Error,
                "Bạn đã từ chối cấp quyền quản trị. Nhấn KẾT NỐI để thử lại.");
        }
        catch (EngineSessionException exception)
        {
            AppLog.Warn("connect: " + exception.Message);
            SetState(EngineConnectionState.Error, exception.Message);
        }
        catch (Exception exception)
        {
            AppLog.Error("connect", exception);
            SetState(EngineConnectionState.Error, "Không thể kết nối. Vui lòng thử lại.");
        }
    }

    private async Task DisconnectAsync()
    {
        _resetCts?.Cancel();
        _steamCts?.Cancel();
        _steamCts?.Dispose();
        _steamCts = null;
        _steamChecking = false;
        _steamResult = null;
        SetState(EngineConnectionState.Disconnecting, "Đang dừng engine an toàn...");

        try
        {
            await _session.DisconnectAsync().ConfigureAwait(true);
            SetState(EngineConnectionState.DisconnectedJustNow, "Engine đã được dừng.");
            ScheduleReset();
        }
        catch (Exception exception)
        {
            AppLog.Error("disconnect", exception);
            SetState(EngineConnectionState.Error, "Không thể ngắt kết nối. Vui lòng thử lại.");
        }
    }

    private async Task CheckSteamAsync(CancellationToken cancellationToken)
    {
        _steamChecking = true;
        try
        {
            var result = await _steamProbe.RunAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            _steamResult = result;
            AppLog.Info(
                "steam probe: reachable=" + result.Reachable +
                " degraded=" + result.Degraded + " " + result.Detail);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            AppLog.Warn("steam probe: " + exception.Message);
            _steamResult = new SteamProbeResult(Array.Empty<SteamEndpointResult>());
        }
        finally
        {
            _steamChecking = false;
        }

        if (_state == EngineConnectionState.Connected)
        {
            SetState(EngineConnectionState.Connected, BuildConnectedDetail());
        }
    }

    private async Task RefreshSteamLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(SteamRefreshInterval, cancellationToken).ConfigureAwait(true);

                if (_state != EngineConnectionState.Connected || _steamChecking)
                {
                    continue;
                }

                await CheckSteamAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal teardown.
        }
        catch (Exception exception)
        {
            AppLog.Warn("steam refresh: " + exception.Message);
        }
    }

    private string BuildConnectedDetail()
    {
        if (_steamResult is null)
        {
            return "Engine đã chạy nhưng chưa xác minh được Steam. " + EngineDefinition.DnsRedirectSummary;
        }

        return _steamResult.Detail + " " + EngineDefinition.DnsRedirectSummary;
    }

    private void OnSessionFaulted(string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() =>
        {
            if (_state is EngineConnectionState.Connected or EngineConnectionState.Connecting)
            {
                _steamCts?.Cancel();
                _steamChecking = false;
                _steamResult = null;
                SetState(
                    EngineConnectionState.Error,
                    string.IsNullOrWhiteSpace(message) ? "Kết nối đã bị gián đoạn." : message);
            }
        });
    }

    private void ScheduleReset()
    {
        _resetCts?.Cancel();
        _resetCts?.Dispose();
        _resetCts = new CancellationTokenSource();
        _ = ResetLaterAsync(_resetCts.Token);
    }

    private async Task ResetLaterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2.8), cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_state == EngineConnectionState.DisconnectedJustNow)
        {
            SetState(EngineConnectionState.Disconnected, "Nhấn KẾT NỐI để bắt đầu.");
        }
    }

    private void SetState(EngineConnectionState state, string detail)
    {
        if (_state == state && _statusDetail == detail)
        {
            return;
        }

        _state = state;
        _statusDetail = detail;

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusHint));
        OnPropertyChanged(nameof(ShowStatusHint));
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(TrayActionText));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(HasActiveSession));

        _toggleCommand.RaiseCanExecuteChanged();
    }
}
