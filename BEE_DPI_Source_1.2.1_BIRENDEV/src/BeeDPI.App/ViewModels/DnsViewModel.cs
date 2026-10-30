using System.Collections.ObjectModel;
using System.Windows.Input;
using BeeDPI.App.Services;
using BeeDPI.Core.Dns;

namespace BeeDPI.App.ViewModels;

/// <summary>
/// State and actions for the separate DNS Manager window. DNS is deliberately
/// independent of the engine: changing or restoring DNS never starts or stops
/// the network engine.
/// </summary>
public sealed class DnsViewModel : ObservableObject, IAsyncDisposable
{
    private const string PlainDnsNote = "DNS thường (không mã hóa).";
    private const string DohNoteText = "DNS mã hóa bằng DoH gốc của Windows (HTTPS).";

    private static readonly DnsProvider CustomProvider = new()
    {
        Id = DnsCatalog.CustomProviderId,
        Name = "Tùy chỉnh",
        Profiles = Array.Empty<DnsProfile>()
    };

    private readonly IDnsPlatform _platform;
    private readonly DnsCoordinator _coordinator;
    private readonly AsyncRelayCommand _refreshCommand;
    private readonly AsyncRelayCommand _applyCommand;
    private readonly AsyncRelayCommand _restoreCommand;
    private readonly AsyncRelayCommand _overwriteRestoreCommand;
    private readonly RelayCommand _toggleAdvancedCommand;
    private readonly RelayCommand _cancelConflictCommand;
    private readonly object _operationSync = new();

    private Task _currentOperation = Task.CompletedTask;
    private DnsCapabilities? _capabilities;
    private bool _initialized;
    private bool _isBusy;
    private bool _isRefreshing;
    private bool _suppressAdapterReload;
    private bool _showAdapterSelector;
    private bool _isAdvancedOpen;
    private bool _hasBackup;
    private bool _useDoh;
    private bool _isConflictPending;
    private bool _isError;
    private bool _isSuccess;
    private bool _hasStatus;
    private bool _showNetworkWarning;
    private string _statusTitle = string.Empty;
    private string _statusMessage = string.Empty;
    private string _networkType = "Đang xác định...";
    private string _currentDnsSummary = string.Empty;
    private string _backupSummary = string.Empty;
    private string _appliedSummary = string.Empty;
    private string _dohAvailability = "Đang kiểm tra khả năng của Windows...";
    private string _encryptionNote = PlainDnsNote;
    private string _customIpv4 = string.Empty;
    private string _customIpv6 = string.Empty;
    private DnsAdapterInfo? _selectedAdapter;
    private DnsProvider? _selectedProvider;
    private DnsProfile? _selectedProfile;

    public DnsViewModel(IDnsPlatform platform, IDnsBackupStore backups)
    {
        _platform = platform;
        _coordinator = new DnsCoordinator(platform, backups);

        Adapters = new ObservableCollection<DnsAdapterInfo>();
        Providers = new ObservableCollection<DnsProvider>(
            DnsCatalog.Default.Providers.Concat(new[] { CustomProvider }));
        Profiles = new ObservableCollection<DnsProfile>();

        _refreshCommand = new AsyncRelayCommand(
            () => TrackAsync(RefreshAsync()),
            () => !IsBusy && _initialized);
        _applyCommand = new AsyncRelayCommand(
            () => TrackAsync(ApplyAsync()),
            () => CanApply);
        _restoreCommand = new AsyncRelayCommand(
            () => TrackAsync(RestoreAsync(allowOverwrite: false)),
            () => CanRestore);
        _overwriteRestoreCommand = new AsyncRelayCommand(
            () => TrackAsync(RestoreAsync(allowOverwrite: true)),
            () => IsConflictPending && !IsBusy && SelectedAdapter is not null);
        _toggleAdvancedCommand = new RelayCommand(() => IsAdvancedOpen = !IsAdvancedOpen);
        _cancelConflictCommand = new RelayCommand(() =>
        {
            IsConflictPending = false;
            ClearStatus();
        });

        SelectedProvider = Providers.FirstOrDefault();
        UpdateDerivedState();
    }

    public ObservableCollection<DnsAdapterInfo> Adapters { get; }

    public ObservableCollection<DnsProvider> Providers { get; }

    public ObservableCollection<DnsProfile> Profiles { get; }

    public ICommand RefreshCommand => _refreshCommand;

    public ICommand ApplyCommand => _applyCommand;

    public ICommand RestoreCommand => _restoreCommand;

    public ICommand OverwriteRestoreCommand => _overwriteRestoreCommand;

    public ICommand CancelConflictCommand => _cancelConflictCommand;

    public ICommand ToggleAdvancedCommand => _toggleAdvancedCommand;

    public bool IsBusy => _isBusy;

    public bool IsIdle => !_isBusy;

    public bool IsError => _isError;

    public bool IsSuccess => _isSuccess;

    public bool HasStatus => _hasStatus;

    public string StatusTitle => _statusTitle;

    public string StatusMessage => _statusMessage;

    /// <summary>Friendly network type: Wi-Fi or Ethernet.</summary>
    public string NetworkType => _networkType;

    public bool ShowNetworkWarning => _showNetworkWarning;

    public bool ShowAdapterSelector
    {
        get => _showAdapterSelector;
        private set
        {
            if (SetProperty(ref _showAdapterSelector, value))
            {
                OnPropertyChanged(nameof(ShowNetworkKindText));
            }
        }
    }

    public bool ShowNetworkKindText => !ShowAdapterSelector;

    public bool ShowProfileSelector => !IsCustomProvider;

    public bool ShowCustomFields => IsAdvancedOpen && IsCustomProvider;

    public bool ShowDohOption => IsAdvancedOpen && !IsCustomProvider;

    public bool IsAdvancedOpen
    {
        get => _isAdvancedOpen;
        set
        {
            if (!SetProperty(ref _isAdvancedOpen, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ShowCustomFields));
            OnPropertyChanged(nameof(ShowDohOption));
            OnPropertyChanged(nameof(AdvancedChevron));
        }
    }

    public string AdvancedChevron => IsAdvancedOpen ? "\uE70E" : "\uE70D";

    public string CurrentDnsSummary => _currentDnsSummary;

    public string BackupSummary => _backupSummary;

    public string AppliedSummary => _appliedSummary;

    public string DohAvailability => _dohAvailability;

    public string EncryptionNote => _encryptionNote;

    public bool HasBackup
    {
        get => _hasBackup;
        private set
        {
            if (SetProperty(ref _hasBackup, value))
            {
                _restoreCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsConflictPending
    {
        get => _isConflictPending;
        private set
        {
            if (SetProperty(ref _isConflictPending, value))
            {
                UpdateCommands();
            }
        }
    }

    public bool UseDoh
    {
        get => _useDoh;
        set
        {
            if (!SetProperty(ref _useDoh, value))
            {
                return;
            }

            UpdateDerivedState();
        }
    }

    public bool CanUseDoh =>
        !IsBusy
        && !IsCustomProvider
        && SelectedProfile?.SupportsDoh == true
        && _capabilities?.NativeDohSupported == true;

    public bool IsCustomProvider =>
        string.Equals(SelectedProvider?.Id, DnsCatalog.CustomProviderId, StringComparison.OrdinalIgnoreCase);

    public DnsAdapterInfo? SelectedAdapter
    {
        get => _selectedAdapter;
        set
        {
            if (!SetProperty(ref _selectedAdapter, value))
            {
                return;
            }

            _networkType = value?.NetworkKind ?? "Không xác định";
            OnPropertyChanged(nameof(NetworkType));
            UpdateCommands();

            if (!_suppressAdapterReload && value is not null)
            {
                _ = TrackAsync(LoadAdapterStateAsync(setErrorStatus: true));
            }
        }
    }

    public DnsProvider? SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (!SetProperty(ref _selectedProvider, value))
            {
                return;
            }

            ApplyProviderSelection();
        }
    }

    public DnsProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value))
            {
                return;
            }

            UpdateDerivedState();
        }
    }

    public string CustomIpv4
    {
        get => _customIpv4;
        set
        {
            if (SetProperty(ref _customIpv4, value))
            {
                UpdateCommands();
            }
        }
    }

    public string CustomIpv6
    {
        get => _customIpv6;
        set
        {
            if (SetProperty(ref _customIpv6, value))
            {
                UpdateCommands();
            }
        }
    }

    public bool CanApply => !IsBusy && SelectedAdapter is not null && (IsCustomProvider || SelectedProfile is not null);

    public bool CanRestore => !IsBusy && SelectedAdapter is not null && HasBackup;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await TrackAsync(RefreshCoreAsync()).ConfigureAwait(true);
    }

    public async Task RefreshAsync()
    {
        if (_isRefreshing || IsBusy)
        {
            return;
        }

        await TrackAsync(RefreshCoreAsync()).ConfigureAwait(true);
    }

    public async Task RefreshIfIdleAsync()
    {
        if (!_initialized || _isRefreshing || IsBusy)
        {
            return;
        }

        await TrackAsync(RefreshCoreAsync()).ConfigureAwait(true);
    }

    public async Task WaitForIdleAsync()
    {
        Task operation;
        lock (_operationSync)
        {
            operation = _currentOperation;
        }

        try
        {
            await operation.ConfigureAwait(true);
        }
        catch
        {
            // The command already reported the failure.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await WaitForIdleAsync().ConfigureAwait(true);

        if (_platform is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync().ConfigureAwait(true);
        }
    }

    private Task TrackAsync(Task operation)
    {
        lock (_operationSync)
        {
            _currentOperation = operation;
        }

        return operation;
    }

    private async Task RefreshCoreAsync()
    {
        _isRefreshing = true;
        SetBusy(true);

        try
        {
            if (_capabilities is null)
            {
                try
                {
                    _capabilities = await _platform.GetCapabilitiesAsync().ConfigureAwait(true);
                }
                catch (Exception exception)
                {
                    AppLog.Warn("dns capabilities: " + exception.Message);
                    _capabilities = new DnsCapabilities(false, 0, "Không kiểm tra được khả năng DoH của Windows.");
                }
            }

            var adapters = await _coordinator.GetUsableAdaptersAsync().ConfigureAwait(true);
            var active = DnsAdapterSelector.FindActive(adapters, out var ambiguous);
            var previousGuid = SelectedAdapter?.InterfaceGuid;

            _suppressAdapterReload = true;
            try
            {
                Adapters.Clear();
                foreach (var adapter in adapters)
                {
                    Adapters.Add(adapter);
                }

                var previous = adapters.FirstOrDefault(
                    a => DnsGuids.Equal(a.InterfaceGuid, previousGuid));

                SelectedAdapter = ambiguous && previous is not null
                    ? previous
                    : active ?? previous ?? adapters.FirstOrDefault();
            }
            finally
            {
                _suppressAdapterReload = false;
            }

            ShowAdapterSelector = ambiguous && Adapters.Count > 1;
            _showNetworkWarning = adapters.Count > 0 && adapters.All(a => !a.HasDefaultRoute);
            OnPropertyChanged(nameof(ShowNetworkWarning));

            if (SelectedAdapter is null)
            {
                _currentDnsSummary = string.Empty;
                _backupSummary = string.Empty;
                HasBackup = false;
                _appliedSummary = string.Empty;
                ClearStatus();
                SetStatus("KHÔNG THỂ XÁC ĐỊNH MẠNG", "Không tìm thấy mạng vật lý đang hoạt động.", error: true);
                NotifyStateSummaries();
                return;
            }

            await LoadAdapterStateCoreAsync().ConfigureAwait(true);
            ClearStatus();
        }
        catch (DnsOperationException exception)
        {
            AppLog.Warn("dns refresh: " + exception.Code + " " + exception.Message);
            SetStatus("KHÔNG THỂ XÁC ĐỊNH MẠNG", "Không đọc được cấu hình mạng hiện tại.", error: true);
        }
        catch (Exception exception)
        {
            AppLog.Error("dns refresh", exception);
            SetStatus("KHÔNG THỂ XÁC ĐỊNH MẠNG", "Không đọc được cấu hình mạng hiện tại.", error: true);
        }
        finally
        {
            _isRefreshing = false;
            SetBusy(false);
        }
    }

    private async Task LoadAdapterStateAsync(bool setErrorStatus)
    {
        try
        {
            await LoadAdapterStateCoreAsync().ConfigureAwait(true);
        }
        catch (DnsOperationException exception)
        {
            AppLog.Warn("dns adapter state: " + exception.Code + " " + exception.Message);
            if (setErrorStatus)
            {
                SetStatus("KHÔNG THỂ XÁC ĐỊNH MẠNG", "Không đọc được cấu hình DNS hiện tại.", error: true);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("dns adapter state", exception);
            if (setErrorStatus)
            {
                SetStatus("KHÔNG THỂ XÁC ĐỊNH MẠNG", "Không đọc được cấu hình DNS hiện tại.", error: true);
            }
        }
    }

    private async Task LoadAdapterStateCoreAsync()
    {
        var adapter = SelectedAdapter;
        if (adapter is null)
        {
            return;
        }

        var snapshot = await _coordinator.GetSnapshotAsync(adapter).ConfigureAwait(true);
        _currentDnsSummary = "IPv4: " + snapshot.Ipv4.Display + "\nIPv6: " + snapshot.Ipv6.Display;

        var backup = await _coordinator.LoadBackupAsync(adapter.InterfaceGuid).ConfigureAwait(true);
        HasBackup = backup.Status == DnsBackupStatus.Valid;
        _appliedSummary = backup.Status switch
        {
            DnsBackupStatus.Valid when !string.IsNullOrWhiteSpace(backup.Document!.AppliedProfileName) =>
                "BEE DPI đang quản lý: " + backup.Document.AppliedProfileName + ".",
            DnsBackupStatus.Valid => "Đã có bản sao lưu gốc.",
            DnsBackupStatus.Corrupt => "Bản sao lưu bị hỏng.",
            _ => string.Empty
        };

        _backupSummary = backup.Status switch
        {
            DnsBackupStatus.Valid => "Đã có bản sao lưu gốc cho mạng này.",
            DnsBackupStatus.Corrupt => "Bản sao lưu DNS bị hỏng.",
            _ => "Chưa có bản sao lưu cho mạng này."
        };

        if (backup.Status == DnsBackupStatus.Valid
            && !string.IsNullOrWhiteSpace(backup.Document!.AppliedProfileName))
        {
            ApplyStoredSelection(backup.Document.AppliedProfileId);
        }

        NotifyStateSummaries();
        UpdateCommands();
    }

    private void ApplyStoredSelection(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        var profile = DnsCatalog.Default.FindProfile(profileId);
        if (profile is null)
        {
            return;
        }

        var provider = Providers.FirstOrDefault(p =>
            string.Equals(p.Id, profile.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            return;
        }

        SelectedProvider = provider;
        SelectedProfile = Profiles.FirstOrDefault(p =>
            string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
    }

    private Task ApplyAsync()
    {
        var adapter = SelectedAdapter;
        if (adapter is null || IsBusy)
        {
            return Task.CompletedTask;
        }

        DnsProfile profile;
        if (IsCustomProvider)
        {
            var validation = DnsValidation.Validate(SplitAddresses(CustomIpv4), SplitAddresses(CustomIpv6));
            if (!validation.IsValid)
            {
                SetStatus(
                    "KHÔNG THỂ ÁP DỤNG DNS",
                    "Địa chỉ DNS tùy chỉnh không hợp lệ.",
                    error: true);
                return Task.CompletedTask;
            }

            profile = new DnsProfile
            {
                Id = DnsCatalog.CustomProfileId,
                ProviderId = DnsCatalog.CustomProviderId,
                ProviderName = "Tùy chỉnh",
                Name = "DNS tùy chỉnh",
                Description = "Địa chỉ DNS do bạn nhập.",
                Filtering = "Không do BEE DPI lọc.",
                Ipv4 = validation.Ipv4,
                Ipv6 = validation.Ipv6,
                DohTemplate = null,
                Protocols = new[] { DnsProtocol.Udp, DnsProtocol.Tcp }
            };
        }
        else
        {
            profile = SelectedProfile!;
            if (profile is null)
            {
                return Task.CompletedTask;
            }
        }

        var useDoh = UseDoh && profile.SupportsDoh;
        if (useDoh && _capabilities?.NativeDohSupported != true)
        {
            SetStatus(
                "KHÔNG THỂ ÁP DỤNG DNS",
                "Windows này không hỗ trợ DNS mã hóa. Hãy tắt tùy chọn này.",
                error: true);
            return Task.CompletedTask;
        }

        return TrackAsync(ApplyCoreAsync(adapter, profile, useDoh));
    }

    private async Task ApplyCoreAsync(DnsAdapterInfo adapter, DnsProfile profile, bool useDoh)
    {
        SetBusy(true);
        IsConflictPending = false;
        ClearStatus();

        try
        {
            adapter = await RevalidateAdapterAsync(adapter).ConfigureAwait(true);
            var result = await _coordinator.ApplyAsync(adapter, profile, useDoh).ConfigureAwait(true);

            AppLog.Info(
                "dns apply: " + result.Code + " " + result.Message + " " +
                result.VerificationDetail + " " + result.RollbackDetail);

            SetStatus("ĐÃ ĐỔI DNS", string.Empty, success: true);
        }
        catch (ElevationCancelledException)
        {
            SetStatus("KHÔNG CÓ QUYỀN QUẢN TRỊ", "Bạn đã từ chối cấp quyền quản trị.", error: true);
        }
        catch (DnsConfigurationChangedException exception)
        {
            AppLog.Warn("dns apply config changed: " + exception.Message);
            SetStatus(
                "DNS ĐÃ THAY ĐỔI",
                "Cấu hình đã thay đổi bên ngoài BEE DPI. Vui lòng thử lại.",
                error: true);
        }
        catch (DnsOperationException exception)
        {
            AppLog.Warn("dns apply: " + exception.Code + " " + exception.Message + " " + exception.TechnicalDetail);
            var (title, message) = MapApplyFailure(exception);
            SetStatus(title, message, error: true);
        }
        catch (EngineSessionException exception)
        {
            AppLog.Warn("dns apply host: " + exception.Message);
            SetStatus("KHÔNG THỂ ÁP DỤNG DNS", "Không thể hoàn tất. Vui lòng thử lại.", error: true);
        }
        catch (Exception exception)
        {
            AppLog.Error("dns apply", exception);
            SetStatus("KHÔNG THỂ ÁP DỤNG DNS", "Đã xảy ra lỗi. Vui lòng thử lại.", error: true);
        }
        finally
        {
            try
            {
                await RefreshAdapterAfterOperationAsync(adapter).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                AppLog.Warn("dns apply refresh: " + exception.Message);
            }

            SetBusy(false);
        }
    }

    private Task RestoreAsync(bool allowOverwrite)
    {
        var adapter = SelectedAdapter;
        if (adapter is null || IsBusy)
        {
            return Task.CompletedTask;
        }

        return TrackAsync(RestoreCoreAsync(adapter, allowOverwrite));
    }

    private async Task RestoreCoreAsync(DnsAdapterInfo adapter, bool allowOverwrite)
    {
        SetBusy(true);
        ClearStatus();

        try
        {
            adapter = await RevalidateAdapterAsync(adapter).ConfigureAwait(true);
            var result = await _coordinator.RestoreAsync(adapter, allowOverwrite).ConfigureAwait(true);
            IsConflictPending = false;

            AppLog.Info(
                "dns restore: " + result.Code + " " + result.Message + " " +
                result.VerificationDetail + " " + result.RollbackDetail);

            SetStatus("ĐÃ KHÔI PHỤC DNS", string.Empty, success: true);
        }
        catch (ElevationCancelledException)
        {
            SetStatus("KHÔNG CÓ QUYỀN QUẢN TRỊ", "Bạn đã từ chối cấp quyền quản trị.", error: true);
        }
        catch (DnsConfigurationChangedException exception)
        {
            AppLog.Warn("dns restore config changed: " + exception.Message);
            IsConflictPending = true;
            SetStatus(
                "DNS ĐÃ THAY ĐỔI",
                "Bản sao lưu gốc vẫn an toàn. Bạn có muốn khôi phục bản gốc?",
                error: true);
        }
        catch (DnsOperationException exception)
        {
            AppLog.Warn("dns restore: " + exception.Code + " " + exception.Message + " " + exception.TechnicalDetail);
            var (title, message) = MapRestoreFailure(exception);
            SetStatus(title, message, error: true);
        }
        catch (EngineSessionException exception)
        {
            AppLog.Warn("dns restore host: " + exception.Message);
            SetStatus("KHÔNG THỂ KHÔI PHỤC DNS", "Không thể hoàn tất. Vui lòng thử lại.", error: true);
        }
        catch (Exception exception)
        {
            AppLog.Error("dns restore", exception);
            SetStatus("KHÔNG THỂ KHÔI PHỤC DNS", "Đã xảy ra lỗi. Vui lòng thử lại.", error: true);
        }
        finally
        {
            try
            {
                await RefreshAdapterAfterOperationAsync(adapter).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                AppLog.Warn("dns restore refresh: " + exception.Message);
            }

            SetBusy(false);
        }
    }

    /// <summary>
    /// Re-scans the live adapter list before a mutation. A stale identifier is
    /// never sent to Windows: if the adapter disappeared or a different network
    /// now owns the default route, the user is asked to refresh.
    /// </summary>
    private async Task<DnsAdapterInfo> RevalidateAdapterAsync(DnsAdapterInfo requested)
    {
        var adapters = await _coordinator.GetUsableAdaptersAsync().ConfigureAwait(true);
        var match = DnsAdapterSelector.FindLiveMatch(requested, adapters);
        if (match is null)
        {
            throw new DnsOperationException(
                "ADAPTER_STALE",
                "Mạng đã thay đổi. Vui lòng thử lại.");
        }

        // Keep the visible selector in sync with the adapter that really exists.
        _suppressAdapterReload = true;
        try
        {
            var index = Adapters.ToList().FindIndex(a => DnsGuids.Equal(a.InterfaceGuid, match.InterfaceGuid));
            if (index >= 0)
            {
                Adapters[index] = match;
            }

            if (SelectedAdapter is not null
                && DnsGuids.Equal(SelectedAdapter.InterfaceGuid, match.InterfaceGuid))
            {
                SelectedAdapter = match;
            }
        }
        finally
        {
            _suppressAdapterReload = false;
        }

        return match;
    }

    private async Task RefreshAdapterAfterOperationAsync(DnsAdapterInfo adapter)
    {
        var fresh = await _coordinator.GetUsableAdaptersAsync().ConfigureAwait(true);
        var match = fresh.FirstOrDefault(a => DnsGuids.Equal(a.InterfaceGuid, adapter.InterfaceGuid));
        if (match is not null)
        {
            _suppressAdapterReload = true;
            try
            {
                var index = Adapters.ToList().FindIndex(a => DnsGuids.Equal(a.InterfaceGuid, match.InterfaceGuid));
                if (index >= 0)
                {
                    Adapters[index] = match;
                }

                SelectedAdapter = match;
            }
            finally
            {
                _suppressAdapterReload = false;
            }
        }

        await LoadAdapterStateCoreAsync().ConfigureAwait(true);
    }

    private static (string Title, string Message) MapApplyFailure(DnsOperationException exception) =>
        exception.Code switch
        {
            "ADAPTER_MISMATCH" or "ADAPTER_STALE" or "NO_ADAPTER" =>
                ("KHÔNG THỂ XÁC ĐỊNH MẠNG", "Mạng đã thay đổi. Vui lòng thử lại."),
            "NOT_ELEVATED" or "ELEVATION_FAILED" =>
                ("KHÔNG CÓ QUYỀN QUẢN TRỊ", "Không thể yêu cầu quyền quản trị."),
            "CONFIG_CHANGED" =>
                ("DNS ĐÃ THAY ĐỔI", "Cấu hình đã thay đổi bên ngoài BEE DPI. Vui lòng thử lại."),
            "BACKUP_CORRUPT" =>
                ("KHÔNG THỂ ÁP DỤNG DNS", "Bản sao lưu DNS bị hỏng."),
            "INVALID_ADDRESS" or "DOH_UNSUPPORTED_PROFILE" or "DOH_UNSUPPORTED" or "DOH_INVALID" =>
                ("KHÔNG THỂ ÁP DỤNG DNS", "Thông tin DNS chưa hợp lệ."),
            "DNS_TIMEOUT" or "DNS_SCRIPT_TIMEOUT" =>
                ("KHÔNG THỂ ÁP DỤNG DNS", "Quá thời gian xử lý. Vui lòng thử lại."),
            "DNS_HOST_EXITED" or "DNS_HOST_START_FAILED" or "DNS_SCRIPT_FAILED" or "DNS_SCRIPT_MISSING" =>
                ("KHÔNG THỂ ÁP DỤNG DNS", "Không thể khởi động thành phần DNS."),
            _ => LooksLikeVerificationFailure(exception)
                ? ("KHÔNG THỂ XÁC MINH DNS", "DNS chưa hoạt động. Đã cố khôi phục cấu hình cũ.")
                : ("KHÔNG THỂ ÁP DỤNG DNS", "Vui lòng thử lại.")
        };

    private static (string Title, string Message) MapRestoreFailure(DnsOperationException exception) =>
        exception.Code switch
        {
            "ADAPTER_MISMATCH" or "ADAPTER_STALE" or "NO_ADAPTER" =>
                ("KHÔNG THỂ XÁC ĐỊNH MẠNG", "Mạng đã thay đổi. Vui lòng thử lại."),
            "NOT_ELEVATED" or "ELEVATION_FAILED" =>
                ("KHÔNG CÓ QUYỀN QUẢN TRỊ", "Không thể yêu cầu quyền quản trị."),
            "NO_BACKUP" =>
                ("KHÔNG THỂ KHÔI PHỤC DNS", "Chưa có bản sao lưu cho mạng này."),
            "BACKUP_CORRUPT" =>
                ("KHÔNG THỂ KHÔI PHỤC DNS", "Bản sao lưu DNS bị hỏng."),
            "CONFIG_CHANGED" =>
                ("DNS ĐÃ THAY ĐỔI", "Cấu hình đã thay đổi bên ngoài BEE DPI."),
            "DNS_TIMEOUT" or "DNS_SCRIPT_TIMEOUT" =>
                ("KHÔNG THỂ KHÔI PHỤC DNS", "Quá thời gian xử lý. Vui lòng thử lại."),
            _ => ("KHÔNG THỂ KHÔI PHỤC DNS", "Vui lòng thử lại.")
        };

    private static bool LooksLikeVerificationFailure(DnsOperationException exception)
    {
        var text = (exception.Message + " " + exception.TechnicalDetail).ToLowerInvariant();
        return text.Contains("xác minh", StringComparison.Ordinal)
               || text.Contains("verify", StringComparison.Ordinal)
               || text.Contains("không phân giải", StringComparison.Ordinal)
               || text.Contains("resolution", StringComparison.Ordinal);
    }

    private void ApplyProviderSelection()
    {
        Profiles.Clear();

        if (SelectedProvider is null || IsCustomProvider)
        {
            SelectedProfile = null;
        }
        else
        {
            foreach (var profile in SelectedProvider.Profiles)
            {
                Profiles.Add(profile);
            }

            SelectedProfile = Profiles.FirstOrDefault();
        }

        if (!CanUseDoh)
        {
            UseDoh = false;
        }

        // Custom DNS only lives in the advanced section, so reveal it instead of
        // showing an empty profile selector.
        if (IsCustomProvider && !IsAdvancedOpen)
        {
            IsAdvancedOpen = true;
        }

        UpdateDerivedState();
    }

    private void UpdateDerivedState()
    {
        if (IsCustomProvider)
        {
            _dohAvailability = "DNS tùy chỉnh chỉ dùng DNS thường (không mã hóa).";
        }
        else if (SelectedProfile is null)
        {
            _dohAvailability = "Chọn một chế độ DNS.";
        }
        else if (!SelectedProfile.SupportsDoh)
        {
            _dohAvailability = "Chế độ này không hỗ trợ DNS mã hóa.";
        }
        else if (_capabilities is null)
        {
            _dohAvailability = "Đang kiểm tra khả năng của Windows...";
        }
        else if (_capabilities.NativeDohSupported)
        {
            _dohAvailability = "Windows hỗ trợ DNS mã hóa.";
        }
        else
        {
            _dohAvailability = "Windows này không hỗ trợ DNS mã hóa.";
        }

        if (!CanUseDoh)
        {
            _useDoh = false;
            OnPropertyChanged(nameof(UseDoh));
        }

        _encryptionNote = UseDoh && CanUseDoh ? DohNoteText : PlainDnsNote;

        OnPropertyChanged(nameof(DohAvailability));
        OnPropertyChanged(nameof(EncryptionNote));
        OnPropertyChanged(nameof(CanUseDoh));
        OnPropertyChanged(nameof(IsCustomProvider));
        OnPropertyChanged(nameof(ShowProfileSelector));
        OnPropertyChanged(nameof(ShowCustomFields));
        OnPropertyChanged(nameof(ShowDohOption));
        UpdateCommands();
    }

    private void SetBusy(bool value)
    {
        if (_isBusy == value)
        {
            return;
        }

        _isBusy = value;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanUseDoh));
        UpdateDerivedState();
    }

    private void SetStatus(string title, string message, bool error = false, bool success = false)
    {
        _statusTitle = title;
        _statusMessage = message;
        _isError = error;
        _isSuccess = success;
        _hasStatus = !string.IsNullOrWhiteSpace(title);

        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsSuccess));
        OnPropertyChanged(nameof(HasStatus));
    }

    private void ClearStatus()
    {
        if (!_hasStatus)
        {
            return;
        }

        _hasStatus = false;
        _isError = false;
        _isSuccess = false;
        _statusTitle = string.Empty;
        _statusMessage = string.Empty;

        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsSuccess));
        OnPropertyChanged(nameof(HasStatus));
    }

    private void NotifyStateSummaries()
    {
        OnPropertyChanged(nameof(CurrentDnsSummary));
        OnPropertyChanged(nameof(BackupSummary));
        OnPropertyChanged(nameof(AppliedSummary));
    }

    private void UpdateCommands()
    {
        _refreshCommand.RaiseCanExecuteChanged();
        _applyCommand.RaiseCanExecuteChanged();
        _restoreCommand.RaiseCanExecuteChanged();
        _overwriteRestoreCommand.RaiseCanExecuteChanged();

        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanRestore));
    }

    private static IReadOnlyList<string> SplitAddresses(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(
                new[] { ',', ';', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal static class DnsGuids
{
    public static bool Equal(string? left, string? right) =>
        string.Equals(
            FileDnsBackupStore.NormalizeGuid(left),
            FileDnsBackupStore.NormalizeGuid(right),
            StringComparison.OrdinalIgnoreCase);
}
