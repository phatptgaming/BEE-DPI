using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace BeeDPI.Core.Dns;

/// <summary>
/// Production <see cref="IDnsPlatform"/> that executes a fixed PowerShell script
/// shipped with BEE DPI. All user-controlled values travel in a temporary JSON
/// request file; nothing is concatenated into a command line.
/// </summary>
public sealed class ScriptDnsPlatform : IDnsPlatform
{
    private const string ResultMarker = "##BEEDPI-RESULT##";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    private readonly string _baseDirectory;
    private readonly Action<string>? _log;

    public ScriptDnsPlatform(string? baseDirectory = null, Action<string>? log = null)
    {
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        _log = log;
    }

    public async Task<DnsCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var json = await ExecuteRawAsync(SerializeCapabilitiesRequest(), cancellationToken).ConfigureAwait(false);
        return ParseCapabilities(json);
    }

    public async Task<IReadOnlyList<DnsAdapterInfo>> ListAdaptersAsync(CancellationToken cancellationToken = default)
    {
        var json = await ExecuteRawAsync(SerializeListRequest(), cancellationToken).ConfigureAwait(false);
        return ParseAdapterList(json);
    }

    public async Task<DnsAdapterSnapshot> GetSnapshotAsync(
        uint interfaceIndex,
        CancellationToken cancellationToken = default)
    {
        var json = await ExecuteRawAsync(SerializeSnapshotRequest(interfaceIndex), cancellationToken)
            .ConfigureAwait(false);
        return ParseSnapshot(json);
    }

    public async Task<DnsApplyResult> ApplyAsync(
        DnsApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        var json = await ExecuteRawAsync(SerializeApplyRequest(request), cancellationToken).ConfigureAwait(false);
        return ParseApplyResult(json);
    }

    public async Task<DnsRestoreResult> RestoreAsync(
        DnsRestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        var json = await ExecuteRawAsync(SerializeRestoreRequest(request), cancellationToken).ConfigureAwait(false);
        return ParseRestoreResult(json);
    }

    /// <summary>
    /// Executes the fixed helper script with a raw request JSON document and
    /// returns the raw result JSON. Used by the elevated host, which must not
    /// depend on UI-side types.
    /// </summary>
    public async Task<string> ExecuteRawAsync(string requestJson, CancellationToken cancellationToken = default)
    {
        var scriptPath = Path.Combine(_baseDirectory, "tools", "dns-manager.ps1");
        if (!File.Exists(scriptPath))
        {
            throw new DnsOperationException(
                "DNS_SCRIPT_MISSING",
                "Thiếu thành phần quản lý DNS. Vui lòng cài đặt lại BEE DPI.");
        }

        var tempDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BeeDPI",
            "temp");
        Directory.CreateDirectory(tempDirectory);

        var requestPath = Path.Combine(tempDirectory, "dns-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            await File.WriteAllTextAsync(
                    requestPath,
                    requestJson,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);

            var startInfo = new ProcessStartInfo
            {
                FileName = ResolvePowerShell(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                WorkingDirectory = _baseDirectory
            };

            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("-RequestPath");
            startInfo.ArgumentList.Add(requestPath);

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new DnsOperationException("DNS_SCRIPT_START_FAILED", "Không thể khởi động thành phần DNS.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DefaultTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw new DnsOperationException("DNS_SCRIPT_TIMEOUT", "Quá thời gian xử lý DNS.");
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            var resultLine = stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r'))
                .LastOrDefault(line => line.StartsWith(ResultMarker, StringComparison.Ordinal));

            if (resultLine is null)
            {
                _log?.Invoke("dns script produced no result (exit=" + process.ExitCode + "): " + Truncate(stderr));
                throw new DnsOperationException(
                    "DNS_SCRIPT_NO_RESULT",
                    "Thành phần quản lý DNS không trả về kết quả.",
                    "exit=" + process.ExitCode + " stderr=" + Truncate(stderr));
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                _log?.Invoke("dns script stderr: " + Truncate(stderr));
            }

            return resultLine[ResultMarker.Length..];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DnsOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new DnsOperationException(
                "DNS_SCRIPT_FAILED",
                "Không thể chạy thành phần quản lý DNS.",
                exception.ToString(),
                exception);
        }
        finally
        {
            try
            {
                if (File.Exists(requestPath))
                {
                    File.Delete(requestPath);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }

    public static string SerializeCapabilitiesRequest() =>
        JsonSerializer.Serialize(new DnsScriptRequest { Action = "capabilities" }, DnsJson.Options);

    public static string SerializeListRequest() =>
        JsonSerializer.Serialize(new DnsScriptRequest { Action = "list" }, DnsJson.Options);

    public static string SerializeSnapshotRequest(uint interfaceIndex) =>
        JsonSerializer.Serialize(
            new DnsScriptRequest { Action = "snapshot", InterfaceIndex = interfaceIndex },
            DnsJson.Options);

    public static string SerializeApplyRequest(DnsApplyRequest request) =>
        JsonSerializer.Serialize(
            new DnsScriptRequest
            {
                Action = "apply",
                InterfaceGuid = request.AdapterGuid,
                InterfaceIndex = request.InterfaceIndex,
                ExpectedFingerprint = request.ExpectedFingerprint,
                Ipv4 = request.Ipv4.ToList(),
                Ipv6 = request.Ipv6.ToList(),
                UseDoh = request.UseDoh,
                DohTemplate = request.DohTemplate,
                DohAddresses = request.DohAddresses.ToList(),
                PreviousDohChanges = request.PreviousDohChanges.ToList()
            },
            DnsJson.Options);

    public static string SerializeRestoreRequest(DnsRestoreRequest request) =>
        JsonSerializer.Serialize(
            new DnsScriptRequest
            {
                Action = "restore",
                InterfaceGuid = request.AdapterGuid,
                InterfaceIndex = request.InterfaceIndex,
                ExpectedFingerprint = request.ExpectedFingerprint,
                OriginalIpv4 = request.OriginalIpv4,
                OriginalIpv6 = request.OriginalIpv6,
                PreviousDohChanges = request.PreviousDohChanges.ToList()
            },
            DnsJson.Options);

    public static IReadOnlyList<DnsAdapterInfo> ParseAdapterList(string json)
    {
        var response = Deserialize(json);
        EnsureSuccess(response, "LIST_FAILED", "Không thể xác định mạng.");
        return response.Adapters?.Select(ToAdapter).ToList() ?? new List<DnsAdapterInfo>();
    }

    public static DnsCapabilities ParseCapabilities(string json)
    {
        var response = Deserialize(json);
        EnsureSuccess(response, "CAPABILITIES_FAILED", "Không thể kiểm tra khả năng DNS của Windows.");
        var capabilities = response.Capabilities;
        return new DnsCapabilities(
            capabilities?.NativeDohSupported ?? false,
            capabilities?.WindowsBuild ?? 0,
            capabilities?.Detail ?? string.Empty);
    }

    public static DnsAdapterSnapshot ParseSnapshot(string json)
    {
        var response = Deserialize(json);
        EnsureSuccess(response, "SNAPSHOT_FAILED", "Không thể đọc cấu hình DNS hiện tại.");
        return ToSnapshot(response.Snapshot)
               ?? throw new DnsOperationException("SNAPSHOT_EMPTY", "Không thể đọc cấu hình DNS hiện tại.");
    }

    public static DnsApplyResult ParseApplyResult(string json)
    {
        var response = Deserialize(json);
        return new DnsApplyResult(
            response.Ok,
            response.Code ?? (response.Ok ? "OK" : "APPLY_FAILED"),
            response.Message ?? (response.Ok ? "Đã đổi DNS thành công." : "Không thể áp dụng DNS."),
            ToSnapshot(response.Snapshot),
            response.DohChanges ?? new List<DnsDohChange>(),
            response.Verification,
            response.Rollback);
    }

    public static DnsRestoreResult ParseRestoreResult(string json)
    {
        var response = Deserialize(json);
        return new DnsRestoreResult(
            response.Ok,
            response.Code ?? (response.Ok ? "OK" : "RESTORE_FAILED"),
            response.Message ?? (response.Ok ? "Đã khôi phục DNS ban đầu." : "Không thể khôi phục DNS."),
            ToSnapshot(response.Snapshot),
            response.Verification,
            response.Rollback);
    }

    private static DnsScriptResponse Deserialize(string json)
    {
        var response = JsonSerializer.Deserialize<DnsScriptResponse>(json, DnsJson.Options);
        return response
               ?? throw new DnsOperationException(
                   "DNS_SCRIPT_BAD_RESULT",
                   "Thành phần quản lý DNS trả về dữ liệu không hợp lệ.");
    }

    private static void EnsureSuccess(DnsScriptResponse response, string code, string message)
    {
        if (!response.Ok)
        {
            throw new DnsOperationException(
                response.Code ?? code,
                response.Message ?? message,
                response.Rollback);
        }
    }

    private static DnsAdapterInfo ToAdapter(DnsAdapterJson json) => new(
        json.InterfaceGuid ?? string.Empty,
        json.InterfaceIndex,
        json.Alias ?? string.Empty,
        json.Description ?? string.Empty,
        json.IsUp,
        json.IsPhysical,
        json.HasDefaultRoute,
        json.RouteMetric,
        json.RouteCost,
        json.IsWireless);

    private static DnsAdapterSnapshot? ToSnapshot(DnsSnapshotJson? json)
    {
        if (json is null)
        {
            return null;
        }

        return new DnsAdapterSnapshot(
            json.InterfaceGuid ?? string.Empty,
            json.InterfaceIndex,
            json.Alias ?? string.Empty,
            new DnsFamilyState(
                json.Ipv4?.IsStatic ?? false,
                json.Ipv4?.ServerAddresses ?? new List<string>()),
            new DnsFamilyState(
                json.Ipv6?.IsStatic ?? false,
                json.Ipv6?.ServerAddresses ?? new List<string>()),
            json.Fingerprint ?? string.Empty);
    }

    private static string ResolvePowerShell()
    {
        var system32 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        return File.Exists(system32) ? system32 : "powershell.exe";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    private static string Truncate(string value) =>
        string.IsNullOrEmpty(value) || value.Length <= 800 ? value : value[..800];
}

internal sealed class DnsScriptRequest
{
    public string Action { get; set; } = string.Empty;

    public string? InterfaceGuid { get; set; }

    public uint? InterfaceIndex { get; set; }

    public string? ExpectedFingerprint { get; set; }

    public List<string>? Ipv4 { get; set; }

    public List<string>? Ipv6 { get; set; }

    public bool UseDoh { get; set; }

    public string? DohTemplate { get; set; }

    public List<string>? DohAddresses { get; set; }

    public DnsFamilyState? OriginalIpv4 { get; set; }

    public DnsFamilyState? OriginalIpv6 { get; set; }

    public List<DnsDohChange>? PreviousDohChanges { get; set; }
}

internal sealed class DnsScriptResponse
{
    public bool Ok { get; set; }

    public string? Code { get; set; }

    public string? Message { get; set; }

    public List<DnsAdapterJson>? Adapters { get; set; }

    public DnsSnapshotJson? Snapshot { get; set; }

    public List<DnsDohChange>? DohChanges { get; set; }

    public DnsCapabilitiesJson? Capabilities { get; set; }

    public string? Verification { get; set; }

    public string? Rollback { get; set; }
}

internal sealed class DnsCapabilitiesJson
{
    public bool NativeDohSupported { get; set; }

    public int WindowsBuild { get; set; }

    public string? Detail { get; set; }
}

internal sealed class DnsAdapterJson
{
    public string? InterfaceGuid { get; set; }

    public uint InterfaceIndex { get; set; }

    public string? Alias { get; set; }

    public string? Description { get; set; }

    public bool IsUp { get; set; }

    public bool IsPhysical { get; set; }

    public bool HasDefaultRoute { get; set; }

    public int RouteMetric { get; set; }

    public int RouteCost { get; set; }

    public bool IsWireless { get; set; }
}

internal sealed class DnsSnapshotJson
{
    public string? InterfaceGuid { get; set; }

    public uint InterfaceIndex { get; set; }

    public string? Alias { get; set; }

    public DnsFamilyJson? Ipv4 { get; set; }

    public DnsFamilyJson? Ipv6 { get; set; }

    public string? Fingerprint { get; set; }
}

internal sealed class DnsFamilyJson
{
    public bool IsStatic { get; set; }

    public List<string>? ServerAddresses { get; set; }
}
