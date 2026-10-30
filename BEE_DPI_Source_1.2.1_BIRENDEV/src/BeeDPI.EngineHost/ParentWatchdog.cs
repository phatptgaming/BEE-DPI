using System.Diagnostics;

namespace BeeDPI.EngineHost;

internal static class ParentWatchdog
{
    /// <summary>
    /// Stops the engine if the BEE DPI window disappears for any reason,
    /// including a crash or a forced termination.
    /// </summary>
    public static IDisposable? Start(int parentProcessId, EngineSupervisor supervisor)
    {
        try
        {
            var parent = Process.GetProcessById(parentProcessId);
            var thread = new Thread(() =>
            {
                try
                {
                    parent.WaitForExit();
                }
                catch
                {
                    // Parent already gone.
                }

                try
                {
                    supervisor.EmergencyStop();
                }
                catch
                {
                    // Best effort.
                }

                Environment.Exit(0);
            })
            {
                IsBackground = true,
                Name = "BeeDPI.ParentWatchdog"
            };

            thread.Start();
            return parent;
        }
        catch (ArgumentException)
        {
            // Parent vanished before the host started: nothing to supervise.
            return null;
        }
    }
}
