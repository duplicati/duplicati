// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

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
            try
            {
                return PrivilegeChecker.TryEnablePrivilege("SeBackupPrivilege");
            }
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
        /// Constant for adjusting token privileges
        /// </summary>
        const int TOKEN_ADJUST_PRIVILEGES = 0x0020;
        /// <summary>
        /// Constants for token access rights.
        /// </summary>
        const int TOKEN_QUERY = 0x0008;
        /// <summary>
        /// Constant for privilege enabled attribute.
        /// </summary>
        const int SE_PRIVILEGE_ENABLED = 0x00000002;
        /// <summary>
        /// The TOKEN_INFORMATION_CLASS.TokenPrivileges value
        /// </summary>
        const int TOKEN_INFORMATION_TOKEN_PRIVILEGES = 3;

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
        /// Adjusts the privileges of a specified access token.
        /// </summary>
        /// <param name="TokenHandle">A handle to the access token whose privileges are to be adjusted.</param>
        /// <param name="DisableAllPrivileges">Specifies whether all privileges are to be disabled.</param>
        /// <param name="NewState">A pointer to a TOKEN_PRIVILEGES structure that specifies the new privileges.</param>
        /// <param name="BufferLength">The size of the buffer pointed to by the PreviousState parameter.</param>
        /// <param name="PreviousState">A pointer to a buffer that receives the previous state of the privileges.</param>
        /// <param name="ReturnLength">A pointer to a variable that receives the size of the data returned in the PreviousState buffer.</param>
        /// <returns><c>true</c> if the function succeeds; otherwise, <c>false</c>.</returns>
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool AdjustTokenPrivileges(
            IntPtr TokenHandle,
            bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState,
            int BufferLength,
            IntPtr PreviousState,
            out uint ReturnLength
        );

        /// <summary>
        /// Retrieves the calling thread's last-error code value.
        /// </summary>
        /// <returns>The last-error code value.</returns>
        [DllImport("kernel32.dll")]
        static extern uint GetLastError();

        /// <summary>
        /// Closes an open object handle.
        /// </summary>
        /// <param name="hObject">The handle to the object to be closed.</param>
        /// <returns><c>true</c> if the function succeeds; otherwise, <c>false</c>.</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        /// <summary>
        /// Flag tracking if backup privilege is enabled
        /// </summary>
        private static bool? _isPrivilegeEnabled;

        /// <summary>
        /// Tries to enable a specific privilege in the current process token.
        /// </summary>
        /// <param name="privilegeName">The name of the privilege (e.g., "SeBackupPrivilege").</param>
        /// <returns>
        /// True if the privilege is present and was successfully enabled.
        /// False if the privilege is not held or could not be enabled.
        /// </returns>
        public static bool TryEnablePrivilege(string privilegeName)
        {
            if (_isPrivilegeEnabled.HasValue)
                return _isPrivilegeEnabled.Value;

            _isPrivilegeEnabled = false;

            // Open the primary token for this process
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var hTok))
                return false;

            try
            {
                // Translate the textual privilege name (e.g. "SeBackupPrivilege") to a LUID
                if (!LookupPrivilegeValue(null, privilegeName, out var luid))
                    return false;

                // Build TOKEN_PRIVILEGES {1 entry, SE_PRIVILEGE_ENABLED}
                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = SE_PRIVILEGE_ENABLED
                    }
                };

                // Query the size of the current privileges set
                GetTokenInformation(hTok, TOKEN_INFORMATION_TOKEN_PRIVILEGES, IntPtr.Zero, 0, out int requiredLen);

                // Allocate a buffer of the requested size
                var prevStatePtr = Marshal.AllocHGlobal(requiredLen);

                try
                {
                    // Enable the privilege, capturing the previous state
                    if (!AdjustTokenPrivileges(hTok, false, ref tp, requiredLen, prevStatePtr, out var _))
                        return false;

                    // If GetLastError() == ERROR_SUCCESS (0), the privilege is now enabled
                    _isPrivilegeEnabled = Marshal.GetLastWin32Error() == 0;

                    // Restore the original state
                    var oldState = Marshal.PtrToStructure<TOKEN_PRIVILEGES>(prevStatePtr);
                    AdjustTokenPrivileges(hTok, false, ref oldState, 0, IntPtr.Zero, out var _);
                }
                finally
                {
                    Marshal.FreeHGlobal(prevStatePtr);
                }

                return _isPrivilegeEnabled.Value;
            }
            finally
            {
                CloseHandle(hTok);
            }
        }

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
                        return (laa.Attributes & SE_PRIVILEGE_ENABLED) != 0;
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