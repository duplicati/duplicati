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

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Snapshots;

namespace Duplicati.Library.SourceProvider;

public class FileRestoreDestinationProvider(string mountedPath, bool allowRestoreOutsideTargetDirectory) : IRestoreDestinationProvider
{
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<FileRestoreDestinationProvider>();
    private static readonly string DIRSEP = Path.DirectorySeparatorChar.ToString();

    /// <inheritdoc />
    public string TargetDestination => mountedPath;

    /// <inheritdoc />
    public Task ClearReadOnlyAttribute(string path, CancellationToken cancel)
    {
        var currentAttr = SystemIO.IO_OS.GetFileAttributes(path);
        SystemIO.IO_OS.SetFileAttributes(path, currentAttr & ~FileAttributes.ReadOnly);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> CreateFolderIfNotExists(string path, CancellationToken cancel)
    {
        VerifyPath(path);
        if (SystemIO.IO_OS.DirectoryExists(path))
            return Task.FromResult(false);

        SystemIO.IO_OS.DirectoryCreate(path);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task DeleteFile(string path, CancellationToken cancel)
    {
        SystemIO.IO_OS.FileDelete(path);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteFolder(string path, CancellationToken cancel)
    {
        SystemIO.IO_OS.DirectoryDelete(path, true);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <inheritdoc />
    public Task<bool> FileExists(string path, CancellationToken cancel)
    => Task.FromResult(SystemIO.IO_OS.FileExists(path));

    /// <inheritdoc />
    public Task<long> GetFileLength(string path, CancellationToken cancel)
        => Task.FromResult(SystemIO.IO_OS.FileLength(path));

    /// <inheritdoc />
    public Task<bool> HasReadOnlyAttribute(string path, CancellationToken cancel)
    {
        var currentAttr = SystemIO.IO_OS.GetFileAttributes(path);
        return Task.FromResult(currentAttr.HasFlag(FileAttributes.ReadOnly));
    }

    /// <inheritdoc />
    public Task Initialize(CancellationToken cancel)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task Finalize(Action<double>? progressCallback, CancellationToken cancel)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task Test(CancellationToken cancellationToken)
        => SystemIO.IO_OS.DirectoryExists(mountedPath) ? Task.CompletedTask : throw new Exception($"The path {mountedPath} does not exist");

    /// <inheritdoc />
    public Task<Stream> OpenRead(string path, CancellationToken cancel)
        => Task.FromResult<Stream>(SystemIO.IO_OS.FileOpenRead(path));
    /// <inheritdoc />

    /// <inheritdoc />
    public Task<Stream> OpenReadWrite(string path, CancellationToken cancel)
        => Task.FromResult<Stream>(SystemIO.IO_OS.FileOpenReadWrite(path));

    /// <inheritdoc />
    public Task<Stream> OpenWrite(string path, CancellationToken cancel)
    {
        VerifyPath(path);
        return Task.FromResult<Stream>(SystemIO.IO_OS.FileOpenWrite(path));
    }

    /// <summary>
    /// Verify that the given path is within the target destination.
    /// </summary>
    /// <param name="path">The path to verify.</param>
    private void VerifyPath(string path)
    {
        if (allowRestoreOutsideTargetDirectory || string.IsNullOrWhiteSpace(TargetDestination))
            return;

        var fullPath = Path.GetFullPath(path);
        var fullTarget = Path.GetFullPath(TargetDestination);

        // Resolve the target destination once
        var realTarget = GetFinalPath(fullTarget);

        // Resolve the path of the file/folder we are about to create
        var realPath = GetFinalPath(fullPath);
        var relative = Path.GetRelativePath(realTarget, realPath);

        if (relative.StartsWith("..") || Path.IsPathRooted(relative))
            throw new UserInformationException($"Path traversal detected: {path} resolves outside {TargetDestination}", "RestorePathTraversal");
    }

    /// <summary>
    /// Get the final resolved path, accounting for symlinks in existing segments.
    /// </summary>
    /// <param name="path">The path to resolve.</param>
    /// <returns>The fully resolved path, with all symlinks resolved.</returns>
    private string GetFinalPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        var current = Path.GetFullPath(path);
        var ghostSegments = new Stack<string>();

        // 1. Walk up the tree until we find a segment that exists on disk
        while (!Path.Exists(current))
        {
            var parent = Path.GetDirectoryName(current);

            // If we've reached the root and nothing exists (rare/impossible for absolute paths),
            // we have to stop and return the original path.
            if (string.IsNullOrEmpty(parent) || parent == current)
                return path;

            // Store the part that doesn't exist so we can put it back later
            ghostSegments.Push(Path.GetFileName(current));
            current = parent;
        }

        // 2. Resolve symlinks for the part of the path that actually exists
        FileSystemInfo info = Directory.Exists(current)
            ? new DirectoryInfo(current)
            : new FileInfo(current);

        var resolvedPath = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName;

        // 3. Re-attach the non-existent segments to the resolved base path
        while (ghostSegments.Count > 0)
            resolvedPath = Path.Combine(resolvedPath, ghostSegments.Pop());

        return resolvedPath;
    }

    /// <inheritdoc />
    public Task<bool> WriteMetadata(string path, Dictionary<string, string?> metadata, bool restoreSymlinkMetadata, bool restorePermissions, CancellationToken cancel)
    {
        VerifyPath(path);
        var wrote_something = false;

        var isDirTarget = path.EndsWith(DIRSEP, StringComparison.Ordinal);
        var targetpath = isDirTarget ? path.Substring(0, path.Length - 1) : path;

        // Make the symlink first, otherwise we cannot apply metadata to it
        if (metadata.TryGetValue("CoreSymlinkTarget", out var k))
        {
            if (!allowRestoreOutsideTargetDirectory)
            {
                var fullPath = Path.GetFullPath(path);
                var parent = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    var target = Path.GetFullPath(Path.Combine(parent, k));
                    var fullTarget = Path.GetFullPath(TargetDestination);

                    if (!Util.AppendDirSeparator(target).StartsWith(Util.AppendDirSeparator(fullTarget), StringComparison.Ordinal))
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "SymlinkTargetOutside", null, "Skipping creation of symlink {0} -> {1} because it points outside the restore target", path, k);
                        return Task.FromResult(false);
                    }
                }
            }

            // Check if the target exists, and overwrite it if it does.
            if (SystemIO.IO_OS.FileExists(targetpath))
            {
                SystemIO.IO_OS.FileDelete(targetpath);
            }
            else if (SystemIO.IO_OS.DirectoryExists(targetpath))
            {
                SystemIO.IO_OS.DirectoryDelete(targetpath, true);
            }
            SystemIO.IO_OS.CreateSymlink(targetpath, k, isDirTarget);
            wrote_something = true;
        }
        // If the target is a folder, make sure we create it first
        else if (isDirTarget && !SystemIO.IO_OS.DirectoryExists(targetpath))
            SystemIO.IO_OS.DirectoryCreate(targetpath);

        // Avoid setting restoring symlink metadata, as that writes the symlink target, not the symlink itself
        if (!restoreSymlinkMetadata && SystemIO.IO_OS.IsSymlink(targetpath))
        {
            Logging.Log.WriteVerboseMessage(LOGTAG, "no-symlink-metadata-restored", "Not applying metadata to symlink: {0}", targetpath);
            return Task.FromResult(wrote_something);
        }

        if (metadata.TryGetValue("CoreLastWritetime", out k) && long.TryParse(k, out var t))
        {
            if (isDirTarget)
                SystemIO.IO_OS.DirectorySetLastWriteTimeUtc(targetpath, new DateTime(t, DateTimeKind.Utc));
            else
                SystemIO.IO_OS.FileSetLastWriteTimeUtc(targetpath, new DateTime(t, DateTimeKind.Utc));
        }

        if (metadata.TryGetValue("CoreCreatetime", out k) && long.TryParse(k, out t))
        {
            if (isDirTarget)
                SystemIO.IO_OS.DirectorySetCreationTimeUtc(targetpath, new DateTime(t, DateTimeKind.Utc));
            else
                SystemIO.IO_OS.FileSetCreationTimeUtc(targetpath, new DateTime(t, DateTimeKind.Utc));
        }

        if (metadata.TryGetValue("CoreAttributes", out k) && Enum.TryParse<FileAttributes>(k, true, out var fa))
            SystemIO.IO_OS.SetFileAttributes(targetpath, fa);

        SystemIO.IO_OS.SetMetadata(path, metadata, restorePermissions);
        return Task.FromResult(wrote_something);
    }
}
