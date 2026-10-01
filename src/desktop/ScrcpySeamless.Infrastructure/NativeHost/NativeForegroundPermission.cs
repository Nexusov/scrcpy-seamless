using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScrcpySeamless.Infrastructure.NativeHost;

/// <summary>Delegates Windows foreground permission only to the retained, still-live owned child.</summary>
internal static class NativeForegroundPermission
{
    /// <summary>Attempts the supported handoff; denial does not replace the native Focus result.</summary>
    internal static bool TryGrant(Process ownedProcess)
    {
        if (!OperatingSystem.IsWindows() || ownedProcess.HasExited)
        {
            return false;
        }

        // Retain the process handle across the PID-based call, including concurrent child exit.
        _ = ownedProcess.Handle;
        bool granted = AllowSetForegroundWindow((uint)ownedProcess.Id);
        GC.KeepAlive(ownedProcess);
        return granted;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
