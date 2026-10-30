namespace BeeDPI.Core.Dns;

/// <summary>
/// Orchestrates safe DNS changes: validation, original backup, conflict
/// detection, apply, verification and restore. The platform performs the actual
/// Windows mutation and returns verification/rollback details.
/// </summary>
public sealed class DnsCoordinator
{
    private static readonly string[] ExcludedAdapterMarkers =
    {
        "hyper-v", "vethernet", "vmware", "virtualbox", "virtual box", "wsl",
        "loopback", "windivert", "bluetooth", "tap-", "tap adapter", "tun",
        "wireguard", "tailscale", "zerotier", "vpn", "pseudo", "teredo",
        "isatap", "6to4", "docker", "npcap"
    };

    private readonly IDnsPlatform _platform;
    private readonly IDnsBackupStore _backups;

    public DnsCoordinator(IDnsPlatform platform, IDnsBackupStore backups)
    {
        _platform = platform;
        _backups = backups;
    }

    public async Task<IReadOnlyList<DnsAdapterInfo>> GetUsableAdaptersAsync(
        CancellationToken cancellationToken = default)
    {
        var adapters = await _platform.ListAdaptersAsync(cancellationToken).ConfigureAwait(false);

        return adapters
            .Where(IsUsable)
            .OrderByDescending(a => a.HasDefaultRoute)
            .ThenBy(a => a.RouteCost > 0 ? a.RouteCost : a.RouteMetric)
            .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<DnsAdapterSnapshot> GetSnapshotAsync(
        DnsAdapterInfo adapter,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _platform.GetSnapshotAsync(adapter.InterfaceIndex, cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(
                FileDnsBackupStore.NormalizeGuid(snapshot.InterfaceGuid),
                FileDnsBackupStore.NormalizeGuid(adapter.InterfaceGuid),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DnsOperationException(
                "ADAPTER_MISMATCH",
                "Đã xảy ra nhầm lẫn adapter. Vui lòng thử lại.");
        }

        return snapshot;
    }

    public Task<DnsBackupLoadResult> LoadBackupAsync(
        string adapterGuid,
        CancellationToken cancellationToken = default) =>
        _backups.LoadAsync(adapterGuid, cancellationToken);

    public async Task<DnsApplyResult> ApplyAsync(
        DnsAdapterInfo adapter,
        DnsProfile profile,
        bool useDoh,
        CancellationToken cancellationToken = default)
    {
        var validation = DnsValidation.Validate(profile.Ipv4, profile.Ipv6);
        if (!validation.IsValid)
        {
            throw new DnsOperationException("INVALID_ADDRESS", validation.Error ?? "Địa chỉ DNS không hợp lệ.");
        }

        if (useDoh && !profile.SupportsDoh)
        {
            throw new DnsOperationException(
                "DOH_UNSUPPORTED_PROFILE",
                "Hồ sơ DNS này không hỗ trợ DNS mã hóa (DoH).");
        }

        var current = await GetSnapshotAsync(adapter, cancellationToken).ConfigureAwait(false);

        var load = await _backups.LoadAsync(adapter.InterfaceGuid, cancellationToken).ConfigureAwait(false);
        if (load.Status == DnsBackupStatus.Corrupt)
        {
            throw new DnsOperationException(
                "BACKUP_CORRUPT",
                "Bản sao lưu DNS bị hỏng. Không thể thay đổi DNS an toàn.",
                load.Error);
        }

        DnsBackupDocument backup;
        if (load.Status == DnsBackupStatus.Missing)
        {
            backup = new DnsBackupDocument
            {
                AdapterGuid = adapter.InterfaceGuid,
                InterfaceIndex = adapter.InterfaceIndex,
                AdapterAlias = adapter.DisplayName,
                CreatedAt = DateTimeOffset.Now,
                Original = current,
                DohChanges = new List<DnsDohChange>()
            };

            await _backups.SaveAsync(backup, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            backup = load.Document!;
        }

        var request = new DnsApplyRequest(
            adapter.InterfaceGuid,
            adapter.InterfaceIndex,
            current.Fingerprint,
            validation.Ipv4,
            validation.Ipv6,
            useDoh,
            useDoh ? profile.DohTemplate : null,
            useDoh ? validation.Ipv4.Concat(validation.Ipv6).ToList() : Array.Empty<string>(),
            backup.DohChanges);

        var result = await _platform.ApplyAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new DnsOperationException(
                result.Code,
                result.Message,
                CombineDetails(result.VerificationDetail, result.RollbackDetail));
        }

        if (result.Snapshot is null)
        {
            throw new DnsOperationException(
                "APPLY_UNCONFIRMED",
                "Không thể xác minh cấu hình DNS sau khi áp dụng.");
        }

        backup.LastApplied = result.Snapshot;
        backup.LastAppliedAt = DateTimeOffset.Now;
        backup.AppliedProfileId = profile.Id;
        backup.AppliedProfileName = profile.DisplayName;
        backup.DohChanges = result.DohChanges.ToList();
        backup.RestoredAt = null;

        try
        {
            await _backups.SaveAsync(backup, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return result with
            {
                Message = result.Message + " Không lưu được trạng thái DNS mới, nhưng bản gốc vẫn an toàn."
            };
        }

        return result;
    }

    public async Task<DnsRestoreResult> RestoreAsync(
        DnsAdapterInfo adapter,
        bool allowOverwriteExternalChanges,
        CancellationToken cancellationToken = default)
    {
        var load = await _backups.LoadAsync(adapter.InterfaceGuid, cancellationToken).ConfigureAwait(false);
        if (load.Status == DnsBackupStatus.Corrupt)
        {
            throw new DnsOperationException(
                "BACKUP_CORRUPT",
                "Bản sao lưu DNS bị hỏng. Không thể khôi phục.",
                load.Error);
        }

        if (load.Status == DnsBackupStatus.Missing)
        {
            throw new DnsOperationException(
                "NO_BACKUP",
                "Chưa có bản sao lưu DNS cho mạng này.");
        }

        var backup = load.Document!;
        var current = await GetSnapshotAsync(adapter, cancellationToken).ConfigureAwait(false);

        if (!allowOverwriteExternalChanges
            && backup.LastApplied is not null
            && !current.Matches(backup.LastApplied)
            && !current.Matches(backup.Original))
        {
            throw new DnsConfigurationChangedException(current);
        }

        var request = new DnsRestoreRequest(
            adapter.InterfaceGuid,
            adapter.InterfaceIndex,
            current.Fingerprint,
            backup.Original.Ipv4,
            backup.Original.Ipv6,
            backup.DohChanges);

        var result = await _platform.RestoreAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new DnsOperationException(
                result.Code,
                result.Message,
                CombineDetails(result.VerificationDetail, result.RollbackDetail));
        }

        backup.RestoredAt = DateTimeOffset.Now;
        backup.LastApplied = null;
        backup.LastAppliedAt = null;
        backup.AppliedProfileId = null;
        backup.AppliedProfileName = null;
        backup.DohChanges = new List<DnsDohChange>();

        try
        {
            await _backups.SaveAsync(backup, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return result with
            {
                Message = result.Message + " Không lưu được trạng thái khôi phục, nhưng DNS đã được khôi phục."
            };
        }

        return result;
    }

    public static bool IsUsable(DnsAdapterInfo adapter)
    {
        if (!adapter.IsUp || !adapter.IsPhysical)
        {
            return false;
        }

        var text = (adapter.Alias + " " + adapter.Description).ToLowerInvariant();
        return !ExcludedAdapterMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    public static string DescribeFingerprintMismatch() =>
        "Cấu hình DNS của mạng đã thay đổi bên ngoài BEE DPI. Vui lòng thử lại.";

    private static string? CombineDetails(params string?[] details)
    {
        var parts = details.Where(d => !string.IsNullOrWhiteSpace(d)).ToArray();
        return parts.Length == 0 ? null : string.Join(" | ", parts);
    }
}
