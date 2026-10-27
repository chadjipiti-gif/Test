using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Copilot.Core;
namespace Copilot.App;
public static class WindowPrivacy
{
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    public static void TryExclude(Window window, Diagnostics log)
    {
        try { bool ok = SetWindowDisplayAffinity(new WindowInteropHelper(window).Handle, 0x11); log.Event("privacy", ok ? "capture_exclusion_enabled" : "capture_exclusion_unavailable", new { code = ok ? 0 : Marshal.GetLastWin32Error() }); } catch (Exception ex) { log.Event("privacy", "capture_exclusion_unavailable", new { error = ex.GetType().Name }); }
    }
}
