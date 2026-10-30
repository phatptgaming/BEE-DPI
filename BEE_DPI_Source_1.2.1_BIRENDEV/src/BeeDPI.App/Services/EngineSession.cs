using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using BeeDPI.Core;
using BeeDPI.Core.Ipc;

namespace BeeDPI.App.Services;

/// <summary>
/// UI-side controller for the elevated engine host. Only the host it launched
/// is ever stopped; the app never searches for or kills processes by name.
/// </summary>
internal sealed class EngineSession : IAsyncDisposable
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private NamedPipeServerStream? _server;
    private Process? _host;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Task? _readLoop;
    private TaskCompletionSource<HostMessage>? _startSignal;
    private TaskCompletionSource<HostMessage>? _stopSignal;
    private CancellationTokenSource? _lifetime;
    private volatile bool _connected;
    private volatile bool _intentionalClose;
    private bool _disposed;

    public event Action<string>? Faulted;

    public bool IsConnected => _connected;

    public int? EngineProcessId { get; private set; }

    public bool HasHost => _host is { HasExited: false };

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        // Connect, disconnect and dispose are serialized. This matters when the
        // user closes the window right after answering the UAC prompt: teardown
        // must not free the session while ConnectAsync is still using it.
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                throw new EngineSessionException("Ứng dụng đang đóng.", "SESSION_CLOSED");
            }

            if (_connected)
            {
                return;
            }

            await ConnectCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task ConnectCoreAsync(CancellationToken cancellationToken)
    {
        await CleanupAsync().ConfigureAwait(false);
        _intentionalClose = false;

        var validation = EngineDefinition.Validate(AppContext.BaseDirectory);
        if (!validation.IsValid)
        {
            throw new EngineSessionException(
                "Thiếu thành phần engine. Vui lòng cài đặt lại BEE DPI.",
                "ENGINE_FILES_MISSING");
        }

        var hostPath = Path.Combine(AppContext.BaseDirectory, "BeeDPI.EngineHost.exe");
        if (!File.Exists(hostPath))
        {
            throw new EngineSessionException(
                "Thiếu thành phần điều khiển. Vui lòng cài đặt lại BEE DPI.",
                "HOST_MISSING");
        }

        var pipeName = "BeeDPI." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N");
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _lifetime = lifetime;

        _server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            inBufferSize: 64 * 1024,
            outBufferSize: 64 * 1024);

        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            _host = HostLauncher.Launch(pipeName);

            using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(60));

                try
                {
                    var waitConnect = _server.WaitForConnectionAsync(connectTimeout.Token);
                    var waitExit = _host.WaitForExitAsync(connectTimeout.Token);
                    var completed = await Task.WhenAny(waitConnect, waitExit).ConfigureAwait(false);

                    if (completed == waitExit)
                    {
                        throw new EngineSessionException(
                            "Không thể khởi động thành phần điều khiển.",
                            "HOST_EXITED");
                    }

                    await waitConnect.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new EngineSessionException(
                        "Hết thời gian chờ xác nhận quyền quản trị.",
                        "UAC_TIMEOUT");
                }
            }

            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            _reader = new StreamReader(_server, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            _writer = new StreamWriter(_server, encoding, bufferSize: 4096, leaveOpen: true)
            {
                AutoFlush = false
            };

            _readLoop = Task.Run(() => ReadLoopAsync(lifetime.Token));

            _startSignal = new TaskCompletionSource<HostMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            await SendAsync(new HostMessage { Type = HostProtocol.Start }, lifetime.Token).ConfigureAwait(false);

            HostMessage reply;
            using (var startTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                startTimeout.CancelAfter(TimeSpan.FromSeconds(25));

                try
                {
                    var delay = Task.Delay(Timeout.InfiniteTimeSpan, startTimeout.Token);
                    var completed = await Task.WhenAny(_startSignal.Task, delay).ConfigureAwait(false);
                    if (completed != _startSignal.Task)
                    {
                        throw new EngineSessionException(
                            "Engine không phản hồi khi khởi động.",
                            "START_TIMEOUT");
                    }

                    reply = await _startSignal.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw new EngineSessionException(
                        "Engine không phản hồi khi khởi động.",
                        "START_TIMEOUT");
                }
            }

            if (reply.Type == HostProtocol.Error)
            {
                throw new EngineSessionException(
                    reply.Message ?? "Không thể khởi động engine.",
                    reply.Code);
            }

            if (reply.Type != HostProtocol.ReadyMessage)
            {
                throw new EngineSessionException(
                    "Không thể xác nhận trạng thái engine.",
                    "START_UNCONFIRMED");
            }

            EngineProcessId = reply.Pid;
            _connected = true;
        }
        catch
        {
            await CleanupAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisconnectAsync(bool force = false)
    {
        await _lifecycleLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync(force).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task DisconnectCoreAsync(bool force)
    {
        _intentionalClose = true;
        var graceful = false;

        try
        {
            if (_host is { HasExited: false } && _writer is not null && _server is { IsConnected: true })
            {
                _stopSignal = new TaskCompletionSource<HostMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                await SendAsync(new HostMessage { Type = HostProtocol.Stop }, CancellationToken.None).ConfigureAwait(false);

                var timeout = force ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(12);
                var completed = await Task.WhenAny(_stopSignal.Task, Task.Delay(timeout)).ConfigureAwait(false);
                if (completed == _stopSignal.Task)
                {
                    var reply = await _stopSignal.Task.ConfigureAwait(false);
                    if (reply.Type != HostProtocol.Error)
                    {
                        graceful = true;
                        try
                        {
                            await SendAsync(new HostMessage { Type = HostProtocol.Shutdown }, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch
                        {
                            // The host may close the pipe as it exits.
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn("disconnect request: " + exception.Message);
        }

        if (_host is not null)
        {
            if (!graceful)
            {
                TryKill(_host);
            }

            var exited = await WaitForExitAsync(_host, TimeSpan.FromSeconds(6)).ConfigureAwait(false);
            if (!exited)
            {
                TryKill(_host);
                exited = await WaitForExitAsync(_host, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }

            if (!exited)
            {
                throw new EngineSessionException(
                    "Không thể dừng engine an toàn.",
                    "STOP_FAILED");
            }
        }

        var enginePid = EngineProcessId;
        await CleanupAsync().ConfigureAwait(false);

        if (enginePid is > 0 && IsProcessAlive(enginePid.Value))
        {
            await Task.Delay(400).ConfigureAwait(false);
            if (IsProcessAlive(enginePid.Value))
            {
                throw new EngineSessionException(
                    "Engine vẫn đang chạy. Vui lòng thử lại.",
                    "ENGINE_STILL_RUNNING");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                await DisconnectCoreAsync(force: true).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                AppLog.Warn("session dispose: " + exception.Message);
                await CleanupAsync().ConfigureAwait(false);
            }

            _sendLock.Dispose();
        }
        finally
        {
            _lifecycleLock.Release();
        }

        _lifecycleLock.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await HostProtocol.ReceiveAsync(_reader!, cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                switch (message.Type)
                {
                    case HostProtocol.ReadyMessage:
                        _startSignal?.TrySetResult(message);
                        break;

                    case HostProtocol.Stopped:
                        _connected = false;
                        _stopSignal?.TrySetResult(message);
                        break;

                    case HostProtocol.Error:
                        if (_startSignal is { Task.IsCompleted: false })
                        {
                            _startSignal.TrySetResult(message);
                        }
                        else if (_stopSignal is { Task.IsCompleted: false })
                        {
                            _stopSignal.TrySetResult(message);
                        }
                        else if (!_intentionalClose)
                        {
                            Faulted?.Invoke(message.Message ?? "Đã xảy ra lỗi.");
                        }

                        break;

                    case HostProtocol.Event when message.State == HostProtocol.StateFaulted:
                        _connected = false;
                        if (!_intentionalClose)
                        {
                            Faulted?.Invoke(message.Message ?? "Kết nối đã bị gián đoạn.");
                        }

                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal teardown.
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Pipe closed with the host.
        }
        catch (Exception exception)
        {
            AppLog.Warn("read loop: " + exception.Message);
        }
        finally
        {
            _connected = false;
            _startSignal?.TrySetException(new EngineSessionException("Thành phần điều khiển đã dừng."));
            _stopSignal?.TrySetResult(new HostMessage { Type = HostProtocol.Stopped });

            if (!_intentionalClose)
            {
                Faulted?.Invoke("Kết nối đã bị gián đoạn.");
            }
        }
    }

    private async Task SendAsync(HostMessage message, CancellationToken cancellationToken)
    {
        if (_writer is null)
        {
            throw new EngineSessionException("Kênh điều khiển chưa sẵn sàng.", "PIPE_NOT_READY");
        }

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await HostProtocol.SendAsync(_writer, message, cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task CleanupAsync()
    {
        _intentionalClose = true;
        _connected = false;

        try
        {
            _lifetime?.Cancel();
        }
        catch
        {
            // Best effort.
        }

        try
        {
            _writer?.Dispose();
        }
        catch
        {
            // Best effort.
        }

        try
        {
            _reader?.Dispose();
        }
        catch
        {
            // Best effort.
        }

        try
        {
            _server?.Dispose();
        }
        catch
        {
            // Best effort.
        }

        _writer = null;
        _reader = null;
        _server = null;

        var host = _host;
        _host = null;
        if (host is not null)
        {
            try
            {
                if (!host.HasExited)
                {
                    host.Kill();
                    await WaitForExitAsync(host, TimeSpan.FromSeconds(4)).ConfigureAwait(false);
                }
            }
            catch
            {
                // Best effort.
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

        _startSignal = null;
        _stopSignal = null;
        EngineProcessId = null;

        var lifetime = _lifetime;
        _lifetime = null;
        lifetime?.Dispose();

        var readLoop = _readLoop;
        _readLoop = null;
        if (readLoop is not null)
        {
            try
            {
                await readLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch
            {
                // The loop is cancelled with the pipe.
            }
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
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch
        {
            // Best effort.
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Exception exception) when (exception is Win32Exception or NotSupportedException)
        {
            // Access denied: be conservative and report that it is still running.
            return true;
        }
    }
}
