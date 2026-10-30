using System.Text.Json;
using BeeDPI.Core.Dns;

namespace BeeDPI.Tests;

internal sealed class FakePlatform : IDnsPlatform
{
    public List<DnsAdapterInfo> Adapters { get; } = new();

    public DnsAdapterSnapshot Current { get; set; } = TestData.Snapshot();

    public List<DnsApplyRequest> ApplyCalls { get; } = new();

    public List<DnsRestoreRequest> RestoreCalls { get; } = new();

    public Func<DnsApplyRequest, IDnsBackupStore, Task<DnsApplyResult>>? ApplyHandler { get; set; }

    public Func<DnsRestoreRequest, Task<DnsRestoreResult>>? RestoreHandler { get; set; }

    public IDnsBackupStore? BackupStore { get; set; }

    public Task<DnsCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new DnsCapabilities(true, 26300, "Windows hỗ trợ DoH gốc."));

    public Task<IReadOnlyList<DnsAdapterInfo>> ListAdaptersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DnsAdapterInfo>>(Adapters);

    public Task<DnsAdapterSnapshot> GetSnapshotAsync(
        uint interfaceIndex,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Current);

    public Task<DnsApplyResult> ApplyAsync(
        DnsApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ApplyCalls.Add(request);

        if (ApplyHandler is not null)
        {
            return ApplyHandler(request, BackupStore ?? new FakeBackupStore());
        }

        return Task.FromResult(BuildApplySuccess(request));
    }

    public DnsApplyResult BuildApplySuccess(DnsApplyRequest request)
    {
        var ipv4 = request.Ipv4.Count > 0 ? DnsFamilyState.Static(request.Ipv4) : Current.Ipv4;
        var ipv6 = request.Ipv6.Count > 0 ? DnsFamilyState.Static(request.Ipv6) : Current.Ipv6;
        var snapshot = TestData.Snapshot(request.AdapterGuid, request.InterfaceIndex, ipv4, ipv6);
        Current = snapshot;

        var changes = request.UseDoh
            ? request.DohAddresses
                .Select(address => new DnsDohChange(
                    address,
                    Existed: false,
                    DohTemplate: null,
                    AutoUpgrade: false,
                    AllowFallbackToUdp: true,
                    AppliedDohTemplate: request.DohTemplate,
                    AppliedAutoUpgrade: true,
                    AppliedAllowFallbackToUdp: false))
                .ToList()
            : new List<DnsDohChange>();

        return new DnsApplyResult(
            true,
            "OK",
            "Đã đổi DNS thành công.",
            snapshot,
            changes,
            "Phân giải thành công.",
            null);
    }

    public Task<DnsRestoreResult> RestoreAsync(
        DnsRestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        RestoreCalls.Add(request);

        if (RestoreHandler is not null)
        {
            return RestoreHandler(request);
        }

        var snapshot = TestData.Snapshot(
            request.AdapterGuid,
            request.InterfaceIndex,
            request.OriginalIpv4,
            request.OriginalIpv6);
        Current = snapshot;

        return Task.FromResult(new DnsRestoreResult(
            true,
            "OK",
            "Đã khôi phục DNS ban đầu.",
            snapshot,
            "Phân giải thành công.",
            null));
    }
}

internal sealed class FakeBackupStore : IDnsBackupStore
{
    private readonly Dictionary<string, DnsBackupDocument> _documents =
        new(StringComparer.OrdinalIgnoreCase);

    public bool Corrupt { get; set; }

    public int SaveCount { get; private set; }

    public IReadOnlyDictionary<string, DnsBackupDocument> Documents => _documents;

    public bool Contains(string adapterGuid) =>
        _documents.ContainsKey(Normalize(adapterGuid));

    public Task<DnsBackupLoadResult> LoadAsync(
        string adapterGuid,
        CancellationToken cancellationToken = default)
    {
        if (Corrupt)
        {
            return Task.FromResult(DnsBackupLoadResult.Corrupt("Bản sao lưu bị hỏng."));
        }

        return Task.FromResult(
            _documents.TryGetValue(Normalize(adapterGuid), out var document)
                ? DnsBackupLoadResult.Valid(Clone(document))
                : DnsBackupLoadResult.Missing());
    }

    public Task SaveAsync(DnsBackupDocument document, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        _documents[Normalize(document.AdapterGuid)] = Clone(document);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string adapterGuid, CancellationToken cancellationToken = default)
    {
        _documents.Remove(Normalize(adapterGuid));
        return Task.CompletedTask;
    }

    private static string Normalize(string guid) =>
        FileDnsBackupStore.NormalizeGuid(guid) ?? guid;

    private static DnsBackupDocument Clone(DnsBackupDocument document)
    {
        var json = JsonSerializer.Serialize(document, DnsJson.Options);
        return JsonSerializer.Deserialize<DnsBackupDocument>(json, DnsJson.Options)!;
    }
}

internal static class TestData
{
    public const string Guid = "{D3E33B3A-2D1C-498D-B5F3-65AE1B6F6BD8}";

    public const uint Index = 17;

    public static DnsAdapterInfo Adapter(
        string alias = "Ethernet",
        string? description = null,
        bool isUp = true,
        bool isPhysical = true,
        bool hasDefaultRoute = true,
        int routeMetric = 0,
        string guid = Guid,
        uint index = Index,
        int routeCost = 0,
        bool isWireless = false) =>
        new(
            guid,
            index,
            alias,
            description ?? alias,
            isUp,
            isPhysical,
            hasDefaultRoute,
            routeMetric,
            routeCost,
            isWireless);

    public static DnsAdapterSnapshot Snapshot(
        string guid = Guid,
        uint index = Index,
        DnsFamilyState? ipv4 = null,
        DnsFamilyState? ipv6 = null)
    {
        ipv4 ??= DnsFamilyState.Static(new[] { "208.67.222.222", "208.67.220.220" });
        ipv6 ??= DnsFamilyState.Automatic(new[] { "fd00:db80::1" });

        return new DnsAdapterSnapshot(
            guid,
            index,
            "Ethernet",
            ipv4,
            ipv6,
            DnsFingerprint.Compute(guid, ipv4, ipv6));
    }

    public static DnsProfile Profile(
        string id = "cloudflare-standard",
        string provider = "Cloudflare",
        bool supportsDoh = true,
        string? dohTemplate = "https://cloudflare-dns.com/dns-query",
        IReadOnlyList<string>? ipv4 = null,
        IReadOnlyList<string>? ipv6 = null) =>
        new()
        {
            Id = id,
            ProviderId = provider.ToLowerInvariant(),
            ProviderName = provider,
            Name = id,
            Description = "test",
            Filtering = "test",
            Ipv4 = ipv4 ?? new[] { "1.1.1.1", "1.0.0.1" },
            Ipv6 = ipv6 ?? new[] { "2606:4700:4700::1111", "2606:4700:4700::1001" },
            DohTemplate = supportsDoh ? dohTemplate : null,
            Protocols = supportsDoh
                ? new[] { DnsProtocol.Udp, DnsProtocol.Tcp, DnsProtocol.Doh }
                : new[] { DnsProtocol.Udp, DnsProtocol.Tcp }
        };
}
