using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using BeeDPI.Core.Dns;
using BeeDPI.Core.Ipc;

namespace BeeDPI.App.Services;

/// <summary>
/// DNS platform used by the UI. Read-only queries run the fixed helper script in
/// the current process; every mutation runs in a freshly elevated
/// <c>BeeDPI.EngineHost.exe --mode dns</c> instance that is shut down again as
/// soon as the operation finishes.
/// </summary>
internal sealed class DnsSession : IDnsPlatform, IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(170);

    private readonly ScriptDnsPlatform _local;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public DnsSession(string? baseDirectory = null, Action<string>? log = null)
    {
        _local = new ScriptDnsPlatform(baseDirectory ?? AppContext.BaseDirectory, log);
    }

    public Task<DnsCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        _local.GetCapabilitiesAsync(cancellationToken);

    public Task<IReadOnlyList<DnsAdapterInfo>> ListAdaptersAsync(CancellationToken cancellationToken = default) =>
        _local.ListAdaptersAsync(cancellationToken);

    public Task<DnsAdapterSnapshot> GetSnapshotAsync(
        uint interfaceIndex,
        CancellationToken cancellationToken = default) =>
        _local.GetSnapshotAsync(interfaceIndex, cancellationToken);

    public async Task<DnsApplyResult> ApplyAsync(
        DnsApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        var resultJson = await RunElevatedAsync(
                HostProtocol.DnsApplyAction,
                ScriptDnsPlatform.SerializeApplyRequest(request),
                cancellationToken)
            .ConfigureAwait(false);

        return ScriptDnsPlatform.ParseApplyResult(resultJson);
    }

    public async Task<DnsRestoreResult> RestoreAsync(
        DnsRestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        var resultJson = await RunElevatedAsync(
                HostProtocol.DnsRestoreAction,
                ScriptDnsPlatform.SerializeRestoreRequest(request),
                cancellationToken)
            .ConfigureAwait(false);

        return ScriptDnsPlatform.ParseRestoreResult(resultJson);
    }

    private async Task<string> RunElevatedAsync(
        string action,
        string requestJson,
        CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new DnsOperationException("SESSION_CLOSED", "Ứng dụng đang đóng.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var pipeName = "BeeDPI.Dns." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N");
            await using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                inBufferSize: 64 * 1024,
                outBufferSize: 64 * 1024);

            Process? host = null;
            try
            {
                host = HostLauncher.LaunchDns(pipeName);
                if (host is null)
                {
                    throw new DnsOperationException(
                        "DNS_HOST_START_FAILED",
                        "Không thể khởi động thành phần DNS.");
                }

                using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    connectTimeout.CancelAfter(ConnectTimeout);

                    var waitConnect = server.WaitForConnectionAsync(connectTimeout.Token);
                    var waitExit = host.WaitForExitAsync(connectTimeout.Token);
                    var completed = await Task.WhenAny(waitConnect, waitExit).ConfigureAwait(false);

                    if (completed == waitExit)
                    {
                        throw new DnsOperationException(
                            "DNS_HOST_EXITED",
                            "Thành phần DNS đã dừng trước khi kết nối.");
                    }

                    try
                    {
                        await waitConnect.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new DnsOperationException(
                            "DNS_HOST_TIMEOUT",
                            "Hết thời gian chờ xác nhận quyền quản trị.");
                    }
                }

                var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                using var reader = new StreamReader(
                    server,
                    encoding,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 4096,
                    leaveOpen: true);
                using var writer = new StreamWriter(server, encoding, bufferSize: 4096, leaveOpen: true)
                {
                    AutoFlush = false
                };

                await HostProtocol
                    .SendAsync(writer, HostProtocol.DnsRequest(action, requestJson), cancellationToken)
                    .ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

                using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                responseTimeout.CancelAfter(ResponseTimeout);

                while (true)
                {
                    HostMessage? message;
                    try
                    {
                        message = await HostProtocol.ReceiveAsync(reader, responseTimeout.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new DnsOperationException(
                            "DNS_TIMEOUT",
                            "Quá thời gian xử lý DNS.");
                    }

                    if (message is null)
                    {
                        throw new DnsOperationException(
                            "DNS_HOST_EXITED",
                            "Thành phần DNS đã dừng khi đang xử lý.");
                    }

                    switch ((message.Type ?? string.Empty).ToLowerInvariant())
                    {
                        case HostProtocol.DnsResult:
                            return message.Payload
                                   ?? throw new DnsOperationException(
                                       "DNS_EMPTY_RESULT",
                                       "Thành phần DNS không trả về kết quả.");

                        case HostProtocol.Error:
                            throw new DnsOperationException(
                                message.Code ?? "DNS_FAILED",
                                message.Message ?? "Không thể xử lý DNS.");

                        default:
                            // "ready" and any future keep-alive messages are ignored.
                            continue;
                    }
                }
            }
            finally
            {
                if (host is not null)
                {
                    await ShutdownHostAsync(host, server, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task ShutdownHostAsync(
        Process host,
        NamedPipeServerStream server,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!host.HasExited && server.IsConnected)
            {
                var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                using var writer = new StreamWriter(server, encoding, bufferSize: 4096, leaveOpen: true)
                {
                    AutoFlush = false
                };

                await HostProtocol
                    .SendAsync(writer, new HostMessage { Type = HostProtocol.Shutdown }, CancellationToken.None)
                    .ConfigureAwait(false);
                await writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
            // The host may already have closed the pipe.
        }

        var exited = await WaitForExitAsync(host, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        if (!exited)
        {
            try
            {
                if (!host.HasExited)
                {
                    host.Kill();
                }
            }
            catch
            {
                // Best effort.
            }

            await WaitForExitAsync(host, TimeSpan.FromSeconds(4)).ConfigureAwait(false);
        }

        try
        {
            host.Dispose();
        }
        catch
        {
            // Best effort.
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            try
            {
                return process.HasExited;
            }
            catch
            {
                return true;
            }
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var acquired = false;
        try
        {
            acquired = await _gate.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Shutdown must never hang the window forever.
        }
        finally
        {
            if (acquired)
            {
                _gate.Release();
            }
        }

        _gate.Dispose();
    }
}
