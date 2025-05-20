#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Duplicati.Library.Utility;

/// <summary>
/// Collection of helper methods to check for permissions.
/// </summary>
public static class PermissionHelper
{
    /// <summary>
    /// Checks if the current process has the Backup Privilege.
    /// </summary>
    /// <returns>True if has Backup Privilege, false otherwise.</returns>
    public static bool HasSeBackupPrivilege()
    {
        if (OperatingSystem.IsWindows())
            try { return HasBackupPrivilegeClaim() && PrivilegeChecker.IsPrivilegeEnabled("SeBackupPrivilege"); }
            catch (DllNotFoundException)
            {
                // If the function is not available, we assume the privilege is not present.
            }

        return false;
    }

    /// <summary>
    /// Check if the current process is running as an administrator, LocalSystem or root.
    /// </summary>
    /// <returns>True if running as administrator, LocalSystem or root, false otherwise.</returns>
    public static bool IsRunningAsAdministratorOrLocalSystem()
    {
        if (OperatingSystem.IsWindows())
            return IsRunningAsAdministrator() || IsRunningAsLocalSystem();
        if (OperatingSystem.IsLinux())
            return IsRunningAsRoot();

        return false;
    }

    /// <summary>
    /// Check if the current process is running as an administrator.
    /// </summary>
    /// <returns>True if running as administrator, false otherwise.</returns>
    [SupportedOSPlatform("windows")]
    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Check if the current process is running as LocalSystem.
    /// </summary>
    /// <returns>True if running as LocalSystem, false otherwise.</returns>
    [SupportedOSPlatform("windows")]
    private static bool IsRunningAsLocalSystem()
    {
        using (var identity = WindowsIdentity.GetCurrent())
            return identity.User?.Value == "S-1-5-18"; // SID for LocalSystem
    }

    /// <summary>
    /// Check if the current process has the Backup Privilege.
    /// </summary>
    /// <returns>True if has Backup Privilege, false otherwise.</returns>
    [SupportedOSPlatform("windows")]
    private static bool HasBackupPrivilegeClaim()
    {
        using (var identity = WindowsIdentity.GetCurrent())
            foreach (var claim in identity.Claims)
                if (claim.Value.Contains("SeBackupPrivilege"))
                    return true;

        return false;
    }

    /// <summary>
    /// Gets the effective user ID of the calling process.
    /// </summary>
    /// <returns>The effective user ID of the calling process.</returns>
    [SupportedOSPlatform("linux")]
    [DllImport("libc")]
    public static extern uint geteuid();

    /// <summary>
    /// Check if the current process is running as root (user ID 0).
    /// </summary>
    /// <returns>True if running as root, false otherwise.</returns>
    [SupportedOSPlatform("linux")]
    private static bool IsRunningAsRoot()
    {
        return geteuid() == 0;
    }

    /// <summary>
    /// Helper class for checking Windows privileges.
    /// </summary>
    private class PrivilegeChecker
    {
        /// <summary>
        /// Constants for token access rights.
        /// </summary>
        const int TOKEN_QUERY = 0x0008;
        /// <summary>
        /// Constant for privilege enabled attribute.
        /// </summary>
        const int SE_PRIVILEGE_ENABLED = 0x00000002;

        /// <summary>
        /// Structure that contains a locally unique identifier (LUID).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        struct LUID
        {
            public uint LowPart;
            public int HighPart;

            /// <inheritdoc/>
            public override bool Equals(object? obj)
            {
                return obj is LUID other &&
                       LowPart == other.LowPart &&
                       HighPart == other.HighPart;
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return HashCode.Combine(LowPart, HighPart);
            }
        }

        /// <summary>
        /// Structure that contains a locally unique identifier (LUID) and its attributes.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        struct LUID_AND_ATTRIBUTES
        {
            /// <summary>
            /// The locally unique identifier (LUID) for the privilege.
            /// </summary>
            public LUID Luid;
            /// <summary>
            /// The attributes for the privilege.
            /// </summary>
            public int Attributes;
        }

        /// <summary>
        /// Structure that contains the privileges associated with a token.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        struct TOKEN_PRIVILEGES
        {
            /// <summary>
            /// The number of privileges in the Privileges array.
            /// </summary>
            public int PrivilegeCount;
            /// <summary>
            /// The privileges associated with the token.
            /// </summary>
            /// <remarks>This field is actually an array of ANY size — we handle this dynamically</remarks>
            public LUID_AND_ATTRIBUTES Privileges;
        }

        /// <summary>
        /// Opens the access token associated with a process.
        /// </summary>
        /// <param name="ProcessHandle">The handle of the process whose access token is to be opened.</param>
        /// <param name="DesiredAccess">The access rights to the token.</param>
        /// <param name="TokenHandle"> A pointer to a variable that receives the handle to the opened access token.</param>
        /// <returns><c>true</c> if the function succeeds; otherwise, <c>false</c>.</returns>
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr ProcessHandle, int DesiredAccess, out IntPtr TokenHandle);

        /// <summary>
        /// Retrieves a handle to the access token associated with the current process.
        /// /// </summary>
        /// <returns>A handle to the access token associated with the current process.</returns>        
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        /// <summary>
        /// Retrieves a specified type of information about an access token.
        /// </summary>
        /// <param name="TokenHandle">A handle to the access token from which information is being retrieved.</param>
        /// <param name="TokenInformationClass">The type of information to be retrieved.</param>
        /// <param name="TokenInformation">A pointer to a buffer that receives the requested information.</param>
        /// <param name="TokenInformationLength">The size of the buffer pointed to by the TokenInformation parameter.</param>
        /// <param name="ReturnLength">A pointer to a variable that receives the size of the data returned in the TokenInformation buffer.</param>
        /// <returns><c>true</c> if the function succeeds; otherwise, <c>false</c>.</returns>
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(
            IntPtr TokenHandle,
            int TokenInformationClass,
            IntPtr TokenInformation,
            int TokenInformationLength,
            out int ReturnLength
        );

        /// <summary>
        /// Retrieves the locally unique identifier (LUID) for a specified privilege name.
        /// </summary>
        /// <param name="lpSystemName">The name of the system on which the privilege is being looked up. This parameter can be null.</param>
        /// <param name="lpName">The name of the privilege to look up. This parameter cannot be null.</param>
        /// <param name="lpLuid">A pointer to a LUID structure that receives the LUID for the specified privilege.</param>
        /// <returns><c>true</c> if the function succeeds; otherwise, <c>false</c>.</returns>
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

        /// <summary>
        /// Determines whether the current process token has the specified Windows privilege enabled.
        /// </summary>
        /// <param name="privilegeName">
        /// The name of the privilege to check (e.g., "SeBackupPrivilege", "SeRestorePrivilege").
        /// </param>
        /// <returns>
        /// <c>true</c> if the privilege is present and enabled in the current process token; otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>
        /// This function does not attempt to enable the privilege, it only verifies whether it is enabled.
        /// Requires that the calling process has permission to query its own token.
        /// </remarks>
        public static bool IsPrivilegeEnabled(string privilegeName)
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out IntPtr tokenHandle))
                return false;

            if (!LookupPrivilegeValue(null, privilegeName, out LUID targetLuid))
                return false;

            // First call to get required buffer size
            GetTokenInformation(tokenHandle, 3, IntPtr.Zero, 0, out int bufferSize); // TokenPrivileges = 3
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

            try
            {
                if (!GetTokenInformation(tokenHandle, 3, buffer, bufferSize, out _))
                    return false;

                int count = Marshal.ReadInt32(buffer);
                IntPtr luidPtr = buffer + sizeof(int);
                int structSize = Marshal.SizeOf<LUID_AND_ATTRIBUTES>();

                for (int i = 0; i < count; i++)
                {
                    LUID_AND_ATTRIBUTES laa = Marshal.PtrToStructure<LUID_AND_ATTRIBUTES>(luidPtr + i * structSize);
                    if (laa.Luid.Equals(targetLuid))
                    {
                        return (laa.Attributes & SE_PRIVILEGE_ENABLED) != 0;
                    }
                }
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}