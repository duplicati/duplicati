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
    /// <summary>
    /// The file backend implementation
    /// </summary>
    public class File : IBackend, IStreamingBackend, IQuotaEnabledBackend, IRenameEnabledBackend
    {
        /// <summary>
        /// The option key for the destination marker file
        /// </summary>
        private const string OPTION_DESTINATION_MARKER = "alternate-destination-marker";
        /// <summary>
        /// The option key for the alternate target paths
        /// </summary>
        private const string OPTION_ALTERNATE_PATHS = "alternate-target-paths";
        /// <summary>
        /// The option key for moving files instead of copying them
        /// </summary>
        private const string OPTION_MOVE_FILE = "use-move-for-put";
        /// <summary>
        /// The option key for forcing reauthentication against the remote share
        /// </summary>
        private const string OPTION_FORCE_REAUTH = "force-smb-authentication";
        /// <summary>
        /// The option key for disabling length verification
        /// </summary>
        private const string OPTION_DISABLE_LENGTH_VERIFICATION = "disable-length-verification";
        /// <summary>
        /// The option key for disabling filename sanitization
        /// </summary>
        private const string OPTION_DISABLE_FILENAME_SANITIZATION = "disable-filename-sanitation";

        /// <summary>
        /// The path to the remote file storage (i.e., the directory where files are stored)
        /// </summary>
        private readonly string m_path;
        /// <summary>
        /// The username for authentication against the remote share
        /// </summary>
        private string? m_username;
        /// <summary>
        /// The password for authentication against the remote share
        /// </summary>
        private string? m_password;
        /// <summary>
        /// Flag to indicate if the file should be moved instead of copied
        /// </summary>
        private readonly bool m_moveFile;
        /// <summary>
        /// Flag to indicate if the backend has already authenticated
        /// </summary>
        private bool m_hasAutenticated;
        /// <summary>
        /// Flag to indicate if the backend should force reauthentication
        /// </summary>
        private readonly bool m_forceReauth;
        /// <summary>
        /// Flag to indicate if the destination file length should be verified against the source file length
        /// </summary>
        private readonly bool m_verifyDestinationLength;
        /// <summary>
        /// Flag to indicate if filename sanitization is disabled
        /// </summary>
        private readonly bool m_disableFilenameSanitization;
        /// <summary>
        /// The timeout options for the backend operations
        /// </summary>
        private readonly TimeoutOptionsHelper.Timeouts m_timeouts;

        /// <summary>
        /// The system IO abstraction used for file operations
        /// </summary>
        private static readonly ISystemIO systemIO = SystemIO.IO_OS;

        /// <summary>
        /// Unused constructor for dynamic loading purposes
        /// </summary>
        public File()
        {
            m_path = null!;
            m_timeouts = null!;
        }

        /// <summary>
        /// Constructs a file backend with the specified URL and options.
        /// </summary>
        /// <param name="url">The url to the remote file storage</param>
        /// <param name="options">The options for the file backend</param>
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
            m_disableFilenameSanitization = Utility.Utility.ParseBoolOption(options, OPTION_DISABLE_FILENAME_SANITIZATION);
            m_hasAutenticated = false;
        }

        /// <summary>
        /// Pre-authenticates against the remote share if necessary.
        /// </summary>
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

        /// <summary>
        /// Sanitizes the filenames if filename sanitization is enabled.
        /// </summary>
        /// <param name="filename">The filename to sanitize</param>
        /// <returns>The sanitized filename</returns>
        private string SanitizeFilename(string filename)
        {
            if (m_disableFilenameSanitization)
                return filename;

            // Trim null characters due to issue with Sharepoint mounted folders
            return filename.Trim('\0');
        }

        /// <summary>
        /// Gets the remote path for the specified file.
        /// </summary>
        /// <param name="remotename">The remote filename</param>
        /// <returns>The remote path for the specified file</returns>
        private string GetRemoteName(string remotename)
        {
            PreAuthenticate();

            if (!systemIO.DirectoryExists(m_path))
                throw new FolderMissingException(Strings.FileBackend.FolderMissingError(m_path));

            return systemIO.PathCombine(m_path, SanitizeFilename(remotename));
        }

        #region IBackendInterface Members

        /// <inheritdoc />
        public string DisplayName => Strings.FileBackend.DisplayName;

        /// <inheritdoc />
        public string ProtocolKey => "file";

        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            PreAuthenticate();

            if (!systemIO.DirectoryExists(m_path))
                throw new FolderMissingException(Strings.FileBackend.FolderMissingError(m_path));

            var res = await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, _ =>
                systemIO.EnumerateFileEntries(m_path)
                    .Select(entry => new FileEntry(SanitizeFilename(entry.Name), entry.Size, entry.LastAccess, entry.LastModification, entry.IsFolder, entry.IsArchived))
            ).ConfigureAwait(false);

            foreach (var entry in res)
                yield return entry;
        }

#if DEBUG_RETRY
        /// <inheritdoc />
        public async Task Put(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            try
            {
                using(var writestream = systemIO.FileCreate(GetRemoteName(remotename)))
                {
                    if (Random.Shared.NextDouble() > 0.6666)
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
        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, _
                => systemIO.FileDelete(GetRemoteName(remotename))
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
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
                    new CommandLineArgument(OPTION_DISABLE_FILENAME_SANITIZATION, CommandLineArgument.ArgumentType.Boolean, Strings.FileBackend.DisableFilenameSanitizationShort, Strings.FileBackend.DisableFilenameSanitizationLong),
                    .. TimeoutOptionsHelper.GetOptions()
                ]);

                return lst;
            }
        }

        /// <inheritdoc />
        public string Description => Strings.FileBackend.Description;

        /// <inheritdoc />
        public Task TestAsync(CancellationToken cancelToken)
            => this.TestReadWritePermissionsAsync(cancelToken);

        /// <inheritdoc />
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

        /// <inheritdoc />
        public void Dispose()
        {
            m_username = null;
            m_password = null;
        }

        #endregion

        /// <summary>
        /// Maps System.IO exceptions to Duplicati specific exceptions.
        /// </summary>
        /// <param name="ex">The exception to map.</param>
        private static void MapException(Exception ex)
        {
            if (ex is DirectoryNotFoundException)
                throw new FolderMissingException(Strings.FileBackend.FolderMissingError(ex.Message), ex);
            if (ex is FileNotFoundException)
                throw new FileMissingException(Strings.FileBackend.FileNotFoundError(ex.Message), ex);
        }

        /// <summary>
        /// Gets the drive or volume information for the specified path.
        /// </summary>
        /// <returns>The drive or volume information, or null if it could not be determined.</returns>
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

        /// <inheritdoc />
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

        /// <inheritdoc />
        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());

        /// <inheritdoc />
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
        [MethodImpl(MethodImplOptions.NoInlining)]
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

        /// <summary>
        /// Provides helper methods for working with Windows drives.
        /// </summary>
        [SupportedOSPlatform("windows")]
        private static class WindowsDriveHelper
        {
            /// <summary>
            /// Gets the free disk space for the specified directory using the Win32 API.
            /// </summary>
            /// <param name="lpDirectoryName">The directory name to check</param>
            /// <param name="lpFreeBytesAvailable">The available free bytes</param>
            /// <param name="lpTotalNumberOfBytes">The total number of bytes</param>
            /// <param name="lpTotalNumberOfFreeBytes">The total number of free bytes</param>
            /// <returns></returns>
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetDiskFreeSpaceEx(
                string lpDirectoryName,
                out ulong lpFreeBytesAvailable,
                out ulong lpTotalNumberOfBytes,
                out ulong lpTotalNumberOfFreeBytes);
        }

        /// <summary>
        /// Verifies that the target file size matches the source file size.
        /// </summary>
        /// <param name="targetFilePath">The path to the target file</param>
        /// <param name="sourceFilePath"> The path to the source file</param>
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

        /// <summary>
        /// Verifies that the target file size matches the source stream length or expected length.
        /// </summary>
        /// <param name="targetFilePath">The path to the target file</param>
        /// <param name="sourceStream">The source stream to check the length against</param>
        /// <param name="expectedLength">The expected length of the file</param>
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
