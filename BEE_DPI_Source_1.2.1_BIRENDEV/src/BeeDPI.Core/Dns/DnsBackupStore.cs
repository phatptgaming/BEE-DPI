using System.Text;
using System.Text.Json;

namespace BeeDPI.Core.Dns;

/// <summary>
/// Persistent record of the DNS configuration that existed before BEE DPI first
/// changed an adapter. The original baseline is never overwritten when the user
/// switches profiles.
/// </summary>
public sealed class DnsBackupDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string AdapterGuid { get; set; } = string.Empty;

    public uint InterfaceIndex { get; set; }

    public string AdapterAlias { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DnsAdapterSnapshot Original { get; set; } = default!;

    public DnsAdapterSnapshot? LastApplied { get; set; }

    public DateTimeOffset? LastAppliedAt { get; set; }

    public string? AppliedProfileId { get; set; }

    public string? AppliedProfileName { get; set; }

    public List<DnsDohChange> DohChanges { get; set; } = new();

    public DateTimeOffset? RestoredAt { get; set; }
}

public enum DnsBackupStatus
{
    Missing = 0,
    Valid = 1,
    Corrupt = 2
}

public sealed record DnsBackupLoadResult(
    DnsBackupStatus Status,
    DnsBackupDocument? Document,
    string? Error)
{
    public static DnsBackupLoadResult Missing() => new(DnsBackupStatus.Missing, null, null);

    public static DnsBackupLoadResult Valid(DnsBackupDocument document) =>
        new(DnsBackupStatus.Valid, document, null);

    public static DnsBackupLoadResult Corrupt(string error) => new(DnsBackupStatus.Corrupt, null, error);
}

public interface IDnsBackupStore
{
    Task<DnsBackupLoadResult> LoadAsync(string adapterGuid, CancellationToken cancellationToken = default);

    Task SaveAsync(DnsBackupDocument document, CancellationToken cancellationToken = default);

    Task DeleteAsync(string adapterGuid, CancellationToken cancellationToken = default);
}

/// <summary>
/// File-backed backup store using atomic replace. Backup files live under
/// <c>%LOCALAPPDATA%\BeeDPI\dns</c> and are validated on every read.
/// </summary>
public sealed class FileDnsBackupStore : IDnsBackupStore
{
    private readonly string _root;

    public FileDnsBackupStore(string? rootDirectory = null)
    {
        _root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BeeDPI",
            "dns");
    }

    public string RootDirectory => _root;

    public async Task<DnsBackupLoadResult> LoadAsync(string adapterGuid, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeGuid(adapterGuid);
        if (normalized is null)
        {
            return DnsBackupLoadResult.Corrupt("GUID adapter không hợp lệ.");
        }

        var path = GetPath(normalized);
        if (!File.Exists(path))
        {
            return DnsBackupLoadResult.Missing();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<DnsBackupDocument>(json, DnsJson.Options);
            var error = Validate(document, normalized);
            return error is null
                ? DnsBackupLoadResult.Valid(document!)
                : DnsBackupLoadResult.Corrupt(error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return DnsBackupLoadResult.Corrupt(exception.Message);
        }
    }

    public async Task SaveAsync(DnsBackupDocument document, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeGuid(document.AdapterGuid)
                         ?? throw new ArgumentException("GUID adapter không hợp lệ.", nameof(document));

        var error = Validate(document, normalized);
        if (error is not null)
        {
            throw new InvalidDataException("Bản sao lưu DNS không hợp lệ: " + error);
        }

        Directory.CreateDirectory(_root);
        var path = GetPath(normalized);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            var json = JsonSerializer.Serialize(document, DnsJson.Options);
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    public Task DeleteAsync(string adapterGuid, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeGuid(adapterGuid);
        if (normalized is not null)
        {
            var path = GetPath(normalized);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }

    private string GetPath(string normalizedGuid) =>
        Path.Combine(_root, "backup-" + normalizedGuid + ".json");

    public static string? NormalizeGuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Guid.TryParse(value.Trim(), out var guid) ? guid.ToString("N") : null;
    }

    private static string? Validate(DnsBackupDocument? document, string expectedGuid)
    {
        if (document is null)
        {
            return "Tệp sao lưu rỗng hoặc không đọc được.";
        }

        if (document.SchemaVersion != DnsBackupDocument.CurrentSchemaVersion)
        {
            return "Phiên bản bản sao lưu không được hỗ trợ.";
        }

        var documentGuid = NormalizeGuid(document.AdapterGuid);
        if (documentGuid is null || !string.Equals(documentGuid, expectedGuid, StringComparison.OrdinalIgnoreCase))
        {
            return "Bản sao lưu thuộc về adapter khác.";
        }

        if (document.Original is null)
        {
            return "Bản sao lưu thiếu cấu hình gốc.";
        }

        if (!string.Equals(
                NormalizeGuid(document.Original.InterfaceGuid),
                expectedGuid,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Cấu hình gốc không khớp adapter.";
        }

        var originalError = ValidateSnapshot(document.Original);
        if (originalError is not null)
        {
            return originalError;
        }

        if (document.LastApplied is not null)
        {
            var appliedError = ValidateSnapshot(document.LastApplied);
            if (appliedError is not null)
            {
                return appliedError;
            }
        }

        if (document.DohChanges is null)
        {
            return "Bản sao lưu thiếu thông tin DoH.";
        }

        foreach (var change in document.DohChanges)
        {
            if (!DnsValidation.TryNormalizeAddress(change.ServerAddress, out _, out _))
            {
                return "Bản sao lưu chứa địa chỉ DoH không hợp lệ.";
            }
        }

        return null;
    }

    private static string? ValidateSnapshot(DnsAdapterSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Fingerprint))
        {
            return "Cấu hình DNS thiếu dấu vân tay.";
        }

        foreach (var family in new[] { snapshot.Ipv4, snapshot.Ipv6 })
        {
            if (family is null)
            {
                return "Cấu hình DNS thiếu họ địa chỉ.";
            }

            foreach (var address in family.ServerAddresses)
            {
                // A static value must be a usable unicast resolver. DHCP values
                // are informational and may include link-local addresses.
                var valid = family.IsStatic
                    ? DnsValidation.TryNormalizeAddress(address, out _, out _)
                    : DnsValidation.TryParseLiteral(address, out _);

                if (!valid)
                {
                    return "Cấu hình DNS chứa địa chỉ không hợp lệ.";
                }
            }

            if (family.IsStatic && family.ServerAddresses.Count == 0)
            {
                return "Cấu hình DNS tĩnh nhưng không có địa chỉ.";
            }
        }

        return null;
    }
}
