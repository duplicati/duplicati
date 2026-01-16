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

public class FileRestoreDestinationProvider(string mountedPath) : IRestoreDestinationProvider
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
    public Task Finalize(CancellationToken cancel)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task<Stream> OpenRead(string path, CancellationToken cancel)
        => Task.FromResult<Stream>(SystemIO.IO_OS.FileOpenRead(path));
    /// <inheritdoc />

    /// <inheritdoc />
    public Task<Stream> OpenReadWrite(string path, CancellationToken cancel)
        => Task.FromResult<Stream>(SystemIO.IO_OS.FileOpenReadWrite(path));

    /// <inheritdoc />
    public Task<Stream> OpenWrite(string path, CancellationToken cancel)
        => Task.FromResult<Stream>(SystemIO.IO_OS.FileOpenWrite(path));

    /// <inheritdoc />
    public Task<bool> WriteMetadata(string path, Dictionary<string, string?> metadata, bool restoreSymlinkMetadata, bool restorePermissions, CancellationToken cancel)
    {
        var wrote_something = false;

        var isDirTarget = path.EndsWith(DIRSEP, StringComparison.Ordinal);
        var targetpath = isDirTarget ? path.Substring(0, path.Length - 1) : path;

        // Make the symlink first, otherwise we cannot apply metadata to it
        if (metadata.TryGetValue("CoreSymlinkTarget", out var k))
        {
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
