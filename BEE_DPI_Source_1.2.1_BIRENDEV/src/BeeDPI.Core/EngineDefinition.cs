namespace BeeDPI.Core;

/// <summary>
/// Describes the bundled, unmodified third-party engine and the verified launch
/// parameters.
///
/// The launch profile reproduces the verified configuration observed in the
/// reference Steam Bypass 2.0.0 application:
///
/// <code>-5 --dns-addr 1.1.1.1 --dns-port 53 --dnsv6-addr 2606:4700:4700::1111 --dnsv6-port 53</code>
///
/// The DNS options are handled entirely inside GoodbyeDPI: it redirects UDP DNS
/// packets to the supplied resolvers. BEE DPI does not change Windows adapter
/// DNS settings, routes, proxy settings or the public IP for this profile.
/// </summary>
public static class EngineDefinition
{
    public const string ExecutableName = "goodbyedpi.exe";
    public const string DllName = "WinDivert.dll";
    public const string DriverName = "WinDivert64.sys";

    /// <summary>Upstream engine release bundled with this build.</summary>
    public const string EngineVersion = "0.2.3rc3";

    /// <summary>WinDivert version published together with the engine.</summary>
    public const string WinDivertVersion = "2.2";

    /// <summary>
    /// Verified launch arguments. Order matters: GoodbyeDPI requires each
    /// <c>--dns-addr</c>/<c>--dnsv6-addr</c> to appear before its port option.
    /// </summary>
    public const string Arguments =
        "-5 --dns-addr 1.1.1.1 --dns-port 53 --dnsv6-addr 2606:4700:4700::1111 --dnsv6-port 53";

    /// <summary>Human readable summary of the DNS redirection performed by the engine.</summary>
    public const string DnsRedirectSummary =
        "DNS IPv4 1.1.1.1:53, DNS IPv6 [2606:4700:4700::1111]:53 (xử lý trong engine, không đổi DNS của adapter)";

    public const string EngineFolderName = "Engine";

    public static readonly IReadOnlyList<string> RequiredFiles =
        new[] { ExecutableName, DllName, DriverName };

    public static string ResolveEngineDirectory(string baseDirectory) =>
        Path.Combine(baseDirectory, EngineFolderName);

    public static EngineValidation Validate(string baseDirectory)
    {
        var directory = ResolveEngineDirectory(baseDirectory);
        var missing = new List<string>();

        foreach (var file in RequiredFiles)
        {
            var path = Path.Combine(directory, file);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                missing.Add(file);
            }
        }

        return new EngineValidation(directory, missing);
    }
}

public sealed record EngineValidation(string Directory, IReadOnlyList<string> MissingFiles)
{
    public bool IsValid => MissingFiles.Count == 0;
}
