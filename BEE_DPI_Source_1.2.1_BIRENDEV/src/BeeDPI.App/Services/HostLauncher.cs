using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace BeeDPI.App.Services;

internal static class HostLauncher
{
    public static Process Launch(string pipeName) =>
        Launch(pipeName, "--pipe \"{0}\" --parent {1}");

    public static Process LaunchDns(string pipeName) =>
        Launch(pipeName, "--mode dns --pipe \"{0}\" --parent {1}");

    private static Process Launch(string pipeName, string argumentFormat)
    {
        var hostPath = Path.Combine(AppContext.BaseDirectory, "BeeDPI.EngineHost.exe");
        if (!File.Exists(hostPath))
        {
            throw new EngineSessionException("Thiếu thành phần điều khiển. Vui lòng cài đặt lại BEE DPI.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = hostPath,
            Arguments = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                argumentFormat,
                pipeName,
                Environment.ProcessId),
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            return Process.Start(startInfo)
                   ?? throw new EngineSessionException("Không thể khởi động thành phần điều khiển.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user declined the Windows UAC prompt.
            throw new ElevationCancelledException();
        }
        catch (Win32Exception exception)
        {
            throw new EngineSessionException(
                "Không thể yêu cầu quyền quản trị. Vui lòng thử lại.",
                "ELEVATION_FAILED",
                exception);
        }
    }
}
