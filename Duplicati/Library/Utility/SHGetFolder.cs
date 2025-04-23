using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

#nullable enable

namespace Duplicati.Library.Utility;
/// <summary>
/// Wrapper for SHGetKnownFolderPath to various system folders on Windows
/// </summary>
/// <remarks>List of GUIDs: https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid</remarks>
[SupportedOSPlatform("windows")]
public static class SHGetFolder
{
    /// <summary>
    /// Gets the download folder path
    /// </summary>
    public static string? DownloadFolder => GetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"));

    /// <summary>
    /// Gets the user profiles folder path
    /// </summary>
    public static string? UserProfilesFolder
    {
        get
        {
            try
            {
                // Prefered way, works on Windows Vista and later
                var shPath = GetKnownFolderPath(new Guid("0762D272-C50A-4BB0-A382-697DCD729B80"));
                if (!string.IsNullOrWhiteSpace(shPath) && Path.IsPathRooted(shPath) && Directory.Exists(shPath))
                    return shPath;
            }
            catch
            {
            }

            try
            {
                // Fallback to "{systemDrive}\\Users"
                var envPath = Environment.ExpandEnvironmentVariables("%SystemDrive%\\Users");
                if (!string.IsNullOrWhiteSpace(envPath) && Path.IsPathRooted(envPath) && Directory.Exists(envPath))
                    return envPath;
            }
            catch
            {
            }

            try
            {
                // Fallback to "{systemDrive}\\Users", method 2            
                var sysPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (!string.IsNullOrWhiteSpace(sysPath) && Path.IsPathRooted(sysPath) && Directory.Exists(sysPath))
                {
                    var sysDir = Path.GetPathRoot(sysPath);
                    if (!string.IsNullOrWhiteSpace(sysDir))
                        return Path.Combine(sysDir, "Users");
                }
            }
            catch
            {
            }

            // Most likely, this is the correct path
            var path = "C:\\Users";
            if (Directory.Exists(path))
                return path;

            return null;
        }
    }

    /// <summary>
    /// SHGetKnownFolderPath function to get the folder path
    /// </summary>
    /// <param name="rfid">The folder GUID</param>
    /// <param name="dwFlags">Get folder flags</param>
    /// <param name="hToken">The access token</param>
    /// <param name="ppszPath">The folder path</param>
    /// <returns>The HRESULT error code</returns>
    [DllImport("Shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHGetKnownFolderPath(
          [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
          uint dwFlags,
          IntPtr hToken,
          out IntPtr ppszPath);

    /// <summary>
    /// Gets the folder path using SHGetKnownFolderPath
    /// </summary>
    /// <param name="folderGuid">The folder GUID</param>
    /// <returns>The folder path</returns>
    private static string? GetKnownFolderPath(Guid folderGuid)
    {
        var result = SHGetKnownFolderPath(folderGuid, 0, IntPtr.Zero, out var outPath);
        if (result != 0)
            Marshal.ThrowExceptionForHR(result); // Throws an exception for the HRESULT error code

        var path = Marshal.PtrToStringUni(outPath);
        Marshal.FreeCoTaskMem(outPath); // Free the memory allocated by SHGetKnownFolderPath
        return path;
    }
}
