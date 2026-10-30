using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeeDPI.Core.Dns;

internal static class DnsJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

internal static class DnsFingerprint
{
    /// <summary>
    /// Canonical, order-independent fingerprint of an adapter's DNS state. It is
    /// computed by the DNS script; C# only compares the resulting string.
    /// </summary>
    public static string Compute(
        string interfaceGuid,
        DnsFamilyState ipv4,
        DnsFamilyState ipv6)
    {
        static string Part(DnsFamilyState state) =>
            state.IsStatic
                ? "static[" + string.Join(",", state.ServerAddresses.OrderBy(a => a, StringComparer.OrdinalIgnoreCase)) + "]"
                : "auto";

        return string.Join("|", interfaceGuid.Trim().ToUpperInvariant(), Part(ipv4), Part(ipv6));
    }
}
