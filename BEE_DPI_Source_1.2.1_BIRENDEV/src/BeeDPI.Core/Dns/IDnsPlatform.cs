namespace BeeDPI.Core.Dns;

/// <summary>
/// Abstraction over the Windows DNS platform. The production implementation
/// runs a fixed PowerShell script; tests use an in-memory fake so automation
/// never touches the real network configuration.
/// </summary>
public interface IDnsPlatform
{
    Task<DnsCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DnsAdapterInfo>> ListAdaptersAsync(CancellationToken cancellationToken = default);

    Task<DnsAdapterSnapshot> GetSnapshotAsync(uint interfaceIndex, CancellationToken cancellationToken = default);

    Task<DnsApplyResult> ApplyAsync(DnsApplyRequest request, CancellationToken cancellationToken = default);

    Task<DnsRestoreResult> RestoreAsync(DnsRestoreRequest request, CancellationToken cancellationToken = default);
}
