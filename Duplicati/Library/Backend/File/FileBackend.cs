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
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Duplicati.Library.Backend
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class File : IBackend, IStreamingBackend, IQuotaEnabledBackend, IRenameEnabledBackend
    {
        private const string OPTION_DESTINATION_MARKER = "alternate-destination-marker";
        private const string OPTION_ALTERNATE_PATHS = "alternate-target-paths";
        private const string OPTION_MOVE_FILE = "use-move-for-put";
        private const string OPTION_FORCE_REAUTH = "force-smb-authentication";
        private const string OPTION_DISABLE_LENGTH_VERIFICATION = "disable-length-verification";

        private readonly string m_path;
        private string? m_username;
        private string? m_password;
        private readonly bool m_moveFile;
        private bool m_hasAutenticated;
        private readonly bool m_forceReauth;
        private readonly bool m_verifyDestinationLength;
        private readonly TimeoutOptionsHelper.Timeouts m_timeouts;

        private static readonly ISystemIO systemIO = SystemIO.IO_OS;

        public File()
        {
            m_path = null!;
            m_timeouts = null!;
        }

        public File(string url, Dictionary<string, string?> options)
        {
            var uri = new Utility.Uri(url);
            var path = uri.HostAndPath;
            var auth = AuthOptionsHelper.Parse(options, uri);
            m_timeouts = TimeoutOptionsHelper.Parse(options);
            m_username = auth.Username;
            m_password = auth.Password;

            if (!Path.IsPathRooted(path))
                path = systemIO.PathGetFullPath(path);

            var altPaths = options.GetValueOrDefault(OPTION_ALTERNATE_PATHS);
            if (!string.IsNullOrWhiteSpace(altPaths))
            {
                List<string> paths =
                [
                    path,
                    .. altPaths.Split(new string[] {Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries),
                ];

                //On windows we expand the drive letter * to all drives
                if (OperatingSystem.IsWindows())
                {
                    var drives = DriveInfo.GetDrives();
                    for (var i = 0; i < paths.Count; i++)
                    {
                        if (paths[i].StartsWith("*:", StringComparison.Ordinal))
                        {
                            var rpl_path = paths[i].Substring(1);
                            paths.RemoveAt(i);
                            i--;
                            foreach (var di in drives)
                                paths.Insert(++i, di.Name[0] + rpl_path);
                        }
                    }
                }

                //If there is a marker file, we do not allow the primary target path
                // to be accepted, unless it contains the marker file
                var markerfile = options.GetValueOrDefault(OPTION_DESTINATION_MARKER);
                if (!string.IsNullOrWhiteSpace(markerfile))
                    path = null;

                foreach (string p in paths)
                {
                    try
                    {
                        if (systemIO.DirectoryExists(p) && (markerfile == null || systemIO.FileExists(systemIO.PathCombine(p, markerfile))))
                        {
                            path = p;
                            break;
                        }
                    }
                    catch
                    {
                    }
                }

                if (string.IsNullOrWhiteSpace(path))
                    throw new UserInformationException(Strings.FileBackend.NoDestinationWithMarkerFileError(markerfile, paths.ToArray()), "NoDestinationWithMarker");
            }

            m_path = path;
            m_moveFile = Utility.Utility.ParseBoolOption(options, OPTION_MOVE_FILE);
            m_forceReauth = Utility.Utility.ParseBoolOption(options, OPTION_FORCE_REAUTH);
            m_verifyDestinationLength = !Utility.Utility.ParseBoolOption(options, OPTION_DISABLE_LENGTH_VERIFICATION);
            m_hasAutenticated = false;
        }

        private void PreAuthenticate()
        {
            if (!OperatingSystem.IsWindows())
                return;

            try
            {
                if (!string.IsNullOrEmpty(m_username) && m_password != null && !m_hasAutenticated)
                {
                    Win32.PreAuthenticate(m_path, m_username, m_password, m_forceReauth);
                    m_hasAutenticated = true;
                }
            }
            catch
            { }
        }

        private string GetRemoteName(string remotename)
        {
            PreAuthenticate();

            if (!systemIO.DirectoryExists(m_path))
                throw new FolderMissingException(Strings.FileBackend.FolderMissingError(m_path));

            return systemIO.PathCombine(m_path, remotename);
        }

        #region IBackendInterface Members

        public string DisplayName => Strings.FileBackend.DisplayName;

        public string ProtocolKey => "file";

        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            PreAuthenticate();

            if (!systemIO.DirectoryExists(m_path))
                throw new FolderMissingException(Strings.FileBackend.FolderMissingError(m_path));

            var res = await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, _ =>
                systemIO.EnumerateFileEntries(m_path)
            ).ConfigureAwait(false);

            foreach (var entry in res)
                yield return entry;
        }

#if DEBUG_RETRY
        private static Random random = new Random();
        public async Task Put(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            try
            {
                using(System.IO.FileStream writestream = systemIO.FileCreate(GetRemoteName(remotename)))
                {
                    if (random.NextDouble() > 0.6666)
                        throw new Exception("Random upload failure");
                    await Utility.Utility.CopyStreamAsync(stream, writestream, cancelToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                MapException(ex);
                throw;
            }

        }
#else
        public async Task PutAsync(string targetFilename, Stream sourceStream, CancellationToken cancelToken)
        {
            try
            {
                string targetFilePath = GetRemoteName(targetFilename);
                long copiedBytes = 0;
                using (var targetStream = systemIO.FileCreate(targetFilePath))
                using (var timeoutStream = targetStream.ObserveWriteTimeout(m_timeouts.ReadWriteTimeout))
                    copiedBytes = await Utility.Utility.CopyStreamAsync(sourceStream, timeoutStream, true, cancelToken).ConfigureAwait(false);

                VerifyMatchingSize(targetFilePath, sourceStream, copiedBytes);
            }
            catch (Exception ex)
            {
                MapException(ex);
                throw;
            }

        }
#endif

        public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            try
            {
                // FileOpenRead has flags System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read
                using (var readstream = systemIO.FileOpenRead(GetRemoteName(remotename)))
                using (var timeoutStream = readstream.ObserveReadTimeout(m_timeouts.ReadWriteTimeout))
                    await Utility.Utility.CopyStreamAsync(timeoutStream, stream, true, cancelToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                MapException(ex);
                throw;
            }

        }

        public Task PutAsync(string targetFilename, string sourceFilePath, CancellationToken cancelToken)
        {
            try
            {
                string targetFilePath = GetRemoteName(targetFilename);
                if (m_moveFile)
                {
                    if (systemIO.FileExists(targetFilePath))
                        systemIO.FileDelete(targetFilePath);

                    var sourceFileInfo = new FileInfo(sourceFilePath);
                    var sourceFileLength = sourceFileInfo.Exists ? (long?)sourceFileInfo.Length : null;

                    systemIO.FileMove(sourceFilePath, targetFilePath);
                    if (m_verifyDestinationLength)
                        VerifyMatchingSize(targetFilePath, null, sourceFileLength);
                }
                else
                {
                    systemIO.FileCopy(sourceFilePath, targetFilePath, true);
                    if (m_verifyDestinationLength)
                        VerifyMatchingSize(targetFilePath, sourceFilePath);
                }

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                MapException(ex);
                throw;
            }

        }

        public Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            try
            {
                systemIO.FileCopy(GetRemoteName(remotename), filename, true);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                MapException(ex);
                throw;
            }
        }

        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, _
                => systemIO.FileDelete(GetRemoteName(remotename))
            ).ConfigureAwait(false);
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                var lst = new List<ICommandLineArgument>();
                if (OperatingSystem.IsWindows())
                    lst.AddRange([
                        .. AuthOptionsHelper.GetOptions(),
                        new CommandLineArgument(OPTION_FORCE_REAUTH, CommandLineArgument.ArgumentType.Boolean, Strings.FileBackend.ForceReauthShort, Strings.FileBackend.ForceReauthLong)
                    ]);

                lst.AddRange([
                    new CommandLineArgument(OPTION_DESTINATION_MARKER, CommandLineArgument.ArgumentType.String, Strings.FileBackend.AlternateDestinationMarkerShort, Strings.FileBackend.AlternateDestinationMarkerLong(OPTION_ALTERNATE_PATHS)),
                    new CommandLineArgument(OPTION_ALTERNATE_PATHS, CommandLineArgument.ArgumentType.Path, Strings.FileBackend.AlternateTargetPathsShort, Strings.FileBackend.AlternateTargetPathsLong(OPTION_DESTINATION_MARKER, System.IO.Path.PathSeparator)),
                    new CommandLineArgument(OPTION_MOVE_FILE, CommandLineArgument.ArgumentType.Boolean, Strings.FileBackend.UseMoveForPutShort, Strings.FileBackend.UseMoveForPutLong),
                    new CommandLineArgument(OPTION_DISABLE_LENGTH_VERIFICATION, CommandLineArgument.ArgumentType.Boolean, Strings.FileBackend.DisableLengthVerificationShort, Strings.FileBackend.DisableLengthVerificationLong),
                    .. TimeoutOptionsHelper.GetOptions()
                ]);

                return lst;
            }
        }

        public string Description => Strings.FileBackend.Description;

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestReadWritePermissionsAsync(cancelToken);

        public async Task CreateFolderAsync(CancellationToken cancelToken)
        {
            if (systemIO.DirectoryExists(m_path))
                throw new FolderAreadyExistedException();

            await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, _
                => systemIO.DirectoryCreate(m_path)
            ).ConfigureAwait(false);
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            m_username = null;
            m_password = null;
        }

        #endregion

        private static void MapException(Exception ex)
        {
            if (ex is DirectoryNotFoundException)
                throw new FolderMissingException(Strings.FileBackend.FolderMissingError(ex.Message), ex);
            if (ex is FileNotFoundException)
                throw new FileMissingException(Strings.FileBackend.FileNotFoundError(ex.Message), ex);
        }

        private DriveInfo? GetDrive()
        {
            string root;
            if (!OperatingSystem.IsWindows())
            {
                string path = Util.AppendDirSeparator(systemIO.PathGetFullPath(m_path));

                // If the built-in .NET DriveInfo works, use it
                try { return new DriveInfo(path); }
                catch { }

                root = "/";

                //Find longest common prefix from mounted devices
                //TODO: Can trick this with symlinks, where the symlink is on one mounted volume,
                // and the actual storage is on another
                foreach (var di in DriveInfo.GetDrives())
                    if (path.StartsWith(Util.AppendDirSeparator(di.Name), StringComparison.Ordinal) && di.Name.Length > root.Length)
                        root = di.Name;
            }
            else
            {
                root = systemIO.GetPathRoot(m_path);
            }

            // On Windows, DriveInfo is only valid for lettered drives. (e.g., not for UNC paths and shares)
            // So only attempt to get it if we aren't on Windows or if the root starts with a letter.
            if (!OperatingSystem.IsWindows() || (root.Length > 0 && char.IsLetter(root[0])))
            {
                try
                {
                    return new DriveInfo(root);
                }
                catch (ArgumentException)
                {
                    // If there was a problem, fall back to returning null
                }
            }

            return null;
        }

        public Task<IQuotaInfo?> GetQuotaInfoAsync(CancellationToken cancelToken)
        {
            var driveInfo = this.GetDrive();
            if (driveInfo != null)
            {
                // Check that the total space is above 0, because Mono sometimes reports 0 for unknown file systems
                // If the drive actually has a total size of 0, this should be obvious immediately due to write errors
                if (driveInfo.TotalSize > 0)
                {
                    return Task.FromResult<IQuotaInfo?>(new QuotaInfo(driveInfo.TotalSize, driveInfo.AvailableFreeSpace));
                }
            }

            if (OperatingSystem.IsWindows())
            {
                // If we can't get the DriveInfo on Windows, fallback to GetFreeDiskSpaceEx
                // https://stackoverflow.com/questions/2050343/programmatically-determining-space-available-from-unc-path
                return Task.FromResult<IQuotaInfo?>(GetDiskFreeSpace(m_path));
            }

            return Task.FromResult<IQuotaInfo?>(null);
        }

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());

        public Task RenameAsync(string oldname, string newname, CancellationToken cancellationToken)
        {
            var source = GetRemoteName(oldname);
            var target = GetRemoteName(newname);
            if (systemIO.FileExists(target))
                systemIO.FileDelete(target);
            systemIO.FileMove(source, target);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Get the disk free space using the Win32 API's GetDiskFreeSpaceEx function.
        /// </summary>
        /// <param name="directory">Directory</param>
        /// <returns>Quota info</returns>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        [SupportedOSPlatform("windows")]
        public static QuotaInfo? GetDiskFreeSpace(string directory)
        {
            ulong available;
            ulong total;
            if (WindowsDriveHelper.GetDiskFreeSpaceEx(directory, out available, out total, out _))
            {
                return new QuotaInfo((long)total, (long)available);
            }
            else
            {
                return null;
            }
        }

        [SupportedOSPlatform("windows")]
        private static class WindowsDriveHelper
        {
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetDiskFreeSpaceEx(
                string lpDirectoryName,
                out ulong lpFreeBytesAvailable,
                out ulong lpTotalNumberOfBytes,
                out ulong lpTotalNumberOfFreeBytes);
        }

        private static void VerifyMatchingSize(string targetFilePath, string sourceFilePath)
        {
            try
            {
                var targetFileInfo = new FileInfo(targetFilePath);
                if (!targetFileInfo.Exists)
                    throw new FileMissingException($"Target file does not exist. Target: {targetFilePath}");

                var sourceFileInfo = new FileInfo(sourceFilePath);
                if (!sourceFileInfo.Exists)
                    throw new FileMissingException($"Source file does not exist. Source: {sourceFilePath}");

                if (targetFileInfo.Length != sourceFileInfo.Length)
                    throw new FileMissingException($"Target file size ({targetFileInfo.Length:n0}) is different from source file size ({sourceFileInfo.Length:n0}). Target: {targetFilePath}");
            }
            catch
            {
                try { System.IO.File.Delete(targetFilePath); } catch { }
                throw;
            }
        }

        private static void VerifyMatchingSize(string targetFilePath, Stream? sourceStream, long? expectedLength)
        {
            try
            {
                var targetFileInfo = new FileInfo(targetFilePath);
                if (!targetFileInfo.Exists)
                    throw new FileMissingException($"Target file does not exist. Target: {targetFilePath}");

                bool isStreamPostion = false;
                long? sourceStreamLength = sourceStream == null ? null : Utility.Utility.GetStreamLength(sourceStream, out isStreamPostion);

                if (sourceStreamLength.HasValue && targetFileInfo.Length != sourceStreamLength.Value)
                    throw new FileMissingException($"Target file size ({targetFileInfo.Length:n0}) is different from the source length ({sourceStreamLength.Value:n0}){(isStreamPostion ? " - ending stream position" : "")}. Target: {targetFilePath}");

                if (expectedLength.HasValue && targetFileInfo.Length != expectedLength.Value)
                    throw new FileMissingException($"Target file size ({targetFileInfo.Length:n0}) is different from the expected length ({expectedLength.Value:n0}). Target: {targetFilePath}");
            }
            catch
            {
                try { System.IO.File.Delete(targetFilePath); }
                catch { }
                throw;
            }
        }
    }
}
