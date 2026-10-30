using System.Text;
using BeeDPI.Core;
using BeeDPI.EngineHost.Diagnostics;
using BeeDPI.EngineHost.Native;

namespace BeeDPI.EngineHost;

internal sealed record EngineStartResult(
    bool Success,
    int Pid,
    string Code,
    string FriendlyMessage,
    string? TechnicalDetail);

internal sealed record EngineStopResult(bool Success, string? TechnicalDetail);

/// <summary>
/// Owns exactly one engine process. It never enumerates or kills processes by
/// name; only the handle of the process it created is ever used.
/// </summary>
internal sealed class EngineSupervisor : IAsyncDisposable
{
    private const string ReadyMarker = "Filter activated, GoodbyeDPI is now running!";

    private readonly string _baseDirectory;
    private readonly object _sync = new();
    private readonly JobObject _job = new();

    private PseudoConsoleProcess? _current;
    private PseudoConsoleProcess? _armed;
    private bool _expectedExit;
    private bool _disposed;

    public EngineSupervisor(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
    }

    public event Action<int>? EngineExited;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _current is { HasExited: false };
            }
        }
    }

    public int? ProcessId
    {
        get
        {
            lock (_sync)
            {
                return _current is { HasExited: false } process ? process.ProcessId : null;
            }
        }
    }

    public async Task<EngineStartResult> StartAsync()
    {
        PseudoConsoleProcess? stale;
        lock (_sync)
        {
            if (_disposed)
            {
                return new EngineStartResult(false, 0, "ENGINE_STOPPED", "Ứng dụng đang đóng.", null);
            }

            if (_current is { HasExited: false } running)
            {
                return new EngineStartResult(true, running.ProcessId, string.Empty, string.Empty, null);
            }

            stale = _current;
            _current = null;
            _armed = null;
            _expectedExit = false;
        }

        if (stale is not null)
        {
            stale.SuppressExitNotification();
            stale.Dispose();
        }

        var validation = EngineDefinition.Validate(_baseDirectory);
        if (!validation.IsValid)
        {
            var missing = string.Join(", ", validation.MissingFiles);
            HostLog.Warn("engine files missing: " + missing);
            return new EngineStartResult(
                false,
                0,
                "ENGINE_FILES_MISSING",
                "Thiếu thành phần engine. Vui lòng cài đặt lại BEE DPI.",
                "Missing: " + missing + " in " + validation.Directory);
        }

        var executable = Path.Combine(validation.Directory, EngineDefinition.ExecutableName);
        PseudoConsoleProcess session;

        try
        {
            HostLog.Info("starting engine: " + executable + " " + EngineDefinition.Arguments);
            session = PseudoConsoleProcess.Start(executable, EngineDefinition.Arguments, validation.Directory);
        }
        catch (Exception exception)
        {
            HostLog.Error("could not start engine", exception);
            return new EngineStartResult(
                false,
                0,
                "ENGINE_START_FAILED",
                "Không thể khởi động engine. Vui lòng thử lại.",
                exception.Message);
        }

        var startupSignal = new TaskCompletionSource<StartupSignal>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new StringBuilder();

        session.OutputReceived += text =>
        {
            string combined;

            lock (output)
            {
                if (output.Length < 32 * 1024)
                {
                    output.Append(text);
                }

                // The marker may be split across two console reads, so always
                // test the accumulated output rather than a single chunk.
                combined = output.ToString();
            }

            if (combined.Contains(ReadyMarker, StringComparison.Ordinal))
            {
                startupSignal.TrySetResult(StartupSignal.Ready);
            }
            else if (combined.Contains("Error opening filter", StringComparison.OrdinalIgnoreCase)
                     || combined.Contains("ERROR:", StringComparison.Ordinal)
                     || combined.Contains("Can't load", StringComparison.OrdinalIgnoreCase)
                     || combined.Contains("parameter error", StringComparison.OrdinalIgnoreCase))
            {
                startupSignal.TrySetResult(StartupSignal.EngineError);
            }
        };

        session.Exited += _ => startupSignal.TrySetResult(StartupSignal.Exited);

        lock (_sync)
        {
            _current = session;
            _expectedExit = false;
        }

        _job.Assign(session.ProcessHandle);
        session.BeginReading();

        StartupSignal signal;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
        {
            var finished = await Task.WhenAny(
                startupSignal.Task,
                Task.Delay(Timeout.InfiniteTimeSpan, timeout.Token)).ConfigureAwait(false);

            if (finished != startupSignal.Task)
            {
                return await FailStartAsync(
                    session,
                    "ENGINE_START_TIMEOUT",
                    "Engine không phản hồi khi khởi động.",
                    "Readiness timeout. Output: " + SafeOutput(output)).ConfigureAwait(false);
            }

            signal = await startupSignal.Task.ConfigureAwait(false);
        }

        if (signal != StartupSignal.Ready)
        {
            var code = signal == StartupSignal.Exited ? session.ExitCode : -1;
            return await FailStartAsync(
                session,
                "ENGINE_START_FAILED",
                "Không thể khởi động engine. Hãy kiểm tra quyền quản trị hoặc phần mềm bảo mật.",
                "Signal=" + signal + ", exitCode=" + code + ". Output: " + SafeOutput(output)).ConfigureAwait(false);
        }

        // Let the engine settle for a moment before declaring this connection healthy.
        await Task.Delay(200).ConfigureAwait(false);

        if (session.HasExited)
        {
            return await FailStartAsync(
                session,
                "ENGINE_START_FAILED",
                "Engine vừa dừng ngay sau khi khởi động.",
                "ExitCode=" + session.ExitCode + ". Output: " + SafeOutput(output)).ConfigureAwait(false);
        }

        lock (_sync)
        {
            if (!ReferenceEquals(_current, session))
            {
                // A concurrent stop won the race.
                return new EngineStartResult(false, 0, "ENGINE_STOPPED", "Kết nối đã được hủy.", null);
            }

            _armed = session;
        }

        session.Exited += code => HandleRunningExit(session, code);
        if (session.HasExited)
        {
            // Covers the narrow race where the process died before the handler was attached.
            HandleRunningExit(session, session.ExitCode);
        }

        HostLog.Info("engine ready, pid=" + session.ProcessId);
        return new EngineStartResult(true, session.ProcessId, string.Empty, string.Empty, null);
    }

    public async Task<EngineStopResult> StopAsync()
    {
        PseudoConsoleProcess? session;
        lock (_sync)
        {
            session = _current;
            _current = null;
            _armed = null;
            _expectedExit = true;
        }

        if (session is null)
        {
            return new EngineStopResult(true, null);
        }

        session.SuppressExitNotification();
        session.Kill();
        var exited = await session.WaitForExitAsync(TimeSpan.FromSeconds(6)).ConfigureAwait(false);
        session.Dispose();

        if (!exited)
        {
            HostLog.Error("engine did not exit within the timeout");
            return new EngineStopResult(false, "Engine did not exit within 6 seconds.");
        }

        HostLog.Info("engine stopped");
        // Give WinDivert a moment to release its filter handles and network resources.
        await Task.Delay(150).ConfigureAwait(false);
        return new EngineStopResult(true, null);
    }

    public void EmergencyStop()
    {
        PseudoConsoleProcess? session;
        lock (_sync)
        {
            session = _current;
            _current = null;
            _armed = null;
            _expectedExit = true;
        }

        if (session is null)
        {
            return;
        }

        session.SuppressExitNotification();
        session.Kill();
        session.WaitForExitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        session.Dispose();
        HostLog.Warn("emergency engine stop");
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await StopAsync().ConfigureAwait(false);
        _job.Dispose();
    }

    private async Task<EngineStartResult> FailStartAsync(
        PseudoConsoleProcess session,
        string code,
        string friendlyMessage,
        string technicalDetail)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_current, session))
            {
                _current = null;
                _armed = null;
                _expectedExit = true;
            }
        }

        HostLog.Warn("engine start failed: " + code + " " + technicalDetail);
        session.SuppressExitNotification();
        session.Kill();
        await session.WaitForExitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
        session.Dispose();

        return new EngineStartResult(false, 0, code, friendlyMessage, technicalDetail);
    }

    private void HandleRunningExit(PseudoConsoleProcess session, int exitCode)
    {
        bool notify;
        lock (_sync)
        {
            notify = !_disposed
                     && !_expectedExit
                     && ReferenceEquals(_current, session)
                     && ReferenceEquals(_armed, session);

            if (notify)
            {
                _current = null;
                _armed = null;
            }
        }

        if (!notify)
        {
            return;
        }

        HostLog.Warn("engine exited unexpectedly, code=" + exitCode);
        EngineExited?.Invoke(exitCode);
    }

    private static string SafeOutput(StringBuilder builder)
    {
        lock (builder)
        {
            var text = builder.ToString().Replace('\r', ' ').Replace('\n', ' ');
            return text.Length <= 600 ? text : text[..600];
        }
    }

    private enum StartupSignal
    {
        Ready,
        EngineError,
        Exited
    }
}
