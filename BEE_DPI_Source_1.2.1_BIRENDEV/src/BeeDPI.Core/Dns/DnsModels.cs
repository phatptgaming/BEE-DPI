using System.Net;

namespace BeeDPI.Core.Dns;

/// <summary>
/// Transport protocols that can be used with a DNS resolver. BEE DPI only
/// configures plain DNS (UDP/TCP port 53) and, on supported Windows versions,
/// native DNS over HTTPS. DoT, DoQ and DNSCrypt are catalogued for accuracy but
/// are never claimed as supported by the application.
/// </summary>
public enum DnsProtocol
{
    Udp = 0,
    Tcp = 1,
    Doh = 2,
    Dot = 3,
    Doq = 4,
    DnsCrypt = 5
}

/// <summary>An active physical network adapter that BEE DPI may configure.</summary>
public sealed record DnsAdapterInfo(
    string InterfaceGuid,
    uint InterfaceIndex,
    string Alias,
    string Description,
    bool IsUp,
    bool IsPhysical,
    bool HasDefaultRoute,
    int RouteMetric,
    int RouteCost = 0,
    bool IsWireless = false)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? Description : Alias;

    /// <summary>Safety net for controls that fall back to ToString().</summary>
    public override string ToString() => DisplayName;

    /// <summary>
    /// True when Windows reports a wireless adapter. The name fallback only
    /// covers older Windows versions that do not expose InterfaceType.
    /// </summary>
    public bool LooksWireless =>
        IsWireless
        || (Alias + " " + Description).Contains("wi-fi", StringComparison.OrdinalIgnoreCase)
        || (Alias + " " + Description).Contains("wireless", StringComparison.OrdinalIgnoreCase)
        || (Alias + " " + Description).Contains("wlan", StringComparison.OrdinalIgnoreCase);

    /// <summary>User-facing network type: Wi-Fi or Ethernet.</summary>
    public string NetworkKind => LooksWireless ? "Wi-Fi" : "Ethernet";
}

/// <summary>
/// Chooses the physical adapter that carries the default Internet route. Route
/// cost (route metric + interface metric) is used instead of adapter names, so
/// the result is stable across Windows display languages.
/// </summary>
public static class DnsAdapterSelector
{
    /// <summary>
    /// Returns the best adapter and whether the choice is genuinely ambiguous
    /// (two active adapters with the same route cost). Never throws.
    /// </summary>
    public static DnsAdapterInfo? FindActive(
        IReadOnlyList<DnsAdapterInfo> adapters,
        out bool ambiguous)
    {
        ambiguous = false;
        if (adapters.Count == 0)
        {
            return null;
        }

        var routed = adapters
            .Where(a => a.HasDefaultRoute)
            .OrderBy(a => a.RouteCost > 0 ? a.RouteCost : a.RouteMetric)
            .ThenBy(a => a.RouteMetric)
            .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (routed.Count == 0)
        {
            // No physical adapter owns a default route. The caller should warn
            // that a VPN or tunnel may be active instead of guessing silently.
            ambiguous = adapters.Count > 1;
            return adapters
                .OrderByDescending(a => a.IsUp)
                .ThenBy(a => a.RouteMetric)
                .FirstOrDefault();
        }

        var best = routed[0];
        var bestCost = best.RouteCost > 0 ? best.RouteCost : best.RouteMetric;
        ambiguous = routed.Count(a => (a.RouteCost > 0 ? a.RouteCost : a.RouteMetric) == bestCost) > 1;
        return best;
    }

    /// <summary>
    /// Revalidates a previously chosen adapter against the live list. Returns
    /// null when the adapter disappeared, changed interface index, went down or
    /// is no longer physical, so a stale identifier is never used.
    /// </summary>
    public static DnsAdapterInfo? FindLiveMatch(
        DnsAdapterInfo requested,
        IReadOnlyList<DnsAdapterInfo> live)
    {
        var match = live.FirstOrDefault(a =>
            string.Equals(
                NormalizeGuid(a.InterfaceGuid),
                NormalizeGuid(requested.InterfaceGuid),
                StringComparison.OrdinalIgnoreCase));

        if (match is null
            || match.InterfaceIndex != requested.InterfaceIndex
            || !match.IsUp
            || !match.IsPhysical)
        {
            return null;
        }

        return match;
    }

    private static string NormalizeGuid(string? value) =>
        (value ?? string.Empty).Trim().Trim('{', '}').ToUpperInvariant();
}

/// <summary>DNS state of one address family on one adapter.</summary>
public sealed record DnsFamilyState(bool IsStatic, IReadOnlyList<string> ServerAddresses)
{
    public static DnsFamilyState Automatic(IReadOnlyList<string>? effective = null) =>
        new(false, effective ?? Array.Empty<string>());

    public static DnsFamilyState Static(IReadOnlyList<string> addresses) => new(true, addresses);

    public string Display => IsStatic
        ? (ServerAddresses.Count == 0 ? "(trống)" : string.Join(", ", ServerAddresses))
        : (ServerAddresses.Count == 0 ? "Tự động (DHCP)" : "Tự động: " + string.Join(", ", ServerAddresses));
}

/// <summary>Immutable snapshot of an adapter's DNS configuration.</summary>
public sealed record DnsAdapterSnapshot(
    string InterfaceGuid,
    uint InterfaceIndex,
    string Alias,
    DnsFamilyState Ipv4,
    DnsFamilyState Ipv6,
    string Fingerprint)
{
    public bool Matches(DnsAdapterSnapshot other) =>
        string.Equals(Fingerprint, other.Fingerprint, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A DoH registration that BEE DPI created or altered, together with the state
/// that existed before. Restoring DNS uses this to undo only BEE DPI changes.
/// </summary>
public sealed record DnsDohChange(
    string ServerAddress,
    bool Existed,
    string? DohTemplate,
    bool AutoUpgrade,
    bool AllowFallbackToUdp,
    string? AppliedDohTemplate,
    bool AppliedAutoUpgrade,
    bool AppliedAllowFallbackToUdp);

public sealed record DnsApplyRequest(
    string AdapterGuid,
    uint InterfaceIndex,
    string ExpectedFingerprint,
    IReadOnlyList<string> Ipv4,
    IReadOnlyList<string> Ipv6,
    bool UseDoh,
    string? DohTemplate,
    IReadOnlyList<string> DohAddresses,
    IReadOnlyList<DnsDohChange> PreviousDohChanges);

public sealed record DnsApplyResult(
    bool Success,
    string Code,
    string Message,
    DnsAdapterSnapshot? Snapshot,
    IReadOnlyList<DnsDohChange> DohChanges,
    string? VerificationDetail,
    string? RollbackDetail);

public sealed record DnsRestoreRequest(
    string AdapterGuid,
    uint InterfaceIndex,
    string ExpectedFingerprint,
    DnsFamilyState OriginalIpv4,
    DnsFamilyState OriginalIpv6,
    IReadOnlyList<DnsDohChange> PreviousDohChanges);

public sealed record DnsRestoreResult(
    bool Success,
    string Code,
    string Message,
    DnsAdapterSnapshot? Snapshot,
    string? VerificationDetail,
    string? RollbackDetail);

/// <summary>Result of validating a set of custom DNS addresses.</summary>
public sealed record DnsValidationResult(
    bool IsValid,
    IReadOnlyList<string> Ipv4,
    IReadOnlyList<string> Ipv6,
    string? Error)
{
    public bool HasAnyAddress => Ipv4.Count > 0 || Ipv6.Count > 0;
}

/// <summary>
/// What the current Windows build can actually do. BEE DPI never claims DoT,
/// DoQ or DNSCrypt support; only the native Windows DoH client is reported.
/// </summary>
public sealed record DnsCapabilities(
    bool NativeDohSupported,
    int WindowsBuild,
    string Detail);

public sealed class DnsOperationException : Exception
{
    public DnsOperationException(string code, string message, string? technicalDetail = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        TechnicalDetail = technicalDetail;
    }

    public string Code { get; }

    public string? TechnicalDetail { get; }
}

/// <summary>Raised when the adapter DNS changed outside BEE DPI since the last apply.</summary>
public sealed class DnsConfigurationChangedException : Exception
{
    public DnsConfigurationChangedException(DnsAdapterSnapshot current)
        : base("Cấu hình DNS của adapter đã thay đổi bên ngoài BEE DPI.")
    {
        Current = current;
    }

    public DnsAdapterSnapshot Current { get; }
}

internal static class DnsAddressNormalizer
{
    public static string? Normalize(string? value, out bool isIpv4)
    {
        isIpv4 = false;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!IPAddress.TryParse(value.Trim(), out var address))
        {
            return null;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            isIpv4 = true;
            return address.ToString();
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        return null;
    }
}
