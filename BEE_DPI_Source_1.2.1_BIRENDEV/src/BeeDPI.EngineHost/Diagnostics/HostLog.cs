using System.Text;

namespace BeeDPI.EngineHost.Diagnostics;

internal static class HostLog
{
    private static readonly object Sync = new();
    private static string? _path;

    public static void Start()
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BeeDPI",
                "logs");
            Directory.CreateDirectory(folder);
            _path = Path.Combine(folder, "engine-host.log");
            Write("host started (pid=" + Environment.ProcessId + ")");
        }
        catch
        {
            _path = null;
        }
    }

    public static void Info(string message) => Write("INFO  " + message);

    public static void Warn(string message) => Write("WARN  " + message);

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR " + message);
        if (exception is not null)
        {
            Write(exception.GetType().Name + ": " + exception.Message);
        }
    }

    private static void Write(string line)
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            lock (Sync)
            {
                var entry = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + "  " + line + Environment.NewLine;
                File.AppendAllText(_path, entry, Encoding.UTF8);

                var info = new FileInfo(_path);
                if (info.Exists && info.Length > 512 * 1024)
                {
                    var trimmed = File.ReadAllLines(_path).TakeLast(2000);
                    File.WriteAllLines(_path, trimmed, Encoding.UTF8);
                }
            }
        }
        catch
        {
            // Logging must never take the host down.
        }
    }
}
