using System.Runtime.InteropServices;
using System.Security;

namespace ViciOne.SystemMonitoring;

[SuppressUnmanagedCodeSecurity]
internal static partial class UnsafeNativeMethods
{
    private const int ClockTicksPerSecond = 2; // _SC_CLK_TCK

    [LibraryImport("libc.so.6", EntryPoint = "sysconf")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.UserDirectories)]
    private static partial long Sysconf(int name);

    internal static int GetClockTicks()
        => (int)Sysconf(ClockTicksPerSecond);
}
