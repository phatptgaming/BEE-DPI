using System.Runtime.InteropServices;

namespace BeeDPI.EngineHost.Native;

/// <summary>
/// A kill-on-close job object. If the elevated host process terminates for any
/// reason, Windows terminates the engine process too, so it can never be
/// orphaned.
/// </summary>
internal sealed class JobObject : IDisposable
{
    private const uint JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    private IntPtr _handle;

    public JobObject()
    {
        _handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var info = new NativeMethods.JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new NativeMethods.JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };

        var size = Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            NativeMethods.SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, buffer, (uint)size);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public bool Assign(IntPtr processHandle)
    {
        if (_handle == IntPtr.Zero || processHandle == IntPtr.Zero)
        {
            return false;
        }

        return NativeMethods.AssignProcessToJobObject(_handle, processHandle);
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.CloseHandle(_handle);
        _handle = IntPtr.Zero;
    }
}
