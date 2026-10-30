using System.IO.Pipes;
using System.Text;
using BeeDPI.Core.Ipc;

namespace BeeDPI.EngineHost;

/// <summary>
/// Private, per-launch control channel. The pipe name is a random GUID created
/// by the UI and protected with <c>PipeOptions.CurrentUserOnly</c>.
/// </summary>
internal sealed class HostChannel : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private HostChannel(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _reader = new StreamReader(pipe, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        _writer = new StreamWriter(pipe, encoding, bufferSize: 4096, leaveOpen: true)
        {
            AutoFlush = false
        };
    }

    public static async Task<HostChannel?> ConnectAsync(
        string pipeName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(250, cancellationToken).ConfigureAwait(false);
                return new HostChannel(pipe);
            }
            catch (TimeoutException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        return null;
    }

    public async Task SendAsync(HostMessage message, CancellationToken cancellationToken = default)
    {
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

    public async Task<HostMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await HostProtocol.ReceiveAsync(_reader, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _writer.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            // Channel already gone.
        }

        _writer.Dispose();
        _reader.Dispose();
        await _pipe.DisposeAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }
}
