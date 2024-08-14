using System;

#if NETSTANDARD
using System.Runtime.InteropServices;
using RuntimeEnvironment = Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment;
#endif

namespace DeviceId.Internal;

/// <summary>
/// Provides helper methods relating to the OS.
/// </summary>
internal static class OS
{
    // ToDo: Add SupportedOSPlatformGuardAttribute to these methods so that the CA1416 warning goes away.

    /// <summary>
    /// Gets a value indicating whether this is a Windows OS.
    /// </summary>
    public static bool IsWindows { get; }
#if NETFRAMEWORK
        = true;
#elif NETSTANDARD
        = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#elif NET5_0_OR_GREATER
        = OperatingSystem.IsWindows();
#endif

    /// <summary>
    /// Gets a value indicating whether this is a Linux OS.
    /// </summary>
    public static bool IsLinux { get; }
#if NETFRAMEWORK
        = false;
#elif NETSTANDARD
        = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
#elif NET5_0_OR_GREATER
        = OperatingSystem.IsLinux();
#endif

    /// <summary>
    /// Gets a value indicating whether this is Mac OS.
    /// </summary>
    public static bool IsMacOS { get; }
#if NETFRAMEWORK
        = false;
#elif NETSTANDARD
        = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
#elif NET5_0_OR_GREATER
        = OperatingSystem.IsMacOS();
#endif

    /// <summary>
    /// Gets the current OS version.
    /// </summary>
    public static string Version { get; }
#if (NETFRAMEWORK || NET5_0_OR_GREATER)
        = Environment.OSVersion.ToString();
#elif NETSTANDARD
        = IsWindows
            ? Environment.OSVersion.ToString()
            : string.Concat(RuntimeEnvironment.OperatingSystem, " ", RuntimeEnvironment.OperatingSystemVersion);
#endif
}
