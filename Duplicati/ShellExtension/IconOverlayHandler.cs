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

using System.Runtime.InteropServices;

namespace Duplicati.ShellExtension;

/// <summary>
/// Windows Shell Icon Overlay Handler for showing Duplicati backup status on folders.
/// This handler displays overlay icons on folders that are included in Duplicati backups,
/// similar to how cloud storage solutions show sync status.
/// </summary>
[ComVisible(true)]
[Guid("E4B5F8A3-9C1D-4F2E-B6A7-8D3C5E6F7A9B")]
[ClassInterface(ClassInterfaceType.None)]
public class DuplicatiBackedUpOverlay : IconOverlayHandlerBase
{
    /// <summary>
    /// The icon file name for successfully backed up folders
    /// </summary>
    protected override string IconFileName => "overlay_backed_up.ico";

    /// <summary>
    /// Priority determines the order of overlay handlers (lower = higher priority)
    /// </summary>
    protected override int Priority => 10;

    /// <summary>
    /// Determines if this overlay should be shown for the given path
    /// </summary>
    protected override bool ShouldShowOverlay(string path, FolderBackupStatus status)
    {
        return status == FolderBackupStatus.BackedUp;
    }
}

/// <summary>
/// Overlay handler for folders with backup warnings
/// </summary>
[ComVisible(true)]
[Guid("E4B5F8A3-9C1D-4F2E-B6A7-8D3C5E6F7A9C")]
[ClassInterface(ClassInterfaceType.None)]
public class DuplicatiWarningOverlay : IconOverlayHandlerBase
{
    /// <summary>
    /// The icon file name for folders with backup warnings
    /// </summary>
    protected override string IconFileName => "overlay_warning.ico";

    /// <summary>
    /// Priority for warning overlay
    /// </summary>
    protected override int Priority => 11;

    /// <summary>
    /// Determines if this overlay should be shown for the given path
    /// </summary>
    protected override bool ShouldShowOverlay(string path, FolderBackupStatus status)
    {
        return status == FolderBackupStatus.BackedUpWithWarning;
    }
}

/// <summary>
/// Overlay handler for folders with backup errors
/// </summary>
[ComVisible(true)]
[Guid("E4B5F8A3-9C1D-4F2E-B6A7-8D3C5E6F7A9D")]
[ClassInterface(ClassInterfaceType.None)]
public class DuplicatiErrorOverlay : IconOverlayHandlerBase
{
    /// <summary>
    /// The icon file name for folders with backup errors
    /// </summary>
    protected override string IconFileName => "overlay_error.ico";

    /// <summary>
    /// Priority for error overlay
    /// </summary>
    protected override int Priority => 12;

    /// <summary>
    /// Determines if this overlay should be shown for the given path
    /// </summary>
    protected override bool ShouldShowOverlay(string path, FolderBackupStatus status)
    {
        return status == FolderBackupStatus.BackupFailed;
    }
}

/// <summary>
/// Overlay handler for folders with backup in progress
/// </summary>
[ComVisible(true)]
[Guid("E4B5F8A3-9C1D-4F2E-B6A7-8D3C5E6F7A9E")]
[ClassInterface(ClassInterfaceType.None)]
public class DuplicatiSyncingOverlay : IconOverlayHandlerBase
{
    /// <summary>
    /// The icon file name for folders with backup in progress
    /// </summary>
    protected override string IconFileName => "overlay_syncing.ico";

    /// <summary>
    /// Priority for syncing overlay
    /// </summary>
    protected override int Priority => 9;

    /// <summary>
    /// Determines if this overlay should be shown for the given path
    /// </summary>
    protected override bool ShouldShowOverlay(string path, FolderBackupStatus status)
    {
        return status == FolderBackupStatus.BackupInProgress;
    }
}

/// <summary>
/// Base class for Duplicati icon overlay handlers
/// </summary>
public abstract class IconOverlayHandlerBase : IShellIconOverlayIdentifier
{
    private static readonly Lazy<DuplicatiClient> Client = new(() => new DuplicatiClient());

    /// <summary>
    /// The icon file name to use for this overlay
    /// </summary>
    protected abstract string IconFileName { get; }

    /// <summary>
    /// Priority of this overlay handler
    /// </summary>
    protected abstract int Priority { get; }

    /// <summary>
    /// Determines if this overlay should be shown for the given path and status
    /// </summary>
    protected abstract bool ShouldShowOverlay(string path, FolderBackupStatus status);

    /// <summary>
    /// Gets the overlay icon information
    /// </summary>
    public int GetOverlayInfo(IntPtr pwszIconFile, int cchMax, out int pIndex, out uint pdwFlags)
    {
        pIndex = 0;
        pdwFlags = ISIOI_ICONFILE;

        var iconPath = GetIconPath();
        if (iconPath.Length < cchMax)
        {
            Marshal.Copy(iconPath.ToCharArray(), 0, pwszIconFile, iconPath.Length);
            Marshal.WriteInt16(pwszIconFile, iconPath.Length * 2, 0);
            return S_OK;
        }

        return S_FALSE;
    }

    /// <summary>
    /// Gets the priority of this overlay handler
    /// </summary>
    public int GetPriority(out int pPriority)
    {
        pPriority = Priority;
        return S_OK;
    }

    /// <summary>
    /// Determines if the overlay should be shown for the specified path
    /// </summary>
    public int IsMemberOf(string pwszPath, uint dwAttrib)
    {
        try
        {
            // Only show overlay for directories
            if ((dwAttrib & FILE_ATTRIBUTE_DIRECTORY) == 0)
                return S_FALSE;

            // Skip system folders
            if (IsSystemFolder(pwszPath))
                return S_FALSE;

            // Get the folder status from Duplicati
            var statusTask = Client.Value.GetFolderStatusAsync(pwszPath);

            // Use a short timeout to avoid blocking Explorer
            if (!statusTask.Wait(TimeSpan.FromMilliseconds(100)))
                return S_FALSE;

            var statusInfo = statusTask.Result;
            return ShouldShowOverlay(pwszPath, statusInfo.Status) ? S_OK : S_FALSE;
        }
        catch
        {
            return S_FALSE;
        }
    }

    /// <summary>
    /// Gets the full path to the overlay icon
    /// </summary>
    private string GetIconPath()
    {
        var assemblyPath = Path.GetDirectoryName(typeof(IconOverlayHandlerBase).Assembly.Location);
        return Path.Combine(assemblyPath ?? "", "Icons", IconFileName);
    }

    /// <summary>
    /// Checks if the path is a system folder that shouldn't show overlays
    /// </summary>
    private static bool IsSystemFolder(string path)
    {
        if (string.IsNullOrEmpty(path))
            return true;

        var normalizedPath = path.ToLowerInvariant();

        // Skip Windows and Program Files folders
        var systemFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant(),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToLowerInvariant(),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).ToLowerInvariant(),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles).ToLowerInvariant(),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86).ToLowerInvariant()
        };

        foreach (var folder in systemFolders)
        {
            if (!string.IsNullOrEmpty(folder) &&
                normalizedPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // COM interface constants
    private const int S_OK = 0;
    private const int S_FALSE = 1;
    private const uint ISIOI_ICONFILE = 0x00000001;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
}

/// <summary>
/// COM interface for Windows Shell Icon Overlay Identifiers
/// </summary>
[ComImport]
[Guid("0C6C4200-C589-11D0-999A-00C04FD655E1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellIconOverlayIdentifier
{
    /// <summary>
    /// Determines whether the overlay should be displayed for the specified file
    /// </summary>
    [PreserveSig]
    int IsMemberOf([MarshalAs(UnmanagedType.LPWStr)] string pwszPath, uint dwAttrib);

    /// <summary>
    /// Provides the path to the overlay icon
    /// </summary>
    [PreserveSig]
    int GetOverlayInfo(IntPtr pwszIconFile, int cchMax, out int pIndex, out uint pdwFlags);

    /// <summary>
    /// Specifies the priority of the overlay
    /// </summary>
    [PreserveSig]
    int GetPriority(out int pPriority);
}
