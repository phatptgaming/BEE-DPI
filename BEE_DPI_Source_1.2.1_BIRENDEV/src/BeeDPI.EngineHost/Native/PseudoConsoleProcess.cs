using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BeeDPI.EngineHost.Native;

/// <summary>
/// Runs the third-party engine attached to a pseudoconsole. A pseudoconsole is
/// important for two reasons:
///  * no console window is ever visible to the user;
///  * the engine's stdout behaves like a terminal stream (line buffered), so the
///    host can wait for the real "Filter activated" message instead of guessing
///    that the engine is healthy just because a process exists.
/// </summary>
internal sealed class PseudoConsoleProcess : IDisposable
{
    private const string ReadyMarker = "Filter activated, GoodbyeDPI is now running!";

    private readonly object _sync = new();
    private readonly StringBuilder _recentOutput = new();

    private IntPtr _pseudoConsole;
    private IntPtr _inputWrite;
    private IntPtr _processHandle;
    private FileStream? _outputStream;
    private Process? _process;
    private volatile bool _suppressExitNotification;
    private volatile bool _disposed;
    private int _exitRaised;
    private IntPtr _outputRead;

    private PseudoConsoleProcess()
    {
    }

    public event Action<string>? OutputReceived;

    public event Action<int>? Exited;

    public int ProcessId { get; private set; }

    public IntPtr ProcessHandle => _processHandle;

    public bool HasExited
    {
        get
        {
            try
            {
                return _process?.HasExited ?? true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public int ExitCode
    {
        get
        {
            try
            {
                return _process?.ExitCode ?? -1;
            }
            catch (InvalidOperationException)
            {
                return -1;
            }
        }
    }

    public static PseudoConsoleProcess Start(string executablePath, string arguments, string workingDirectory)
    {
        var instance = new PseudoConsoleProcess();
        try
        {
            instance.StartCore(executablePath, arguments, workingDirectory);
            return instance;
        }
        catch
        {
            instance.Dispose();
            throw;
        }
    }

    public void BeginReading()
    {
        IntPtr handle;
        lock (_sync)
        {
            handle = _outputStream is not null ? IntPtr.Zero : _outputReadTake();
        }

        if (handle == IntPtr.Zero)
        {
            return;
        }

        var stream = new FileStream(
            new SafeFileHandle(handle, ownsHandle: true),
            FileAccess.Read,
            bufferSize: 4096,
            isAsync: false);

        _outputStream = stream;
        _ = Task.Run(() => ReadLoop(stream));
    }

    public void SuppressExitNotification()
    {
        _suppressExitNotification = true;
    }

    public void Kill()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        catch (Win32Exception)
        {
            // Access is gone with the process.
        }
    }

    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        if (_process is null)
        {
            return true;
        }

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return HasExited;
        }
    }

    public string GetRecentOutput()
    {
        lock (_sync)
        {
            return _recentOutput.ToString();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _suppressExitNotification = true;

        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill();
                _process.WaitForExit(3000);
            }
        }
        catch
        {
            // Best effort during teardown.
        }

        try
        {
            if (_pseudoConsole != IntPtr.Zero)
            {
                NativeMethods.ClosePseudoConsole(_pseudoConsole);
                _pseudoConsole = IntPtr.Zero;
            }
        }
        catch
        {
            // Best effort.
        }

        try
        {
            _outputStream?.Dispose();
        }
        catch
        {
            // Best effort.
        }

        CloseHandleQuietly(_inputWrite);
        _inputWrite = IntPtr.Zero;

        if (_processHandle != IntPtr.Zero)
        {
            CloseHandleQuietly(_processHandle);
            _processHandle = IntPtr.Zero;
        }

        try
        {
            _process?.Dispose();
        }
        catch
        {
            // Best effort.
        }

        _process = null;
    }

    private IntPtr _outputReadTake()
    {
        var handle = _outputRead;
        _outputRead = IntPtr.Zero;
        return handle;
    }

    private void StartCore(string executablePath, string arguments, string workingDirectory)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Engine executable not found.", executablePath);
        }

        var attributes = new NativeMethods.SecurityAttributes
        {
            Length = Marshal.SizeOf<NativeMethods.SecurityAttributes>(),
            InheritHandle = 1
        };

        // Pseudoconsole input pipe.
        if (!NativeMethods.CreatePipe(out var ptyInputRead, out var inputWrite, ref attributes, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        // Pseudoconsole output pipe.
        if (!NativeMethods.CreatePipe(out var outputRead, out var ptyOutputWrite, ref attributes, 0))
        {
            NativeMethods.CloseHandle(ptyInputRead);
            NativeMethods.CloseHandle(inputWrite);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var hr = NativeMethods.CreatePseudoConsole(
            new NativeMethods.Coordinate { X = 160, Y = 32 },
            ptyInputRead,
            ptyOutputWrite,
            0,
            out _pseudoConsole);

        // The pseudoconsole keeps its own references.
        NativeMethods.CloseHandle(ptyInputRead);
        NativeMethods.CloseHandle(ptyOutputWrite);

        if (hr != 0)
        {
            NativeMethods.CloseHandle(outputRead);
            NativeMethods.CloseHandle(inputWrite);
            throw Marshal.GetExceptionForHR(hr) ?? new InvalidOperationException("CreatePseudoConsole failed.");
        }

        _inputWrite = inputWrite;
        _outputRead = outputRead;

        var attributeSize = IntPtr.Zero;
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeSize);
        if (attributeSize == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var attributeList = Marshal.AllocHGlobal(attributeSize);
        try
        {
            if (!NativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeSize))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (!NativeMethods.UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    (IntPtr)NativeMethods.ProcThreadAttributePseudoConsole,
                    _pseudoConsole,
                    (IntPtr)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var startup = new NativeMethods.StartupInfoEx
            {
                StartupInfo = new NativeMethods.StartupInfo
                {
                    Size = Marshal.SizeOf<NativeMethods.StartupInfoEx>()
                },
                AttributeList = attributeList
            };

            var commandLine = "\"" + executablePath + "\" " + arguments;
            if (!NativeMethods.CreateProcess(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    inheritHandles: false,
                    NativeMethods.ExtendedStartupInfoPresent,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startup,
                    out var processInformation))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            ProcessId = processInformation.ProcessId;
            _processHandle = processInformation.Process;
            NativeMethods.CloseHandle(processInformation.Thread);

            try
            {
                _process = Process.GetProcessById(ProcessId);
                _process.EnableRaisingEvents = true;
                _process.Exited += OnProcessExited;
            }
            catch (ArgumentException)
            {
                // The process already terminated. Exit handling below covers it.
                _process = null;
            }
        }
        finally
        {
            NativeMethods.DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
        }

        if (_process is { HasExited: true })
        {
            OnProcessExited(this, EventArgs.Empty);
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _exitRaised, 1) == 1)
        {
            return;
        }

        int exitCode;
        try
        {
            exitCode = _process?.ExitCode ?? -1;
        }
        catch
        {
            exitCode = -1;
        }

        if (_suppressExitNotification || _disposed)
        {
            return;
        }

        try
        {
            Exited?.Invoke(exitCode);
        }
        catch
        {
            // Never propagate event handler failures into the process pump.
        }
    }

    private void ReadLoop(FileStream stream)
    {
        var buffer = new byte[4096];

        try
        {
            while (!_disposed)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                var text = Encoding.UTF8.GetString(buffer, 0, read);
                AppendOutput(text);

                try
                {
                    OutputReceived?.Invoke(text);
                }
                catch
                {
                    // Ignore consumer failures.
                }
            }
        }
        catch (Exception) when (_disposed)
        {
            // Normal teardown.
        }
        catch (IOException)
        {
            // Pipe closed by the engine/host shutdown.
        }
        catch (ObjectDisposedException)
        {
            // Normal teardown.
        }
    }

    private void AppendOutput(string text)
    {
        lock (_sync)
        {
            if (_recentOutput.Length > 16 * 1024)
            {
                _recentOutput.Remove(0, _recentOutput.Length - 8 * 1024);
            }

            _recentOutput.Append(text);
        }
    }

    private static void CloseHandleQuietly(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            NativeMethods.CloseHandle(handle);
        }
        catch
        {
            // Best effort.
        }
    }
}
