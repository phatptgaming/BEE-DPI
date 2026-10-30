using BeeDPI.Core.Dns;
using BeeDPI.Core.Ipc;
using BeeDPI.EngineHost.Diagnostics;

namespace BeeDPI.EngineHost;

internal static class Program
{
    private const string MutexName = @"Global\BeeDPI.Engine.v1";
    private const string DnsMutexName = @"Global\BeeDPI.Dns.v1";

    private static async Task<int> Main(string[] args)
    {
        var options = HostOptions.Parse(args);
        if (options is null)
        {
            return 10;
        }

        HostLog.Start();

        if (options.Mode == HostProtocol.ModeDns)
        {
            return await RunDnsAsync(options).ConfigureAwait(false);
        }

        using var mutex = new Mutex(initiallyOwned: false, MutexName);
        var ownsMutex = false;
        try
        {
            ownsMutex = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        if (!ownsMutex)
        {
            HostLog.Warn("another engine host is already running");
            await using var rejected = await HostChannel
                .ConnectAsync(options.PipeName, TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);

            if (rejected is not null)
            {
                await rejected.SendAsync(HostProtocol.ErrorMessage(
                    "ALREADY_RUNNING",
                    "Engine đang được sử dụng bởi một phiên BEE DPI khác.")).ConfigureAwait(false);
            }

            return 3;
        }

        try
        {
            await using var supervisor = new EngineSupervisor(AppContext.BaseDirectory);
            await using var channel = await HostChannel
                .ConnectAsync(options.PipeName, TimeSpan.FromSeconds(25))
                .ConfigureAwait(false);

            if (channel is null)
            {
                HostLog.Warn("could not connect to the BEE DPI control pipe");
                return 4;
            }

            _ = ParentWatchdog.Start(options.ParentProcessId, supervisor);
            supervisor.EngineExited += code => OnEngineExited(channel, code);

            while (true)
            {
                var message = await channel.ReceiveAsync().ConfigureAwait(false);
                if (message is null)
                {
                    HostLog.Info("control pipe closed");
                    break;
                }

                switch ((message.Type ?? string.Empty).ToLowerInvariant())
                {
                    case HostProtocol.Start:
                    {
                        var result = await supervisor.StartAsync().ConfigureAwait(false);
                        if (result.Success)
                        {
                            await channel.SendAsync(HostProtocol.Ready(result.Pid)).ConfigureAwait(false);
                        }
                        else
                        {
                            await channel.SendAsync(HostProtocol.ErrorMessage(result.Code, result.FriendlyMessage))
                                .ConfigureAwait(false);
                        }

                        break;
                    }

                    case HostProtocol.Stop:
                    {
                        var result = await supervisor.StopAsync().ConfigureAwait(false);
                        if (result.Success)
                        {
                            await channel.SendAsync(HostProtocol.StoppedMessage()).ConfigureAwait(false);
                        }
                        else
                        {
                            await channel.SendAsync(HostProtocol.ErrorMessage(
                                "ENGINE_STOP_FAILED",
                                "Không thể dừng engine an toàn.")).ConfigureAwait(false);
                        }

                        break;
                    }

                    case HostProtocol.Status:
                    {
                        if (supervisor.IsRunning)
                        {
                            await channel.SendAsync(HostProtocol.Running(supervisor.ProcessId ?? 0)).ConfigureAwait(false);
                        }
                        else
                        {
                            await channel.SendAsync(HostProtocol.StoppedMessage()).ConfigureAwait(false);
                        }

                        break;
                    }

                    case HostProtocol.Shutdown:
                    {
                        await supervisor.StopAsync().ConfigureAwait(false);
                        HostLog.Info("shutdown requested");
                        return 0;
                    }

                    default:
                        HostLog.Warn("unknown command: " + message.Type);
                        break;
                }
            }

            await supervisor.StopAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            HostLog.Error("fatal host error", exception);
            return 1;
        }
    }

    private static async Task<int> RunDnsAsync(HostOptions options)
    {
        using var mutex = new Mutex(initiallyOwned: false, DnsMutexName);
        var ownsMutex = false;
        try
        {
            ownsMutex = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        if (!ownsMutex)
        {
            HostLog.Warn("another DNS host is already running");
            await using var rejected = await HostChannel
                .ConnectAsync(options.PipeName, TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);

            if (rejected is not null)
            {
                await rejected.SendAsync(HostProtocol.ErrorMessage(
                    "DNS_BUSY",
                    "Một thao tác DNS khác đang chạy. Vui lòng thử lại.")).ConfigureAwait(false);
            }

            return 3;
        }

        try
        {
            await using var channel = await HostChannel
                .ConnectAsync(options.PipeName, TimeSpan.FromSeconds(25))
                .ConfigureAwait(false);

            if (channel is null)
            {
                HostLog.Warn("could not connect to the BEE DPI control pipe (dns mode)");
                return 4;
            }

            // Deliberately no parent watchdog here: if the window disappears while
            // an apply/restore is running, the operation must be allowed to finish
            // (and roll back on failure) before the host exits.
            await channel.SendAsync(HostProtocol.Ready(Environment.ProcessId)).ConfigureAwait(false);
            HostLog.Info("dns host ready, pid=" + Environment.ProcessId);

            var platform = new ScriptDnsPlatform(
                AppContext.BaseDirectory,
                message => HostLog.Info("dns: " + message));

            while (true)
            {
                var message = await channel.ReceiveAsync().ConfigureAwait(false);
                if (message is null)
                {
                    HostLog.Info("dns control pipe closed");
                    break;
                }

                switch ((message.Type ?? string.Empty).ToLowerInvariant())
                {
                    case HostProtocol.Dns:
                    {
                        var action = (message.Code ?? string.Empty).ToLowerInvariant();
                        if (action is not (HostProtocol.DnsApplyAction or HostProtocol.DnsRestoreAction))
                        {
                            await channel.SendAsync(HostProtocol.ErrorMessage(
                                "DNS_ACTION_INVALID",
                                "Hành động DNS không hợp lệ.")).ConfigureAwait(false);
                            break;
                        }

                        try
                        {
                            var resultJson = await platform
                                .ExecuteRawAsync(message.Payload ?? "{}")
                                .ConfigureAwait(false);
                            await channel.SendAsync(HostProtocol.DnsResponse(resultJson)).ConfigureAwait(false);
                        }
                        catch (DnsOperationException exception)
                        {
                            HostLog.Warn("dns " + action + " failed: " + exception.Code + " " + exception.Message);
                            await channel.SendAsync(HostProtocol.ErrorMessage(
                                exception.Code,
                                exception.Message)).ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            HostLog.Error("dns " + action + " failed", exception);
                            await channel.SendAsync(HostProtocol.ErrorMessage(
                                "DNS_FAILED",
                                "Không thể xử lý DNS.")).ConfigureAwait(false);
                        }

                        break;
                    }

                    case HostProtocol.Shutdown:
                    {
                        HostLog.Info("dns host shutdown requested");
                        return 0;
                    }

                    default:
                        HostLog.Warn("unknown dns command: " + message.Type);
                        break;
                }
            }

            return 0;
        }
        catch (Exception exception)
        {
            HostLog.Error("fatal dns host error", exception);
            return 1;
        }
    }

    private static void OnEngineExited(HostChannel channel, int exitCode)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await channel.SendAsync(HostProtocol.Faulted("Engine đã dừng đột ngột.")).ConfigureAwait(false);
            }
            catch
            {
                // UI may already be gone.
            }

            await Task.Delay(250).ConfigureAwait(false);
            Environment.Exit(0);
        });
    }
}
