using System.Net;
using System.Net.Sockets;

namespace BeeDPI.Core.Dns;

/// <summary>
/// Validates and normalizes DNS server addresses. No untrusted text is ever
/// inserted into a command line: only parsed <see cref="IPAddress"/> values are
/// forwarded to the elevated host.
/// </summary>
public static class DnsValidation
{
    public const int MaxAddressesPerFamily = 8;

    public static DnsValidationResult Validate(
        IEnumerable<string?>? ipv4,
        IEnumerable<string?>? ipv6,
        bool requireAtLeastOne = true)
    {
        var errors = new List<string>();
        var normalizedV4 = NormalizeFamily(ipv4, AddressFamily.InterNetwork, "IPv4", errors);
        var normalizedV6 = NormalizeFamily(ipv6, AddressFamily.InterNetworkV6, "IPv6", errors);

        if (requireAtLeastOne && normalizedV4.Count == 0 && normalizedV6.Count == 0)
        {
            errors.Add("Cần nhập ít nhất một địa chỉ DNS.");
        }

        return new DnsValidationResult(
            errors.Count == 0,
            normalizedV4,
            normalizedV6,
            errors.Count == 0 ? null : string.Join(" ", errors));
    }

    public static bool TryNormalizeAddress(string? value, out string normalized, out bool isIpv4)
    {
        normalized = string.Empty;
        isIpv4 = false;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!IPAddress.TryParse(value.Trim(), out var address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            isIpv4 = true;
        }
        else if (address.AddressFamily != AddressFamily.InterNetworkV6 || address.ScopeId != 0)
        {
            return false;
        }

        if (!IsUsableUnicast(address))
        {
            return false;
        }

        normalized = address.ToString();
        return true;
    }

    /// <summary>
    /// Parses any DNS literal, including link-local addresses that Windows may
    /// report for a DHCP-provided IPv6 resolver. This is used only to validate
    /// stored informational state; user input still goes through
    /// <see cref="TryNormalizeAddress"/>.
    /// </summary>
    public static bool TryParseLiteral(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!IPAddress.TryParse(value.Trim(), out var address))
        {
            return false;
        }

        normalized = address.ToString();
        return true;
    }

    public static bool IsUsableUnicast(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            // 0.0.0.0/8, 224.0.0.0/4 (multicast), 255.255.255.255
            if (bytes[0] == 0 || bytes[0] >= 224)
            {
                return false;
            }

            return true;
        }

        if (address.IsIPv6Multicast || address.IsIPv6LinkLocal)
        {
            return false;
        }

        return true;
    }

    private static List<string> NormalizeFamily(
        IEnumerable<string?>? values,
        AddressFamily family,
        string label,
        List<string> errors)
    {
        var result = new List<string>();
        if (values is null)
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (!IPAddress.TryParse(raw.Trim(), out var address)
                || address.AddressFamily != family
                || (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0))
            {
                errors.Add($"Địa chỉ {label} không hợp lệ: {raw.Trim()}");
                continue;
            }

            if (!IsUsableUnicast(address))
            {
                errors.Add($"Địa chỉ {label} không dùng được: {raw.Trim()}");
                continue;
            }

            var normalized = address.ToString();
            if (!seen.Add(normalized))
            {
                errors.Add($"Địa chỉ {label} bị trùng: {normalized}");
                continue;
            }

            result.Add(normalized);
        }

        if (result.Count > MaxAddressesPerFamily)
        {
            errors.Add($"Chỉ hỗ trợ tối đa {MaxAddressesPerFamily} địa chỉ {label}.");
            result.RemoveRange(MaxAddressesPerFamily, result.Count - MaxAddressesPerFamily);
        }

        return result;
    }
}
