// Copyright (C) 2026, The Duplicati Team
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

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;

namespace Duplicati.Library.Common.IO
{
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macOS")]
    public struct SystemIOLinux : ISystemIO
    {
        /// <summary>
        /// The log tag for messages
        /// </summary>
        private static readonly string LOGTAG = Log.LogTagFromType(typeof(SystemIOLinux));

        /// <summary>
        /// PInvoke methods
        /// </summary>
        private static class PInvoke
        {
            /// <summary>
            /// Gets the user ID of the current user
            /// </summary>
            /// <returns>The user ID</returns>
            [DllImport("libc")]
            public static extern uint getuid();

            /// <summary>
            /// Gets the group ID of the current user
            /// </summary>
            /// <returns></returns>
            [DllImport("libc")]
            public static extern uint getgid();
        }

        #region ISystemIO implementation

        public void DirectoryCreate(string path)
        {
            Directory.CreateDirectory(NormalizePath(path));
        }

        public void DirectoryDelete(string path, bool recursive)
        {
            Directory.Delete(NormalizePath(path), recursive);
        }

        public bool DirectoryExists(string path)
        {
            return Directory.Exists(NormalizePath(path));
        }

        public void DirectoryMove(string sourceDirName, string destDirName)
        {
            System.IO.Directory.Move(NormalizePath(sourceDirName), NormalizePath(destDirName));
        }

        public void FileDelete(string path)
        {
            File.Delete(path);
        }

        public void FileSetLastWriteTimeUtc(string path, DateTime time)
        {
            File.SetLastWriteTimeUtc(path, time);

            var gtime = FileGetLastWriteTimeUtc(path);
            if (gtime != time)
            {
                Console.Error.WriteLine($"DISS: {path} {gtime.ToFileTimeUtc() - time.ToFileTimeUtc()}");
            }
        }

        public void FileSetCreationTimeUtc(string path, DateTime time)
        {
            File.SetCreationTimeUtc(path, time);
        }

        public DateTime FileGetLastWriteTimeUtc(string path)
        {
            return File.GetLastWriteTimeUtc(path);
        }

        public DateTime FileGetCreationTimeUtc(string path)
        {
            return File.GetCreationTimeUtc(path);
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public FileStream FileOpenRead(string path)
        {
            return File.OpenRead(path);
        }

        public FileStream FileOpenReadWrite(string path)
        {
            return File.Exists(path)
                ? File.Open(path, FileMode.Open, FileAccess.ReadWrite)
                : File.Create(path);
        }

        public FileStream FileOpenWrite(string path)
        {
            return File.OpenWrite(path);
        }

        public FileStream FileCreate(string path)
        {
            return File.Create(path);
        }

        public FileAttributes GetFileAttributes(string path)
        {
            return File.GetAttributes(NormalizePath(path));
        }

        public void SetFileAttributes(string path, FileAttributes attributes)
        {
            File.SetAttributes(path, attributes);
        }

        public void CreateSymlink(string symlinkfile, string target, bool asDir)
        {
            PosixFile.CreateSymlink(symlinkfile, target);
        }

        public string GetSymlinkTarget(string path)
        {
            return PosixFile.GetSymlinkTarget(NormalizePath(path));
        }

        public string PathGetDirectoryName(string path)
        {
            return Path.GetDirectoryName(NormalizePath(path));
        }

        public IEnumerable<string> EnumerateFileSystemEntries(string path)
        {
            return Directory.EnumerateFileSystemEntries(path);
        }

        public IEnumerable<string> EnumerateFiles(string path)
        {
            return Directory.EnumerateFiles(path);
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            return Directory.EnumerateFiles(path, searchPattern, searchOption);
        }

        public IEnumerable<IFileEntry> EnumerateFileEntries(string path)
        {
            // For consistency with previous implementation, enumerate files first and directories after
            DirectoryInfo dir = new DirectoryInfo(path);

            foreach (FileInfo file in dir.EnumerateFiles())
            {
                yield return FileEntry(file);
            }

            foreach (DirectoryInfo d in dir.EnumerateDirectories())
            {
                yield return DirectoryEntry(d);
            }
        }

        public string PathGetFileName(string path)
        {
            return Path.GetFileName(path);
        }

        public string PathGetExtension(string path)
        {
            return Path.GetExtension(path);
        }

        public string PathChangeExtension(string path, string extension)
        {
            return Path.ChangeExtension(path, extension);
        }

        public string PathCombine(params string[] paths)
        {
            return Path.Combine(paths);
        }

        public void DirectorySetLastWriteTimeUtc(string path, DateTime time)
        {
            Directory.SetLastWriteTimeUtc(NormalizePath(path), time);
        }

        public void DirectorySetCreationTimeUtc(string path, DateTime time)
        {
            Directory.SetCreationTimeUtc(NormalizePath(path), time);
        }

        public void FileMove(string source, string target)
        {
            File.Move(source, target);
        }

        public long FileLength(string path)
        {
            return new FileInfo(path).Length;
        }

        public Dictionary<string, string> GetMetadata(string file, bool isSymlink, bool followSymlink)
        {
            var f = NormalizePath(file);
            var dict = new Dictionary<string, string>();

            var n = PosixFile.GetExtendedAttributes(f, isSymlink, followSymlink);
            if (n != null)
                foreach (var x in n)
                    dict["unix-ext:" + x.Key] = Convert.ToBase64String(x.Value);

            var fse = PosixFile.GetUserGroupAndPermissions(f);
            dict["unix:uid-gid-perm"] = string.Format("{0}-{1}-{2}", fse.UID, fse.GID, fse.Permissions);
            if (fse.OwnerName != null)
            {
                dict["unix:owner-name"] = fse.OwnerName;
            }
            if (fse.GroupName != null)
            {
                dict["unix:group-name"] = fse.GroupName;
            }

            if (OperatingSystem.IsMacOS())
            {
                try
                {
                    var flags = PosixFile.GetFileFlags(f, isSymlink, followSymlink);
                    if (flags.HasValue)
                        dict["macos:flags"] = flags.Value.ToString();
                }
                catch (Exception ex)
                {
                    Log.WriteVerboseMessage(LOGTAG, "GetFileFlagsFailed", ex, "Failed to get file flags for \"{0}\"", f);
                }

                try
                {
                    var acl = PosixFile.GetAcl(f, isSymlink, followSymlink);
                    if (!string.IsNullOrEmpty(acl))
                        dict["macos:acl"] = acl;
                }
                catch (Exception ex)
                {
                    Log.WriteVerboseMessage(LOGTAG, "GetAclFailed", ex, "Failed to get ACL for \"{0}\"", f);
                }
            }

            return dict;
        }

        public void SetMetadata(string file, Dictionary<string, string> data, bool restorePermissions)
        {
            if (data == null)
                return;

            var f = NormalizePath(file);

            foreach (var x in data.Where(x => x.Key.StartsWith("unix-ext:", StringComparison.Ordinal)).Select(x => new KeyValuePair<string, byte[]>(x.Key.Substring("unix-ext:".Length), Convert.FromBase64String(x.Value))))
                PosixFile.SetExtendedAttribute(f, x.Key, x.Value);

            if (restorePermissions && data.ContainsKey("unix:uid-gid-perm"))
            {
                var parts = data["unix:uid-gid-perm"].Split(new char[] { '-' });
                if (parts.Length == 3)
                {
                    long uid;
                    long gid;
                    long perm;

                    if (long.TryParse(parts[0], out uid) && long.TryParse(parts[1], out gid) && long.TryParse(parts[2], out perm))
                    {
                        if (data.ContainsKey("unix:owner-name"))
                            try { uid = PosixFile.GetUserID(data["unix:owner-name"]); }
                            catch { }

                        if (data.ContainsKey("unix:group-name"))
                            try { gid = PosixFile.GetGroupID(data["unix:group-name"]); }
                            catch { }

                        PosixFile.SetUserGroupAndPermissions(f, uid, gid, perm);
                    }
                }
            }

            if (OperatingSystem.IsMacOS())
            {
                // SetMetadata is only reached for a symlink when the metadata should be
                // applied to the link itself (the restore destination skips symlinks
                // otherwise), so operate on the link rather than following it.
                var isSymlink = false;
                try { isSymlink = PosixFile.GetFileType(f) == PosixFile.FileType.Symlink; }
                catch { }

                if (restorePermissions)
                {
                    // Apply ACL before file flags, as flags like uchg (immutable) can prevent ACL changes
                    if (data.TryGetValue("macos:acl", out var acl) && !string.IsNullOrWhiteSpace(acl))
                    {
                        try { PosixFile.SetAcl(f, acl, isSymlink, followSymlink: false); }
                        catch (Exception ex)
                        {
                            Log.WriteWarningMessage(LOGTAG, "SetAclFailed", ex, "Failed to restore ACL on \"{0}\": {1}", f, ex.Message);
                        }
                    }

                    // We consider file flags to be part of the permissions
                    if (data.TryGetValue("macos:flags", out var flagsStr) && uint.TryParse(flagsStr, out var flags))
                    {
                        try { PosixFile.SetFileFlags(f, flags, isSymlink, followSymlink: false); }
                        catch (Exception ex)
                        {
                            Log.WriteWarningMessage(LOGTAG, "SetFileFlagsFailed", ex, "Failed to restore file flags on \"{0}\": {1}", f, ex.Message);
                        }
                    }
                }
            }
        }

        public string GetPathRoot(string path)
        {
            return Path.GetPathRoot(path);
        }

        public string[] GetDirectories(string path)
        {
            return Directory.GetDirectories(path);
        }

        public string[] GetFiles(string path)
        {
            return Directory.GetFiles(path);
        }

        public string[] GetFiles(string path, string searchPattern)
        {
            return Directory.GetFiles(path, searchPattern);
        }

        public DateTime GetCreationTimeUtc(string path)
        {
            return File.GetCreationTimeUtc(path);
        }

        public DateTime GetLastWriteTimeUtc(string path)
        {
            return File.GetLastWriteTimeUtc(path);
        }

        public IEnumerable<string> EnumerateDirectories(string path)
        {
            return Directory.EnumerateDirectories(path);
        }

        public void FileCopy(string source, string target, bool overwrite)
        {
            File.Copy(source, target, overwrite);
        }

        public string PathGetFullPath(string path)
        {
            return Path.GetFullPath(path);
        }

        #endregion

        /// <summary>
        /// Normalizes a path, by removing any trailing slash, before calling system methods
        /// </summary>
        /// <returns>The path to normalize.</returns>
        /// <param name="path">The normalized path.</param>
        public static string NormalizePath(string path)
        {
            var p = Path.GetFullPath(path);

            // This should not be required, but some versions of Mono apparently do not strip the trailing slash
            return p.Length > 1 && p[p.Length - 1] == Path.DirectorySeparatorChar ? p.Substring(0, p.Length - 1) : p;
        }

        public IFileEntry DirectoryEntry(string path)
        {
            return DirectoryEntry(new DirectoryInfo(path));
        }
        public IFileEntry DirectoryEntry(DirectoryInfo dInfo)
        {
            return new FileEntry(dInfo.Name, 0, dInfo.LastAccessTime, dInfo.LastWriteTime)
            {
                IsFolder = true
            };
        }

        public IFileEntry FileEntry(string path)
        {
            return FileEntry(new FileInfo(path));
        }
        public IFileEntry FileEntry(FileInfo fileInfo)
        {
            return new FileEntry(fileInfo.Name, fileInfo.Length, fileInfo.LastAccessTime, fileInfo.LastWriteTime);
        }

        private static (long uid, long gid) GetOwnerAndGroup(string path)
        {
            var fi = PosixFile.GetUserGroupAndPermissions(path);
            return (fi.UID, fi.GID);
        }

        /// <summary>
        /// Sets the unix permission user read-write Only.
        /// </summary>
        /// <param name="path">The file to set permissions on.</param>
        public void FileSetPermissionUserRWOnly(string path)
        {
            PosixFile.SetUserGroupAndPermissions(
                path,
                PInvoke.getuid(),
                PInvoke.getgid(),
                Convert.ToInt32("600", 8) /* FilePermissions.S_IRUSR | FilePermissions.S_IWUSR*/
            );
        }

        /// <summary>
        /// Sets the permission to read-write only for the current user.
        /// </summary>
        /// <param name="path">The directory to set permissions on.</param>
        public void DirectorySetPermissionUserRWOnly(string path)
        {
            PosixFile.SetUserGroupAndPermissions(
                path,
                PInvoke.getuid(),
                PInvoke.getgid(),
                Convert.ToInt32("700", 8) /* FilePermissions.S_IRUSR | FilePermissions.S_IWUSR | FilePermissions.S_IXUSR */
            );
        }

        /// <summary>
        /// Checks whether the directory has the read-write only permission for the current user,
        /// matching the permissions applied by <see cref="DirectorySetPermissionUserRWOnly"/>.
        /// </summary>
        /// <param name="path">The directory to check permissions on.</param>
        /// <param name="detail">A human-readable description of why the check failed, if it did; otherwise <see cref="string.Empty"/>.</param>
        /// <returns><c>true</c> if the directory has the expected permissions; otherwise <c>false</c>.</returns>
        public bool DirectoryHasPermissionUserRWOnly(string path, out string detail)
            => HasPermissionUserRWOnly(path, Convert.ToInt32("700", 8), out detail);

        /// <summary>
        /// The root user id, which is always considered a trusted owner.
        /// </summary>
        private const long RootUid = 0;

        /// <summary>
        /// Checks whether the path has the expected user read-write only permission.
        /// The owner may be either the current user or root: a root-owned, mode 0700
        /// directory is a common and secure setup (for example a system service reading a
        /// configuration folder provisioned by an installer running as root), so it must not
        /// be rejected. The security property that matters is that no group or other access
        /// bits are set; the exact owning uid/gid beyond "current user or root" is not required.
        /// </summary>
        /// <param name="path">The path to check permissions on.</param>
        /// <param name="expectedMode">The expected octal mode (e.g. 0700 for directories).</param>
        /// <param name="detail">A human-readable description of why the check failed, if it did; otherwise <see cref="string.Empty"/>.</param>
        /// <returns><c>true</c> if the expected permissions are set; otherwise <c>false</c>.</returns>
        private static bool HasPermissionUserRWOnly(string path, int expectedMode, out string detail)
        {
            detail = string.Empty;
            try
            {
                var info = PosixFile.GetUserGroupAndPermissions(path);
                var expectedModeStr = Convert.ToString(expectedMode, 8);

                if ((info.Permissions & expectedMode) != expectedMode)
                {
                    detail = $"permissions are {Convert.ToString((long)info.Permissions, 8)} but expected {expectedModeStr}";
                    return false;
                }

                // No group/other access bits should be set
                var groupAndOther = Convert.ToInt32("077", 8);
                if ((info.Permissions & groupAndOther) != 0)
                {
                    detail = $"permissions allow group or other access ({Convert.ToString((long)info.Permissions, 8)})";
                    return false;
                }

                // The owner must be the current user or root. A root-owned folder is trusted
                // because only root (a fully privileged principal) could have created it, and
                // an unprivileged attacker cannot own a folder as root.
                var uid = PInvoke.getuid();
                if (info.UID != uid && info.UID != RootUid)
                {
                    detail = $"owner is uid {info.UID} but expected the current user (uid {uid}) or root";
                    return false;
                }
            }
            catch (Exception ex)
            {
                detail = $"failed to read permissions: {ex.Message}";
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public bool SupportsAlternateDataStreams => false;

        /// <inheritdoc />
        public IEnumerable<string> EnumerateAlternateDataStreams(string path)
            => [];

        /// <inheritdoc />
        public bool IsAlternateDataStream(string path)
            => false;

        /// <inheritdoc />
        public string GetAlternateDataStreamParent(string path)
            => path;
    }
}

