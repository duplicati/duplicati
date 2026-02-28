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
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using System.Globalization;
using System.Threading;
using Duplicati.Library.Utility.Options;
using System.Diagnostics.CodeAnalysis;
using Duplicati.Library.Snapshots;
using System.Runtime.Versioning;
using Duplicati.Library.Snapshots.MacOS;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// A class for keeping all Duplicati options in one place,
    /// and provide typesafe access to the options
    /// </summary>
    public class Options
    {
        /// <summary>
        /// The default block hash algorithm
        /// </summary>
        private const string DEFAULT_BLOCK_HASH_ALGORITHM = "SHA256";
        /// <summary>
        /// The default file hash algorithm
        /// </summary>
        private const string DEFAULT_FILE_HASH_ALGORITHM = "SHA256";

        /// <summary>
        /// The default block size, chose to minimize hash numbers but allow smaller upload sizes.
        /// </summary>
        private const string DEFAULT_BLOCKSIZE = "1mb";

        /// <summary>
        /// The default threshold value
        /// </summary>
        private const long DEFAULT_THRESHOLD = 25;

        /// <summary>
        /// The default value for maximum number of small files
        /// </summary>
        private const long DEFAULT_SMALL_FILE_MAX_COUNT = 20;

        /// <summary>
        /// Default size of volumes
        /// </summary>
        private const string DEFAULT_VOLUME_SIZE = "50mb";

        /// <summary>
        /// Default value for keep-versions
        /// </summary>
        private const int DEFAULT_KEEP_VERSIONS = 0;

        /// <summary>
        /// The default threshold for purging log data
        /// </summary>
        private const string DEFAULT_LOG_RETENTION = "30D";

        /// <summary>
        /// The default activity timeout
        /// </summary>
        private const string DEFAULT_READ_WRITE_TIMEOUT = "30s";

        /// <summary>
        /// The default number of concurrent uploads
        /// </summary>
        private const int DEFAULT_ASYNCHRONOUS_UPLOAD_LIMIT = 4;

        /// <summary>
        /// The backends where throttling is disabled by default
        /// </summary>
        private const string DEFAULT_THROTTLE_DISABLED_BACKENDS = "";

        /// <summary>
        /// The default retry delay
        /// </summary>
        private const string DEFAULT_RETRY_DELAY = "10s";

        /// <summary>
        /// The default number of retries
        /// </summary>
        private const int DEFAULT_NUMBER_OF_RETRIES = 5;

        /// <summary>
        /// The default number of backup test samples
        /// </summary>
        private const long DEFAULT_BACKUP_TEST_SAMPLES = 1;

        /// <summary>
        /// The default snapshot policy
        /// </summary>
        private static readonly OptimizationStrategy DEFAULT_SNAPSHOT_POLICY = PermissionHelper.HasSeBackupPrivilege()
            ? OptimizationStrategy.Auto
            : OptimizationStrategy.Off;

        /// <summary>
        /// The default policy for BackupRead
        /// </summary>
        private static readonly OptimizationStrategy DEFAULT_BACKUPREAD_POLICY = OptimizationStrategy.Off;

        /// <summary>
        /// The default MacOS photos handling strategy
        /// </summary>
        private static readonly MacOSPhotosHandling DEFAULT_MACOS_PHOTOS_HANDLING = MacOSPhotosHandling.PhotosAndLibrary;

        /// <summary>
        /// The default number of compressor instances
        /// </summary>
        private static readonly int DEFAULT_COMPRESSORS = Math.Max(1, Environment.ProcessorCount / 2);

        /// <summary>
        /// The default number of file processors instances
        /// </summary>
        private static readonly int DEFAULT_FILE_PROCESSORS = Math.Max(1, Environment.ProcessorCount / 2);

        /// <summary>
        /// The default number of hasher instances
        /// </summary>
        private static readonly int DEFAULT_BLOCK_HASHERS = Math.Max(1, Environment.ProcessorCount / 2);

        /// <summary>
        /// The default threshold for warning about coming close to quota
        /// </summary>
        private const int DEFAULT_QUOTA_WARNING_THRESHOLD = 10;

        /// <summary>
        /// The default value for the maximum size of the restore cache
        /// </summary>
        private const string DEFAULT_RESTORE_CACHE_MAX = "4gb";

        /// <summary>
        /// The default value for the percentage of the restore cache to evict when full
        /// </summary>
        private const int DEFAULT_RESTORE_CACHE_EVICT = 50;

        /// <summary>
        /// The default value for the number of file processors during restore
        /// </summary>
        private static readonly int DEFAULT_RESTORE_FILE_PROCESSORS = Math.Max(1, Environment.ProcessorCount / 2);

        /// <summary>
        /// The default value for the restore volume cache hint. When empty, the cache is unlimited (disk-space-aware eviction via <see cref="RestoreVolumeCacheMinFree"/>).
        /// </summary>
        private const string DEFAULT_RESTORE_VOLUME_CACHE_HINT = "";

        /// <summary>
        /// The default minimum free disk space to maintain in the temp directory during restore (1 GB).
        /// </summary>
        private const string DEFAULT_RESTORE_VOLUME_CACHE_MIN_FREE = "1gb";

        /// <summary>
        /// The default value for the number of volume decryptors during restore
        /// </summary>
        private static readonly int DEFAULT_RESTORE_VOLUME_DECRYPTORS = Math.Max(1, Environment.ProcessorCount / 2);

        /// <summary>
        /// The default value for the number of volume decompressors during restore
        /// </summary>
        private static readonly int DEFAULT_RESTORE_VOLUME_DECOMPRESSORS = Math.Max(1, Environment.ProcessorCount / 2);

        /// <summary>
        /// The default value for the number of volume downloaders during restore
        /// </summary>
        private static readonly int DEFAULT_RESTORE_VOLUME_DOWNLOADERS = Math.Max(1, Environment.ProcessorCount / 2);

        /// <summary>
        /// The default value for the size of the channel buffers during restore
        /// </summary>
        private static readonly int DEFAULT_RESTORE_CHANNEL_BUFFER_SIZE = Environment.ProcessorCount;

        /// <summary>
        /// An enumeration that describes the supported strategies for an optimization
        /// </summary>
        public enum OptimizationStrategy
        {
            /// <summary>
            /// The optimization feature is created if possible, but silently ignored if it fails
            /// </summary>
            Auto,
            /// <summary>
            /// The optimization feature is created if possible, but an error is logged if it fails
            /// </summary>
            On,
            /// <summary>
            /// The optimization feature is deactivated
            /// </summary>
            Off,
            /// <summary>
            /// The optimization feature is created, and the backup is aborted if it fails
            /// </summary>
            Required
        }

        /// <summary>
        /// The possible settings for the symlink strategy
        /// </summary>
        public enum SymlinkStrategy
        {
            /// <summary>
            /// Store information about the symlink
            /// </summary>
            Store,

            /// <summary>
            /// Treat symlinks as normal files or folders
            /// </summary>
            Follow,

            /// <summary>
            /// Ignore all symlinks
            /// </summary>
            Ignore
        }

        /// <summary>
        /// The possible settings for the remote test strategy
        /// </summary>
        public enum RemoteTestStrategy
        {
            /// <summary>
            /// Test the remote volumes
            /// </summary>
            True = 0,

            /// <summary>
            /// Do not test the remote volumes
            /// </summary>
            False = 1,

            /// <summary>
            /// Test only the list and index volumes
            /// </summary>
            ListAndIndexes = 2,

            /// <summary>
            /// Test only the index files
            /// </summary>
            IndexesOnly = 3,

            /// <summary>
            /// Alias for IndexesOnly
            /// </summary>
            IndexOnly = 3
        }

        /// <summary>
        /// The possible settings for the hardlink strategy
        /// </summary>
        public enum HardlinkStrategy
        {
            /// <summary>
            /// Process only the first hardlink
            /// </summary>
            First,

            /// <summary>
            /// Process all hardlinks
            /// </summary>
            All,

            /// <summary>
            /// Ignore all hardlinks
            /// </summary>
            None
        }

        /// <summary>
        /// The possible settings for index file usage
        /// </summary>
        public enum IndexFileStrategy
        {
            /// <summary>
            /// Disables usage of index files
            /// </summary>
            None,

            /// <summary>
            /// Stores only block lookup information in the index files
            /// </summary>
            Lookup,

            /// <summary>
            /// Stores both block lookup and block lists in the index files
            /// </summary>
            Full

        }

        private static readonly string DEFAULT_COMPRESSED_EXTENSION_FILE = System.IO.Path.Combine(Duplicati.Library.AutoUpdater.UpdaterManager.INSTALLATIONDIR, "default_compressed_extensions.txt");

        /// <summary>
        /// Lock that protects the options collection
        /// </summary>
        protected readonly object m_lock = new object();

        protected readonly Dictionary<string, string?> m_options;

        /// <summary>
        /// Lookup table for compression hints
        /// </summary>
        private Dictionary<string, CompressionHint>? m_compressionHints;

        public Options(Dictionary<string, string?> options)
        {
            m_options = options;
        }

        public Dictionary<string, string?> RawOptions => m_options;

        /// <summary>
        /// Returns a list of strings that are not supported on the commandline as options, but used internally
        /// </summary>
        public static string[] InternalOptions => [
                    "main-action"
                ];

        /// <summary>
        /// Returns a list of options that are intentionally duplicate
        /// </summary>
        public static readonly IReadOnlySet<string> KnownDuplicates =
            AuthOptionsHelper.GetOptions().Select(x => x.Name)
            .Concat(SslOptionsHelper.GetOptions().Select(x => x.Name))
            .Concat(TimeoutOptionsHelper.GetOptions().Select(x => x.Name))
            .Concat(AuthIdOptionsHelper.GetOptions("").Select(x => x.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A default backup name
        /// </summary>
        public static string DefaultBackupName => System.IO.Path.GetFileNameWithoutExtension(Library.Utility.Utility.getEntryAssembly().Location);

        /// <summary>
        /// Gets the options that are conditional based on the operating system
        /// </summary>
        /// <returns>An enumerable of command line arguments</returns>
        private IEnumerable<ICommandLineArgument> GetOSConditionalCommands()
        {
            var items = new List<ICommandLineArgument>();
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                yield return new CommandLineArgument("snapshot-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.SnapshotpolicyShort, Strings.Options.SnapshotpolicyLong, DEFAULT_SNAPSHOT_POLICY.ToString(), null, Enum.GetNames(typeof(OptimizationStrategy)));

            if (OperatingSystem.IsWindows())
            {
                yield return new CommandLineArgument("snapshot-provider", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.SnapshotproviderShort, Strings.Options.SnapshotproviderLong, WindowsSnapshot.DEFAULT_WINDOWS_SNAPSHOT_PROVIDER.ToString(), null, Enum.GetNames(typeof(Snapshots.WindowsSnapshotProvider)));
                yield return new CommandLineArgument("vss-exclude-writers", CommandLineArgument.ArgumentType.String, Strings.Options.VssexcludewritersShort, Strings.Options.VssexcludewritersLong, "{e8132975-6f93-4464-a53e-1050253ae220}");
                yield return new CommandLineArgument("vss-use-mapping", CommandLineArgument.ArgumentType.Boolean, Strings.Options.VssusemappingShort, Strings.Options.VssusemappingLong, "false");
                yield return new CommandLineArgument("usn-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.UsnpolicyShort, Strings.Options.UsnpolicyLong, "off", null, Enum.GetNames(typeof(OptimizationStrategy)));
                yield return new CommandLineArgument("backupread-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.BackupreadpolicyShort, Strings.Options.BackupreadpolicyLong, DEFAULT_BACKUPREAD_POLICY.ToString(), null, Enum.GetNames(typeof(OptimizationStrategy)));
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                yield return new CommandLineArgument("ignore-advisory-locking", CommandLineArgument.ArgumentType.Boolean, Strings.Options.IgnoreadvisorylockingShort, Strings.Options.IgnoreadvisorylockingLong, "false");

            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                yield return new CommandLineArgument("disable-backup-exclusion-xattr", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisablebackupexclusionxattrShort, Strings.Options.DisablebackupexclusionxattrLong, "false");

            if (OperatingSystem.IsMacOS())
            {
                yield return new CommandLineArgument("photos-handling", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.DisablephotohandlingShort, Strings.Options.DisablephotohandlingLong, DEFAULT_MACOS_PHOTOS_HANDLING.ToString(), null, Enum.GetNames(typeof(MacOSPhotosHandling)));
                yield return new CommandLineArgument("photos-library-path", CommandLineArgument.ArgumentType.Path, Strings.Options.MacosphotoslibrarypathShort, Strings.Options.MacosphotoslibrarypathLong);
            }
        }

        /// <summary>
        /// Gets all supported commands
        /// </summary>
        public IList<ICommandLineArgument> SupportedCommands =>
        [
            new CommandLineArgument("dblock-size", CommandLineArgument.ArgumentType.Size, Strings.Options.DblocksizeShort, Strings.Options.DblocksizeLong, DEFAULT_VOLUME_SIZE),
            new CommandLineArgument("auto-cleanup", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AutocleanupShort, Strings.Options.AutocleanupLong, "false"),
            new CommandLineArgument("unittest-mode", CommandLineArgument.ArgumentType.Boolean, Strings.Options.UnittestmodeShort, Strings.Options.UnittestmodeLong, "false"),

            new CommandLineArgument("control-files", CommandLineArgument.ArgumentType.Path, Strings.Options.ControlfilesShort, Strings.Options.ControlfilesLong),
            new CommandLineArgument("skip-file-hash-checks", CommandLineArgument.ArgumentType.Boolean, Strings.Options.SkipfilehashchecksShort, Strings.Options.SkipfilehashchecksLong, "false"),
            new CommandLineArgument("dont-read-manifests", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DontreadmanifestsShort, Strings.Options.DontreadmanifestsLong, "false"),
            new CommandLineArgument("restore-path", CommandLineArgument.ArgumentType.String, Strings.Options.RestorepathShort, Strings.Options.RestorepathLong),
            new CommandLineArgument("time", CommandLineArgument.ArgumentType.DateTime, Strings.Options.TimeShort, Strings.Options.TimeLong, "now"),
            new CommandLineArgument("version", CommandLineArgument.ArgumentType.String, Strings.Options.VersionShort, Strings.Options.VersionLong, ""),
            new CommandLineArgument("all-versions", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllversionsShort, Strings.Options.AllversionsLong, "false"),
            new CommandLineArgument("list-prefix-only", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ListprefixonlyShort, Strings.Options.ListprefixonlyLong, "false"),
            new CommandLineArgument("soft-delete-prefix", CommandLineArgument.ArgumentType.String, Strings.Options.SoftdeleteprefixShort, Strings.Options.SoftdeleteprefixLong),
            new CommandLineArgument("prevent-backend-rename", CommandLineArgument.ArgumentType.Boolean, Strings.Options.PreventbackendrenameShort, Strings.Options.PreventbackendrenameLong, "false"),
            new CommandLineArgument("list-folder-contents", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ListfoldercontentsShort, Strings.Options.ListfoldercontentsLong, "false"),
            new CommandLineArgument("list-sets-only", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ListsetsonlyShort, Strings.Options.ListsetsonlyLong, "false"),
            new CommandLineArgument("disable-autocreate-folder", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisableautocreatefolderShort, Strings.Options.DisableautocreatefolderLong, "false"),
            new CommandLineArgument("allow-missing-source", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllowmissingsourceShort, Strings.Options.AllowmissingsourceLong, "false"),
            new CommandLineArgument("prevent-empty-source", CommandLineArgument.ArgumentType.Boolean, Strings.Options.PreventemptysourceShort, Strings.Options.PreventemptysourceLong, "false"),

            new CommandLineArgument("disable-filetime-check", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisablefiletimecheckShort, Strings.Options.DisablefiletimecheckLong, "false"),
            new CommandLineArgument("check-filetime-only", CommandLineArgument.ArgumentType.Boolean, Strings.Options.CheckfiletimeonlyShort, Strings.Options.CheckfiletimeonlyLong, "false"),
            new CommandLineArgument("disable-time-tolerance", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisabletimetoleranceShort, Strings.Options.DisabletimetoleranceLong, "false"),

            new CommandLineArgument("tempdir", CommandLineArgument.ArgumentType.Path, Strings.Options.TempdirShort, Strings.Options.TempdirLong, System.IO.Path.GetTempPath()),
            new CommandLineArgument("thread-priority", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.ThreadpriorityShort, Strings.Options.ThreadpriorityLong, "normal", null, new string[] {"highest", "high", "abovenormal", "normal", "belownormal", "low", "lowest", "idle" }, Strings.Options.ThreadpriorityDeprecated),

            new CommandLineArgument("prefix", CommandLineArgument.ArgumentType.String, Strings.Options.PrefixShort, Strings.Options.PrefixLong, "duplicati"),

            new CommandLineArgument("passphrase", CommandLineArgument.ArgumentType.Password, Strings.Options.PassphraseShort, Strings.Options.PassphraseLong),
            new CommandLineArgument("new-passphrase", CommandLineArgument.ArgumentType.Password, Strings.Options.PassphraseShort, Strings.Options.PassphraseLong),
            new CommandLineArgument("no-encryption", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NoencryptionShort, Strings.Options.NoencryptionLong, "false"),

            new CommandLineArgument("number-of-retries", CommandLineArgument.ArgumentType.Integer, Strings.Options.NumberofretriesShort, Strings.Options.NumberofretriesLong, DEFAULT_NUMBER_OF_RETRIES.ToString()),
            new CommandLineArgument("retry-delay", CommandLineArgument.ArgumentType.Timespan, Strings.Options.RetrydelayShort, Strings.Options.RetrydelayLong, DEFAULT_RETRY_DELAY),
            new CommandLineArgument("retry-with-exponential-backoff", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RetrywithexponentialbackoffShort, Strings.Options.RetrywithexponentialbackoffLong, "false"),

            new CommandLineArgument("synchronous-upload", CommandLineArgument.ArgumentType.Boolean, Strings.Options.SynchronousuploadShort, Strings.Options.SynchronousuploadLong, "false"),
            new CommandLineArgument("asynchronous-upload-limit", CommandLineArgument.ArgumentType.Integer, Strings.Options.AsynchronousconcurrentuploadlimitShort, Strings.Options.AsynchronousconcurrentuploadlimitLong, DEFAULT_ASYNCHRONOUS_UPLOAD_LIMIT.ToString(), ["asynchronous-concurrent-upload-limit"]),
            new CommandLineArgument("asynchronous-upload-folder", CommandLineArgument.ArgumentType.Path, Strings.Options.AsynchronousuploadfolderShort, Strings.Options.AsynchronousuploadfolderLong, System.IO.Path.GetTempPath()),

            new CommandLineArgument("disable-streaming-transfers", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisableStreamingShort, Strings.Options.DisableStreamingLong, "false"),

            new CommandLineArgument("throttle-upload", CommandLineArgument.ArgumentType.Size, Strings.Options.ThrottleuploadShort, Strings.Options.ThrottleuploadLong, "0kb"),
            new CommandLineArgument("throttle-download", CommandLineArgument.ArgumentType.Size, Strings.Options.ThrottledownloadShort, Strings.Options.ThrottledownloadLong, "0kb"),
            new CommandLineArgument("throttle-disabled", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisablethrottleShort, Strings.Options.DisablethrottleLong, "false"),
            new CommandLineArgument("throttle-disabled-backends", CommandLineArgument.ArgumentType.String, Strings.Options.DisablethrottlebackendsShort, Strings.Options.DisablethrottlebackendsLong("throttle-disabled"), DEFAULT_THROTTLE_DISABLED_BACKENDS),
            new CommandLineArgument("skip-files-larger-than", CommandLineArgument.ArgumentType.Size, Strings.Options.SkipfileslargerthanShort, Strings.Options.SkipfileslargerthanLong),

            new CommandLineArgument("upload-unchanged-backups", CommandLineArgument.ArgumentType.Boolean, Strings.Options.UploadUnchangedBackupsShort, Strings.Options.UploadUnchangedBackupsLong, "false"),

            new CommandLineArgument("encryption-module", CommandLineArgument.ArgumentType.String, Strings.Options.EncryptionmoduleShort, Strings.Options.EncryptionmoduleLong, "aes"),
            new CommandLineArgument("compression-module", CommandLineArgument.ArgumentType.String, Strings.Options.CompressionmoduleShort, Strings.Options.CompressionmoduleLong, "zip"),

            new CommandLineArgument("enable-module", CommandLineArgument.ArgumentType.String, Strings.Options.EnablemoduleShort, Strings.Options.EnablemoduleLong),
            new CommandLineArgument("disable-module", CommandLineArgument.ArgumentType.String, Strings.Options.DisablemoduleShort, Strings.Options.DisablemoduleLong),

            new CommandLineArgument("debug-output", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DebugoutputShort, Strings.Options.DebugoutputLong, "false"),
            new CommandLineArgument("debug-retry-errors", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DebugretryerrorsShort, Strings.Options.DebugretryerrorsLong, "false"),

            new CommandLineArgument("log-file", CommandLineArgument.ArgumentType.Path, Strings.Options.LogfileShort, Strings.Options.LogfileLong),
            new CommandLineArgument("log-file-log-level", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.LogfileloglevelShort, Strings.Options.LogfileloglevelLong, "Warning", null, Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType))),
            new CommandLineArgument("log-file-log-filter", CommandLineArgument.ArgumentType.String, Strings.Options.LogfilelogfiltersShort, Strings.Options.LogfilelogfiltersLong(System.IO.Path.PathSeparator.ToString()), null),
            new CommandLineArgument("log-file-log-ignore", CommandLineArgument.ArgumentType.String, Strings.Options.LogfilelogignoreShort, Strings.Options.LogfilelogignoreLong(System.IO.Path.PathSeparator.ToString()), null),

            new CommandLineArgument("console-log-level", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.ConsoleloglevelShort, Strings.Options.ConsoleloglevelLong, "Warning", null, Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType))),
            new CommandLineArgument("console-log-filter", CommandLineArgument.ArgumentType.String, Strings.Options.ConsolelogfiltersShort, Strings.Options.ConsolelogfiltersLong(System.IO.Path.PathSeparator.ToString()), null),
            new CommandLineArgument("console-log-ignore", CommandLineArgument.ArgumentType.String, Strings.Options.ConsolelogignoreShort, Strings.Options.ConsolelogignoreLong(System.IO.Path.PathSeparator.ToString()), null),

            new CommandLineArgument("log-level", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.LoglevelShort, Strings.Options.LoglevelLong, "Warning", null, Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType)), Strings.Options.LogLevelDeprecated("log-file-log-level", "console-log-level")),
            new CommandLineArgument("suppress-warnings", CommandLineArgument.ArgumentType.String, Strings.Options.SuppresswarningsShort, Strings.Options.SuppresswarningsLong),

            new CommandLineArgument("log-http-requests", CommandLineArgument.ArgumentType.Boolean, Strings.Options.LoghttprequestsShort, Strings.Options.LoghttprequestsLong, "false"),
            new CommandLineArgument("log-socket-data", CommandLineArgument.ArgumentType.Integer, Strings.Options.LogsocketdataShort, Strings.Options.LogsocketdataLong, "-1"),

            new CommandLineArgument("profile-all-database-queries", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ProfilealldatabasequeriesShort, Strings.Options.ProfilealldatabasequeriesLong, "false"),
            new CommandLineArgument("store-metadata-content-in-database", CommandLineArgument.ArgumentType.Boolean, Strings.Options.StoremetadatacontentindatabaseShort, Strings.Options.StoremetadatacontentindatabaseLong, "false"),

            new CommandLineArgument("list-verify-uploads", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ListverifyuploadsShort, Strings.Options.ListverifyuploadsLong, "false"),
            new CommandLineArgument("allow-sleep", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllowsleepShort, Strings.Options.AllowsleepLong, "false"),
            new CommandLineArgument("use-background-io-priority", CommandLineArgument.ArgumentType.Boolean, Strings.Options.UsebackgroundiopriorityShort, Strings.Options.UsebackgroundiopriorityLong, "false"),
            new CommandLineArgument("no-connection-reuse", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NoconnectionreuseShort, Strings.Options.NoconnectionreuseLong, "false"),

            new CommandLineArgument("quota-size", CommandLineArgument.ArgumentType.Size, Strings.Options.QuotasizeShort, Strings.Options.QuotasizeLong),
            new CommandLineArgument("quota-warning-threshold", CommandLineArgument.ArgumentType.Integer, Strings.Options.QuotaWarningThresholdShort, Strings.Options.QuotaWarningThresholdLong, DEFAULT_QUOTA_WARNING_THRESHOLD.ToString()),
            new CommandLineArgument("quota-disable", CommandLineArgument.ArgumentType.Boolean, Strings.Options.QuotaDisableShort, Strings.Options.QuotaDisableLong("quota-size"), "false"),

            new CommandLineArgument("symlink-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.SymlinkpolicyShort, Strings.Options.SymlinkpolicyLong("store", "ignore", "follow"), Enum.GetName(typeof(SymlinkStrategy), SymlinkStrategy.Store), null, Enum.GetNames(typeof(SymlinkStrategy))),
            new CommandLineArgument("hardlink-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.HardlinkpolicyShort, Strings.Options.HardlinkpolicyLong("first", "all", "none"), Enum.GetName(typeof(HardlinkStrategy), HardlinkStrategy.All), null, Enum.GetNames(typeof(HardlinkStrategy))),
            new CommandLineArgument("exclude-files-attributes", CommandLineArgument.ArgumentType.String, Strings.Options.ExcludefilesattributesShort, Strings.Options.ExcludefilesattributesLong(Enum.GetNames(typeof(System.IO.FileAttributes)))),
            new CommandLineArgument("compression-extension-file", CommandLineArgument.ArgumentType.Path, Strings.Options.CompressionextensionfileShort, Strings.Options.CompressionextensionfileLong(DEFAULT_COMPRESSED_EXTENSION_FILE), DEFAULT_COMPRESSED_EXTENSION_FILE),

            new CommandLineArgument("machine-id", CommandLineArgument.ArgumentType.String, Strings.Options.MachineidShort, Strings.Options.MachineidLong, Library.AutoUpdater.DataFolderManager.InstallID),
            new CommandLineArgument("machine-name", CommandLineArgument.ArgumentType.String, Strings.Options.MachinenameShort, Strings.Options.MachinenameLong, Library.AutoUpdater.DataFolderManager.MachineName),
            new CommandLineArgument("backup-id", CommandLineArgument.ArgumentType.String, Strings.Options.BackupidShort, Strings.Options.BackupidLong, ""),
            new CommandLineArgument("backup-name", CommandLineArgument.ArgumentType.String, Strings.Options.BackupnameShort, Strings.Options.BackupnameLong, DefaultBackupName),
            new CommandLineArgument("next-scheduled-run", CommandLineArgument.ArgumentType.String, Strings.Options.NextscheduledrunShort, Strings.Options.NextscheduledrunLong),

            new CommandLineArgument("verbose", CommandLineArgument.ArgumentType.Boolean, Strings.Options.VerboseShort, Strings.Options.VerboseLong, "false", null, null, Strings.Options.VerboseDeprecated),
            new CommandLineArgument("full-result", CommandLineArgument.ArgumentType.Boolean, Strings.Options.FullresultShort, Strings.Options.FullresultLong, "false"),

            new CommandLineArgument("overwrite", CommandLineArgument.ArgumentType.Boolean, Strings.Options.OverwriteShort, Strings.Options.OverwriteLong, "false"),

            new CommandLineArgument("dbpath", CommandLineArgument.ArgumentType.Path, Strings.Options.DbpathShort, Strings.Options.DbpathLong),
            new CommandLineArgument("blocksize", CommandLineArgument.ArgumentType.Size, Strings.Options.BlocksizeShort, Strings.Options.BlocksizeLong, DEFAULT_BLOCKSIZE),
            new CommandLineArgument("file-read-buffer-size", CommandLineArgument.ArgumentType.Size, Strings.Options.FilereadbuffersizeShort, Strings.Options.FilereadbuffersizeLong, "0kb", null, null, Strings.Options.FilereadbuffersizeDeprecated),
            new CommandLineArgument("skip-metadata", CommandLineArgument.ArgumentType.Boolean, Strings.Options.SkipmetadataShort, Strings.Options.SkipmetadataLong, "false"),
            new CommandLineArgument("restore-permissions", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RestorepermissionsShort, Strings.Options.RestorepermissionsLong, "false"),
            new CommandLineArgument("skip-restore-verification", CommandLineArgument.ArgumentType.Boolean, Strings.Options.SkiprestoreverificationShort, Strings.Options.SkiprestoreverificationLong, "false"),
            new CommandLineArgument("disable-filepath-cache", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisablefilepathcacheShort, Strings.Options.DisablefilepathcacheLong, "true", null, null, Strings.Options.DisablefilepathcacheDeprecated),
            new CommandLineArgument("changed-files", CommandLineArgument.ArgumentType.Path, Strings.Options.ChangedfilesShort, Strings.Options.ChangedfilesLong),
            new CommandLineArgument("deleted-files", CommandLineArgument.ArgumentType.Path, Strings.Options.DeletedfilesShort, Strings.Options.DeletedfilesLong("changed-files")),
            new CommandLineArgument("disable-synthetic-filelist", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisablesyntheticfilelistShort, Strings.Options.DisablesyntehticfilelistLong, "false"),

            new CommandLineArgument("threshold", CommandLineArgument.ArgumentType.Integer, Strings.Options.ThresholdShort, Strings.Options.ThresholdLong, DEFAULT_THRESHOLD.ToString()),
            new CommandLineArgument("index-file-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.IndexfilepolicyShort, Strings.Options.IndexfilepolicyLong, IndexFileStrategy.Full.ToString(), null, Enum.GetNames(typeof(IndexFileStrategy))),
            new CommandLineArgument("no-backend-verification", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NobackendverificationShort, Strings.Options.NobackendverificationLong, "false"),
            new CommandLineArgument("backup-test-samples", CommandLineArgument.ArgumentType.Integer, Strings.Options.BackendtestsamplesShort, Strings.Options.BackendtestsamplesLong("no-backend-verification"), DEFAULT_BACKUP_TEST_SAMPLES.ToString()),
            new CommandLineArgument("backup-test-percentage", CommandLineArgument.ArgumentType.Decimal, Strings.Options.BackendtestpercentageShort, Strings.Options.BackendtestpercentageLong, "0.1"),
            new CommandLineArgument("full-remote-verification", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.FullremoteverificationShort, Strings.Options.FullremoteverificationLong("no-backend-verification"), Enum.GetName(typeof(RemoteTestStrategy), RemoteTestStrategy.False), null, Enum.GetNames(typeof(RemoteTestStrategy))),
            new CommandLineArgument("dont-replace-faulty-index-files", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ReplaceFaultyIndexFilesShort, Strings.Options.ReplaceFaultyIndexFilesLong, "false"),

            new CommandLineArgument("dry-run", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DryrunShort, Strings.Options.DryrunLong, "false", new string[] { "dryrun" }),

            new CommandLineArgument("block-hash-algorithm", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.BlockhashalgorithmShort, Strings.Options.BlockhashalgorithmLong, DEFAULT_BLOCK_HASH_ALGORITHM, null, HashFactory.GetSupportedHashes()),
            new CommandLineArgument("file-hash-algorithm", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.FilehashalgorithmShort, Strings.Options.FilehashalgorithmLong, DEFAULT_FILE_HASH_ALGORITHM, null, HashFactory.GetSupportedHashes()),

            new CommandLineArgument("no-auto-compact", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NoautocompactShort, Strings.Options.NoautocompactLong, "false"),
            new CommandLineArgument("small-file-size", CommandLineArgument.ArgumentType.Size, Strings.Options.SmallfilesizeShort, Strings.Options.SmallfilesizeLong),
            new CommandLineArgument("small-file-max-count", CommandLineArgument.ArgumentType.Integer, Strings.Options.SmallfilemaxcountShort, Strings.Options.SmallfilemaxcountLong, DEFAULT_SMALL_FILE_MAX_COUNT.ToString()),

            new CommandLineArgument("patch-with-local-blocks", CommandLineArgument.ArgumentType.Boolean, Strings.Options.PatchwithlocalblocksShort, Strings.Options.PatchwithlocalblocksLong, "false", null, null, Strings.Options.PatchwithlocalblocksDeprecated("restore-with-local-blocks")),
            new CommandLineArgument("no-local-db", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NolocaldbShort, Strings.Options.NolocaldbLong, "false"),
            new CommandLineArgument("dont-compress-restore-paths", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DontcompressrestorepathsShort, Strings.Options.DontcompressrestorepathsLong, "false"),
            new CommandLineArgument("allow-restore-outside-target-directory", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllowRestoreOutsideTargetDirectoryShort, Strings.Options.AllowRestoreOutsideTargetDirectoryLong, "false"),

            new CommandLineArgument("keep-versions", CommandLineArgument.ArgumentType.Integer, Strings.Options.KeepversionsShort, Strings.Options.KeepversionsLong, DEFAULT_KEEP_VERSIONS.ToString()),
            new CommandLineArgument("keep-time", CommandLineArgument.ArgumentType.Timespan, Strings.Options.KeeptimeShort, Strings.Options.KeeptimeLong),
            new CommandLineArgument("remote-file-lock-duration", CommandLineArgument.ArgumentType.Timespan, Strings.Options.RemotefilelockdurationShort, Strings.Options.RemotefilelockdurationLong),
            new CommandLineArgument("retention-policy", CommandLineArgument.ArgumentType.String, Strings.Options.RetentionPolicyShort, Strings.Options.RetentionPolicyLong),
            new CommandLineArgument("upload-verification-file", CommandLineArgument.ArgumentType.Boolean, Strings.Options.UploadverificationfileShort, Strings.Options.UploadverificationfileLong, "false"),
            new CommandLineArgument("allow-passphrase-change", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllowpassphrasechangeShort, Strings.Options.AllowpassphrasechangeLong, "false"),
            new CommandLineArgument("no-local-blocks", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NolocalblocksShort, Strings.Options.NolocalblocksLong, "false", null, null, Strings.Options.NolocalblocksDeprecated("restore-with-local-blocks")),
            new CommandLineArgument("restore-with-local-blocks", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RestorewithlocalblocksShort, Strings.Options.RestorewithlocalblocksLong, "false"),
            new CommandLineArgument("full-block-verification", CommandLineArgument.ArgumentType.Boolean, Strings.Options.FullblockverificationShort, Strings.Options.FullblockverificationLong, "false"),
            new CommandLineArgument("allow-full-removal", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllowfullremovalShort, Strings.Options.AllowfullremovalLong, "false"),

            new CommandLineArgument("log-retention", CommandLineArgument.ArgumentType.Timespan, Strings.Options.LogretentionShort, Strings.Options.LogretentionLong, DEFAULT_LOG_RETENTION),

            new CommandLineArgument("repair-only-paths", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RepaironlypathsShort, Strings.Options.RepaironlypathsLong, "false"),
            new CommandLineArgument("repair-force-block-use", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RepaironlypathsShort, Strings.Options.RepaironlypathsLong, "false"),
            new CommandLineArgument("repair-ignore-outdated-database", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RepairignoreoutdateddatabaseShort, Strings.Options.RepairignoreoutdateddatabaseLong, "false"),
            new CommandLineArgument("force-locale", CommandLineArgument.ArgumentType.String, Strings.Options.ForcelocaleShort, Strings.Options.ForcelocaleLong),
            new CommandLineArgument("force-actual-date", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ForceActualDateShort, Strings.Options.ForceActualDateLong, "false"),

            new CommandLineArgument("concurrency-max-threads", CommandLineArgument.ArgumentType.Integer, Strings.Options.ConcurrencymaxthreadsShort, Strings.Options.ConcurrencymaxthreadsLong, "0"),
            new CommandLineArgument("concurrency-block-hashers", CommandLineArgument.ArgumentType.Integer, Strings.Options.ConcurrencyblockhashersShort, Strings.Options.ConcurrencyblockhashersLong, DEFAULT_BLOCK_HASHERS.ToString()),
            new CommandLineArgument("concurrency-compressors", CommandLineArgument.ArgumentType.Integer, Strings.Options.ConcurrencycompressorsShort, Strings.Options.ConcurrencycompressorsLong, DEFAULT_COMPRESSORS.ToString()),
            new CommandLineArgument("concurrency-fileprocessors", CommandLineArgument.ArgumentType.Integer, Strings.Options.ConcurrencyfileprocessorsShort, Strings.Options.ConcurrencyfileprocessorsLong, DEFAULT_FILE_PROCESSORS.ToString()),

            new CommandLineArgument("auto-vacuum", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AutoVacuumShort, Strings.Options.AutoVacuumLong, "false"),
            new CommandLineArgument("disable-file-scanner", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisablefilescannerShort, Strings.Options.DisablefilescannerLong, "false"),
            new CommandLineArgument("disable-filelist-consistency-checks", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisablefilelistconsistencychecksShort, Strings.Options.DisablefilelistconsistencychecksLong, "false"),
            new CommandLineArgument("disable-on-battery", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisableOnBatteryShort, Strings.Options.DisableOnBatteryLong, "false"),

            new CommandLineArgument("exclude-empty-folders", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ExcludeemptyfoldersShort, Strings.Options.ExcludeemptyfoldersLong, "false"),
            new CommandLineArgument("ignore-filenames", CommandLineArgument.ArgumentType.Path, Strings.Options.IgnorefilenamesShort, Strings.Options.IgnorefilenamesLong, "CACHEDIR.TAG"),
            new CommandLineArgument("restore-symlink-metadata", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RestoresymlinkmetadataShort, Strings.Options.RestoresymlinkmetadataLong, "false"),
            new CommandLineArgument("rebuild-missing-dblock-files", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RebuildmissingdblockfilesShort, Strings.Options.RebuildmissingdblockfilesLong, "false"),
            new CommandLineArgument("disable-partial-dblock-recovery", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisablePartialDblockRecoveryShort, Strings.Options.DisablePartialDblockRecoveryLong, "false"),
            new CommandLineArgument("disable-replace-missing-metadata", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisableReplaceMissingMetadataShort, Strings.Options.DisableReplaceMissingMetadataLong, "false"),
            new CommandLineArgument("reduced-purge-statistics", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ReducedPurgeStatisticsShort, Strings.Options.ReducedPurgeStatisticsLong, "false"),
            new CommandLineArgument("repair-refresh-lock-info", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RepairRefreshLockInfoShort, Strings.Options.RepairRefreshLockInfoLong, "false"),
            new CommandLineArgument("refresh-lock-info-complete", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RefreshLockInfoCompleteShort, Strings.Options.RefreshLockInfoCompleteLong, "false"),

            new CommandLineArgument("auto-compact-interval", CommandLineArgument.ArgumentType.Timespan, Strings.Options.AutoCompactIntervalShort, Strings.Options.AutoCompactIntervalLong, "0m"),
            new CommandLineArgument("auto-vacuum-interval", CommandLineArgument.ArgumentType.Timespan, Strings.Options.AutoVacuumIntervalShort, Strings.Options.AutoVacuumIntervalLong, "0m"),

            new CommandLineArgument("secret-provider", CommandLineArgument.ArgumentType.Password, Strings.Options.SecretProviderShort, Strings.Options.SecretProviderLong(Library.AutoUpdater.PackageHelper.GetExecutableName(AutoUpdater.PackageHelper.NamedExecutable.SecretTool))),
            new CommandLineArgument("secret-provider-pattern", CommandLineArgument.ArgumentType.String, Strings.Options.SecretProviderPatternShort, Strings.Options.SecretProviderPatternLong, SecretProviderHelper.DEFAULT_PATTERN),
            new CommandLineArgument("secret-provider-cache", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.SecretProviderCacheShort, Strings.Options.SecretProviderCacheLong, Enum.GetName(SecretProviderHelper.CachingLevel.None), null, Enum.GetNames(typeof(SecretProviderHelper.CachingLevel))),
            new CommandLineArgument("cpu-intensity", CommandLineArgument.ArgumentType.Integer, Strings.Options.CPUIntensityShort, Strings.Options.CPUIntensityLong, "10", null, ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"]),

            new CommandLineArgument("restore-cache-max", CommandLineArgument.ArgumentType.Size, Strings.Options.RestoreCacheMaxShort, Strings.Options.RestoreCacheMaxLong, DEFAULT_RESTORE_CACHE_MAX),
            new CommandLineArgument("restore-cache-evict", CommandLineArgument.ArgumentType.Integer, Strings.Options.RestoreCacheEvictShort, Strings.Options.RestoreCacheEvictLong, DEFAULT_RESTORE_CACHE_EVICT.ToString()),
            new CommandLineArgument("restore-file-processors", CommandLineArgument.ArgumentType.Integer, Strings.Options.RestoreFileprocessorsShort, Strings.Options.RestoreFileprocessorsLong, DEFAULT_RESTORE_FILE_PROCESSORS.ToString()),
            new CommandLineArgument("restore-legacy", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RestoreLegacyShort, Strings.Options.RestoreLegacyLong, "false"),
            new CommandLineArgument("restore-preallocate-size", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RestorePreallocateSizeShort, Strings.Options.RestorePreallocateSizeLong, "false"),
            new CommandLineArgument("restore-volume-cache-hint", CommandLineArgument.ArgumentType.Size, Strings.Options.RestoreVolumeCacheHintShort, Strings.Options.RestoreVolumeCacheHintLong, DEFAULT_RESTORE_VOLUME_CACHE_HINT),
            new CommandLineArgument("restore-volume-cache-min-free", CommandLineArgument.ArgumentType.Size, Strings.Options.RestoreVolumeCacheMinFreeShort, Strings.Options.RestoreVolumeCacheMinFreeLong, DEFAULT_RESTORE_VOLUME_CACHE_MIN_FREE),
            new CommandLineArgument("restore-volume-decompressors", CommandLineArgument.ArgumentType.Integer, Strings.Options.RestoreVolumeDecompressorsShort, Strings.Options.RestoreVolumeDecompressorsLong, DEFAULT_RESTORE_VOLUME_DECOMPRESSORS.ToString()),
            new CommandLineArgument("restore-volume-decryptors", CommandLineArgument.ArgumentType.Integer, Strings.Options.RestoreVolumeDecryptorsShort, Strings.Options.RestoreVolumeDecryptorsLong, DEFAULT_RESTORE_VOLUME_DECRYPTORS.ToString()),
            new CommandLineArgument("restore-volume-downloaders", CommandLineArgument.ArgumentType.Integer, Strings.Options.RestoreVolumeDownloadersShort, Strings.Options.RestoreVolumeDownloadersLong, DEFAULT_RESTORE_VOLUME_DOWNLOADERS.ToString()),
            new CommandLineArgument("restore-channel-buffer-size", CommandLineArgument.ArgumentType.Integer, Strings.Options.RestoreChannelBufferSizeShort, Strings.Options.RestoreChannelBufferSizeLong, DEFAULT_RESTORE_CHANNEL_BUFFER_SIZE.ToString()),
            new CommandLineArgument("internal-profiling", CommandLineArgument.ArgumentType.Boolean, Strings.Options.InternalProfilingShort, Strings.Options.InternalProfilingLong, "false"),
            new CommandLineArgument("ignore-update-if-version-exists", CommandLineArgument.ArgumentType.Boolean, Strings.Options.IgnoreUpdateIfVersionExistsShort, Strings.Options.IgnoreUpdateIfVersionExistsLong, "false"),

            .. GetOSConditionalCommands()
        ];

        /// <summary>
        /// Gets or sets the current main action of the instance
        /// </summary>
        public OperationMode MainAction
        {
            get
            {
                var value = m_options.GetValueOrDefault("main-action");
                return string.IsNullOrEmpty(value)
                    ? ((OperationMode)(-1))
                    : (OperationMode)Enum.Parse(typeof(OperationMode), value);
            }
            set { m_options["main-action"] = value.ToString(); }
        }

        /// <summary>
        /// Gets the size of each volume in bytes
        /// </summary>
        public long VolumeSize
        {
            get
            {
                var volsize = GetString("dblock-size", DEFAULT_VOLUME_SIZE);
#if DEBUG
                return Math.Max(1024 * 10, Library.Utility.Sizeparser.ParseSize(volsize, "mb"));
#else
                return Math.Max(1024 * 1024, Library.Utility.Sizeparser.ParseSize(volsize, "mb"));
#endif
            }
        }

        /// <summary>
        /// Gets the maximum size of a single file
        /// </summary>
        public long SkipFilesLargerThan
        {
            get
            {
                var value = m_options.GetValueOrDefault("skip-files-larger-than");
                return string.IsNullOrWhiteSpace(value)
                    ? long.MaxValue
                    : Library.Utility.Sizeparser.ParseSize(value, "mb");
            }
        }

        /// <summary>
        /// A value indicating if orphan files are deleted automatically
        /// </summary>
        public bool AutoCleanup => GetBool("auto-cleanup");

        /// <summary>
        /// A value indicating if we are running in unittest mode
        /// </summary>
        public bool UnittestMode => GetBool("unittest-mode");


        /// <summary>
        /// Gets a list of files to add to the signature volumes
        /// </summary>
        public string? ControlFiles => m_options.GetValueOrDefault("control-files");

        /// <summary>
        /// A value indicating if file hash checks are skipped
        /// </summary>
        public bool SkipFileHashChecks => GetBool("skip-file-hash-checks");

        /// <summary>
        /// A value indicating if the manifest files are not read
        /// </summary>
        public bool DontReadManifests => GetBool("dont-read-manifests");

        /// <summary>
        /// Gets the backup that should be restored
        /// </summary>
        public DateTime Time
        {
            get
            {
                var value = m_options.GetValueOrDefault("time");
                return string.IsNullOrEmpty(value)
                    ? new DateTime(0, DateTimeKind.Utc)
                    : Library.Utility.Timeparser.ParseTimeInterval(value, DateTime.Now);
            }
        }

        /// <summary>
        /// Gets the versions the restore or list operation is limited to
        /// </summary>
        public long[]? Version
        {
            get
            {
                m_options.TryGetValue("version", out var v);
                if (string.IsNullOrEmpty(v))
                    return null;

                var versions = v.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (v.Length == 0)
                    return null;

                var res = new List<long>();
                foreach (var n in versions)
                    if (n.Contains('-'))
                    {
                        //TODO: Throw errors if too many entries?
                        var parts = n.Split(new char[] { '-' }, StringSplitOptions.RemoveEmptyEntries).Select(x => Convert.ToInt64(x.Trim())).ToArray();
                        for (var i = Math.Min(parts[0], parts[1]); i <= Math.Max(parts[0], parts[1]); i++)
                            res.Add(i);
                    }
                    else
                        res.Add(Convert.ToInt64(n));

                return res.ToArray();
            }
        }

        /// <summary>
        /// A value indicating if all versions are listed
        /// </summary>
        public bool AllVersions => GetBool("all-versions");

        /// <summary>
        /// A value indicating if only the largest common prefix is returned
        /// </summary>
        public bool ListPrefixOnly => GetBool("list-prefix-only");

        /// <summary>
        /// A value indicating if only folder contents are returned
        /// </summary>
        public bool ListFolderContents => GetBool("list-folder-contents");

        /// <summary>
        /// A value indicating that only filesets are returned
        /// </summary>
        public bool ListSetsOnly => GetBool("list-sets-only");

        /// <summary>
        /// A value indicating if file time checks are skipped
        /// </summary>
        public bool DisableFiletimeCheck => GetBool("disable-filetime-check");

        /// <summary>
        /// A value indicating if file time checks are skipped
        /// </summary>
        public bool CheckFiletimeOnly => GetBool("check-filetime-only");

        /// <summary>
        /// A value indicating if USN numbers are used to get list of changed files
        /// </summary>
        //public bool DisableUSNDiffCheck { get { return GetBool("disable-usn-diff-check"); } }

        /// <summary>
        /// A value indicating if time tolerance is disabled
        /// </summary>
        public bool DisableTimeTolerance => GetBool("disable-time-tolerance");

        /// <summary>
        /// Gets a value indicating whether a temporary folder has been specified
        /// </summary>
        public bool HasTempDir => m_options.ContainsKey("tempdir") && !string.IsNullOrEmpty(m_options["tempdir"]);

        /// <summary>
        /// Gets the folder where temporary files are stored
        /// </summary>
        public string TempDir => GetString("tempdir", TempFolder.SystemTempPath);

        /// <summary>
        /// Gets a value indicating whether the user has forced the locale
        /// </summary>
        public bool HasForcedLocale => m_options.ContainsKey("force-locale");

        /// <summary>
        /// Gets the forced locale for the current user
        /// </summary>
        public CultureInfo ForcedLocale
        {
            get
            {
                if (!m_options.ContainsKey("force-locale"))
                    return CultureInfo.CurrentCulture;

                var localestring = m_options["force-locale"];
                if (string.IsNullOrWhiteSpace(localestring))
                    return CultureInfo.InvariantCulture;

                return new CultureInfo(localestring);
            }
        }

        /// <summary>
        /// A value indicating if missing folders should be created automatically
        /// </summary>
        public bool AutocreateFolders => !GetBool("disable-autocreate-folder");

        /// <summary>
        /// Gets the backup prefix
        /// </summary>
        public string Prefix => GetString("prefix", "duplicati");

        /// <summary>
        /// Gets the number of old backups to keep
        /// </summary>
        public int KeepVersions => Math.Max(0, GetInt("keep-versions", DEFAULT_KEEP_VERSIONS));

        /// <summary>
        /// Gets the timelimit for removal
        /// </summary>
        public DateTime KeepTime
        {
            get
            {
                m_options.TryGetValue("keep-time", out var v);

                if (string.IsNullOrEmpty(v))
                    return new DateTime(0);

                var tolerance =
                    this.DisableTimeTolerance ?
                    TimeSpan.FromSeconds(0) :
                    TimeSpan.FromSeconds(Math.Min(Library.Utility.Timeparser.ParseTimeSpan(v).TotalSeconds / 100, 60.0 * 60.0));

                return Library.Utility.Timeparser.ParseTimeInterval(v, DateTime.Now, true) - tolerance;
            }
        }

        /// <summary>
        /// Gets the configured object lock duration for uploaded files, if specified
        /// </summary>
        public TimeSpan? RemoteFileLockDuration
        {
            get
            {
                m_options.TryGetValue("remote-file-lock-duration", out var value);

                if (string.IsNullOrWhiteSpace(value))
                    return null;

                return Library.Utility.Timeparser.ParseTimeSpan(value);
            }
        }

        /// <summary>
        /// Gets the time frames and intervals for the retention policy
        /// </summary>
        public List<RetentionPolicyValue> RetentionPolicy
        {
            get
            {
                var retentionPolicyConfig = new List<RetentionPolicyValue>();

                m_options.TryGetValue("retention-policy", out var v);
                if (string.IsNullOrEmpty(v))
                    return retentionPolicyConfig;

                var periodIntervalStrings = v.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var periodIntervalString in periodIntervalStrings)
                    retentionPolicyConfig.Add(RetentionPolicyValue.CreateFromString(periodIntervalString));

                return retentionPolicyConfig;
            }
        }

        /// <summary>
        /// Gets the encryption passphrase
        /// </summary>
        public string? Passphrase => GetString("passphrase", null);

        /// <summary>
        /// Gets the new encryption passphrase
        /// </summary>
        public string? NewPassphrase => GetString("new-passphrase", null);

        /// <summary>
        /// A value indicating if backups are not encrypted
        /// </summary>
        public bool NoEncryption => GetBool("no-encryption");

        /// <summary>
        /// Gets the module used for encryption
        /// </summary>
        public string? EncryptionModule => NoEncryption ? null : GetString("encryption-module", "aes");

        /// <summary>
        /// Gets the module used for compression
        /// </summary>
        public string CompressionModule => GetString("compression-module", "zip");

        /// <summary>
        /// Gets the number of time to retry transmission if it fails
        /// </summary>
        public int NumberOfRetries => Math.Max(0, GetInt("number-of-retries", DEFAULT_NUMBER_OF_RETRIES));

        /// <summary>
        /// A value indicating if backups are transmitted on a separate thread
        /// </summary>
        public bool SynchronousUpload => GetBool("synchronous-upload");

        /// <summary>
        /// A value indicating if system is allowed to enter sleep power states during backup/restore
        /// </summary>
        public bool AllowSleep => GetBool("allow-sleep");

        /// <summary>
        /// A value indicating if system should use the low-priority IO during backup/restore
        /// </summary>
        public bool UseBackgroundIOPriority => GetBool("use-background-io-priority");

        /// <summary>
        /// A value indicating if use of the streaming interface is disallowed
        /// </summary>
        public bool DisableStreamingTransfers => GetBool("disable-streaming-transfers");

        /// <summary>
        /// The maximum time to allow inactivity before a connection is closed.
        /// Returns <c>Timeout.Infinite</c> if disabled.
        /// </summary>
        public int ReadWriteTimeout
        {
            get
            {
                var ts = Library.Utility.Utility.ParseTimespanOption(m_options, "read-write-timeout", DEFAULT_READ_WRITE_TIMEOUT);
                return ts.Ticks <= 0 ? Timeout.Infinite : (int)ts.TotalMilliseconds;
            }
        }

        /// <summary>
        /// Gets the delay period to retry uploads
        /// </summary>
        public TimeSpan RetryDelay => Library.Utility.Utility.ParseTimespanOption(m_options, "retry-delay", DEFAULT_RETRY_DELAY);

        /// <summary>
        /// Gets whether exponential backoff is enabled
        /// </summary>
        public bool RetryWithExponentialBackoff => GetBool("retry-with-exponential-backoff");

        /// <summary>
        /// Gets the max upload speed in bytes pr. second
        /// </summary>
        public long MaxUploadPrSecond
        {
            get
            {
                lock (m_lock)
                    return GetSize("throttle-upload", "kb", "0b");
            }
            set
            {
                lock (m_lock)
                    m_options["throttle-upload"] = value <= 0 ? "" : $"{value}b";
            }
        }

        /// <summary>
        /// Gets or sets the max download speed in bytes pr. second
        /// </summary>
        public long MaxDownloadPrSecond
        {
            get
            {
                lock (m_lock)
                    return GetSize("throttle-download", "kb", "0b");
            }
            set
            {
                lock (m_lock)
                    m_options["throttle-download"] = value <= 0 ? "" : $"{value}b";
            }
        }

        /// <summary>
        /// A value indicating if the throttling is disabled
        /// </summary>
        public bool DisableThrottle => GetBool("throttle-disabled");

        /// <summary>
        /// The backends where the throttling is disabled
        /// </summary>
        public HashSet<string> ThrottleDisabledBackends
            => GetString("throttle-disabled-backends", DEFAULT_THROTTLE_DISABLED_BACKENDS)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();

        /// <summary>
        /// A value indicating if the backup is a full backup
        /// </summary>
        public bool AllowFullRemoval => GetBool("allow-full-removal");

        /// <summary>
        /// A value indicating if debug output is enabled
        /// </summary>
        public bool DebugOutput => GetBool("debug-output");

        /// <summary>
        /// A value indicating if unchanged backups are uploaded
        /// </summary>
        public bool UploadUnchangedBackups => GetBool("upload-unchanged-backups");

        /// <summary>
        /// Gets a list of modules that should be loaded
        /// </summary>
        public string[] EnableModules
            => m_options.GetValueOrDefault("enable-module")?.Trim().ToLower(CultureInfo.InvariantCulture).Split(',') ?? [];

        /// <summary>
        /// Gets a list of modules that should not be loaded
        /// </summary>
        public string[] DisableModules
            => m_options.GetValueOrDefault("disable-module")?.Trim().ToLower(CultureInfo.InvariantCulture).Split(',') ?? [];

        /// <summary>
        /// Gets the snapshot strategy to use
        /// </summary>
        public OptimizationStrategy SnapShotStrategy => GetEnum("snapshot-policy", DEFAULT_SNAPSHOT_POLICY);

        /// <summary>
        /// Gets the snapshot strategy to use
        /// </summary>
        [SupportedOSPlatform("windows")]
        public WindowsSnapshotProvider SnapShotProvider
            => GetEnum("snapshot-provider", WindowsSnapshot.DEFAULT_WINDOWS_SNAPSHOT_PROVIDER);

        /// <summary>
        /// Gets the BackupRead strategy to use
        /// </summary>
        public OptimizationStrategy BackupReadStrategy => GetEnum("backupread-policy", DEFAULT_BACKUPREAD_POLICY);

        /// <summary>
        /// Gets a flag indicating if advisory locking should be ignored
        /// </summary>
        public bool IgnoreAdvisoryLocking => GetBool("ignore-advisory-locking");

        /// <summary>
        /// Gets the symlink strategy to use
        /// </summary>
        public SymlinkStrategy SymlinkPolicy
            => GetEnum("symlink-policy", SymlinkStrategy.Store);

        /// <summary>
        /// Gets the hardlink strategy to use
        /// </summary>
        public HardlinkStrategy HardlinkPolicy
            => GetEnum("hardlink-policy", HardlinkStrategy.All);

        /// <summary>
        /// Gets the update sequence number (USN) strategy to use
        /// </summary>
        public OptimizationStrategy UsnStrategy
            => GetEnum("usn-policy", OptimizationStrategy.Off);

        /// <summary>
        /// Gets the number of concurrent volume uploads allowed. Zero for unlimited.
        /// </summary>
        public int AsynchronousConcurrentUploadLimit
            => GetInt("asynchronous-upload-limit", GetInt("asynchronous-concurrent-upload-limit", DEFAULT_ASYNCHRONOUS_UPLOAD_LIMIT));

        /// <summary>
        /// Gets the temporary folder to use for asynchronous transfers
        /// </summary>
        public string AsynchronousUploadFolder => GetString("asynchronous-upload-folder", TempDir);

        /// <summary>
        /// Gets the logfile filename
        /// </summary>
        public string? Logfile => m_options.GetValueOrDefault("log-file");

        /// <summary>
        /// Gets the log-file detail level
        /// </summary>
        public Duplicati.Library.Logging.LogMessageType LogFileLoglevel
        {
            get
            {
                if (!m_options.TryGetValue("log-file-log-level", out var value))
                    value = null;

                if (string.IsNullOrWhiteSpace(value))
                    if (!m_options.TryGetValue("log-level", out value))
                        value = null;

                foreach (string s in Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType)))
                    if (s.Equals(value, StringComparison.OrdinalIgnoreCase))
                        return (Duplicati.Library.Logging.LogMessageType)Enum.Parse(typeof(Duplicati.Library.Logging.LogMessageType), s);

                if (Dryrun)
                    return Duplicati.Library.Logging.LogMessageType.DryRun;
                else
                    return Duplicati.Library.Logging.LogMessageType.Warning;
            }
        }

        /// <summary>
        /// Gets the filter used to suppress warning messages.
        /// </summary>
        public HashSet<string>? SuppressWarningsFilter
            => m_options.GetValueOrDefault("suppress-warnings")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A value indicating if HTTP requests should be logged
        /// </summary>
        public bool LogHttpRequests => GetBool("log-http-requests");

        /// <summary>
        /// Gets the number of bytes of socket data to include in logs, -1 disables logging
        /// </summary>
        public int LogSocketData => GetInt("log-socket-data", -1);

        /// <summary>
        /// Gets the filter used for log-file messages.
        /// </summary>
        /// <value>The log file filter.</value>
        public IFilter? LogFileLogFilter
            => FilterExpression.Combine(
                new FilterExpression(m_options.GetValueOrDefault("log-file-log-ignore")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)?.Select(x => $"*-{x}"), false),
                FilterExpression.ParseLogFilter(m_options.GetValueOrDefault("log-file-log-filter"))
            );

        /// <summary>
        /// Gets the filter used for console messages.
        /// </summary>
        /// <value>The log file filter.</value>
        public IFilter? ConsoleLogFilter
            => FilterExpression.Combine(
                new FilterExpression(m_options.GetValueOrDefault("console-log-ignore")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)?.Select(x => $"*-{x}"), false),
                FilterExpression.ParseLogFilter(m_options.GetValueOrDefault("console-log-filter"))
            );

        /// <summary>
        /// Gets the console log detail level
        /// </summary>
        public Duplicati.Library.Logging.LogMessageType ConsoleLoglevel
        {
            get
            {
                if (!m_options.TryGetValue("console-log-level", out var value))
                    value = null;

                if (string.IsNullOrWhiteSpace(value))
                    if (!m_options.TryGetValue("log-level", out value))
                        value = null;

                foreach (string s in Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType)))
                    if (s.Equals(value, StringComparison.OrdinalIgnoreCase))
                        return (Duplicati.Library.Logging.LogMessageType)Enum.Parse(typeof(Duplicati.Library.Logging.LogMessageType), s);

                if (Dryrun)
                    return Duplicati.Library.Logging.LogMessageType.DryRun;
                else
                    return Duplicati.Library.Logging.LogMessageType.Warning;
            }
        }

        /// <summary>
        /// A value indicating if all database queries should be logged
        /// </summary>
        public bool ProfileAllDatabaseQueries => GetBool("profile-all-database-queries");

        /// <summary>
        /// A value indicating if metadata content should be stored in the database
        /// </summary>
        public bool StoreMetadataContentInDatabase => GetBool("store-metadata-content-in-database");

        /// <summary>
        /// Gets the attribute filter used to exclude files and folders.
        /// </summary>
        public System.IO.FileAttributes FileAttributeFilter
            => Library.Utility.Utility.ParseFlagsOption(m_options, "exclude-files-attributes", default(System.IO.FileAttributes));

        /// <summary>
        /// A value indicating if server uploads are verified by listing the folder contents
        /// </summary>
        public bool ListVerifyUploads => GetBool("list-verify-uploads");

        /// <summary>
        /// A value indicating if connections cannot be re-used
        /// </summary>
        public bool NoConnectionReuse => GetBool("no-connection-reuse");

        /// <summary>
        /// A value indicating if the returned value should not be truncated
        /// </summary>
        public bool FullResult => GetBool("full-result");

        /// <summary>
        /// A value indicating restored files overwrite existing ones
        /// </summary>
        public bool Overwrite => GetBool("overwrite");

        /// <summary>
        /// Gets the total size in bytes that the backup should use, returns -1 if there is no upper limit
        /// </summary>
        public long QuotaSize
        {
            get
            {
                var value = m_options.GetValueOrDefault("quota-size");
                return string.IsNullOrEmpty(value) ? -1 : Library.Utility.Sizeparser.ParseSize(value, "mb");
            }
        }

        /// <summary>
        /// Gets the threshold at which a quota warning should be generated.
        /// </summary>
        /// <remarks>
        /// This is treated as a percentage, where a warning is given when the amount of free space is less than this percentage of the backup size.
        /// </remarks>
        public int QuotaWarningThreshold
            => GetInt("quota-warning-threshold", DEFAULT_QUOTA_WARNING_THRESHOLD);

        /// <summary>
        /// Gets a flag indicating that backup quota reported by the backend should be ignored
        /// </summary>
        /// This is necessary because in some cases the backend might report a wrong quota (especially with some Linux mounts).
        public bool QuotaDisable => GetBool("quota-disable");

        /// <summary>
        /// Gets the soft delete prefix
        /// </summary>
        public string? SoftDeletePrefix => GetString("soft-delete-prefix", null);

        /// <summary>
        /// Gets a flag indicating if the backend rename operation should be avoided
        /// </summary>
        public bool PreventBackendRename => GetBool("prevent-backend-rename");

        /// <summary>
        /// Gets the display name of the backup
        /// </summary>
        public string BackupName
        {
            get => GetString("backup-name", DefaultBackupName);
            set => m_options["backup-name"] = value;
        }

        /// <summary>
        /// Gets the ID of the backup
        /// </summary>
        public string? BackupId => m_options.GetValueOrDefault("backup-id");

        /// <summary>
        /// Gets the ID of the machine
        /// </summary>
        public string? MachineId => m_options.GetValueOrDefault("machine-id", Library.AutoUpdater.DataFolderManager.InstallID);

        /// <summary>
        /// Gets the path to the database
        /// </summary>
        public string? Dbpath
        {
            get => m_options.GetValueOrDefault("dbpath", null);
            set => m_options["dbpath"] = value;
        }

        /// <summary>
        /// Gets a value indicating whether a blocksize has been specified
        /// </summary>
        public bool HasBlocksize => !string.IsNullOrEmpty(m_options.GetValueOrDefault("blocksize"));

        /// <summary>
        /// Gets the size of file-blocks
        /// </summary>
        public int Blocksize
        {
            get
            {
                var blocksize = GetSize("blocksize", "kb", DEFAULT_BLOCKSIZE);
                if (blocksize > int.MaxValue || blocksize < 1024)
                    throw new ArgumentOutOfRangeException(nameof(blocksize), string.Format("The blocksize cannot be less than {0}, nor larger than {1}", 1024, int.MaxValue));

                return (int)blocksize;
            }
        }

        /// <summary>
        /// Cache for the block hash size value, to avoid creating new hash instances just to get the size
        /// </summary>
        private KeyValuePair<string, int> m_cachedBlockHashSize;

        /// <summary>
        /// Gets the size of the blockhash in bytes.
        /// </summary>
        /// <value>The size of the blockhash.</value>
        public int BlockhashSize
        {
            get
            {
                if (m_cachedBlockHashSize.Key != BlockHashAlgorithm)
                    m_cachedBlockHashSize = new KeyValuePair<string, int>(BlockHashAlgorithm, HashFactory.HashSizeBytes(BlockHashAlgorithm));

                return m_cachedBlockHashSize.Value;
            }
        }

        /// <summary>
        /// Gets a flag indicating if metadata for files and folders should be ignored
        /// </summary>
        public bool SkipMetadata => GetBool("skip-metadata");

        /// <summary>
        /// Gets a flag indicating if empty folders should be ignored
        /// </summary>
        public bool ExcludeEmptyFolders => GetBool("exclude-empty-folders");

        /// <summary>
        /// Gets a flag indicating if backup exclusion extended attributes should be ignored
        /// </summary>
        public bool DisableBackupExclusionXattr =>
            OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()
                ? GetBool("disable-backup-exclusion-xattr")
                : true; // Windows does not support xattrs, so disable looking for them

        /// <summary>
        /// Gets a flag indicating if during restores metadata should be applied to the symlink target.
        /// Setting this to true can result in errors if the target no longer exists.
        /// </summary>
        public bool RestoreSymlinkMetadata => GetBool("restore-symlink-metadata");

        /// <summary>
        /// Gets a flag indicating if permissions should be restored
        /// </summary>
        public bool RestorePermissions => GetBool("restore-permissions");


        /// <summary>
        /// Gets a flag indicating if file hashes are checked after a restore
        /// </summary>
        public bool PerformRestoredFileVerification => !GetBool("skip-restore-verification");

        /// <summary>
        /// Gets a flag indicating if synthetic filelist generation is disabled
        /// </summary>
        public bool DisableSyntheticFilelist => GetBool("disable-synthetic-filelist");

        /// <summary>
        /// Gets the compact threshold
        /// </summary>
        public long Threshold => GetLong("threshold", DEFAULT_THRESHOLD);

        /// <summary>
        /// Gets the size of small volumes
        /// </summary>
        public long SmallFileSize => GetSize("small-file-size", "mb", $"{this.VolumeSize / 5}b");

        /// <summary>
        /// Gets the maximum number of small volumes
        /// </summary>
        public long SmallFileMaxCount => GetLong("small-file-max-count", DEFAULT_SMALL_FILE_MAX_COUNT);

        /// <summary>
        /// List of files to check for changes
        /// </summary>
        public string[]? ChangedFilelist => m_options.GetValueOrDefault("changed-files")?.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>
        /// List of files to mark as deleted
        /// </summary>
        public string[]? DeletedFilelist => m_options.GetValueOrDefault("deleted-files")?.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>
        /// List of filenames that are used to exclude a folder
        /// </summary>
        public string[]? IgnoreFilenames
        {
            get
            {
                if (!m_options.TryGetValue("ignore-filenames", out var v))
                    v = "CACHEDIR.TAG";
                if (string.IsNullOrEmpty(v))
                    return null;

                return v.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            }
        }
        /// <summary>
        /// Alternate restore path
        /// </summary>
        public string? Restorepath => m_options.GetValueOrDefault("restore-path");

        /// <summary>
        /// Gets the index file usage method
        /// </summary>
        public IndexFileStrategy IndexfilePolicy => GetEnum("index-file-policy", IndexFileStrategy.Full);

        /// <summary>
        /// Gets a flag indicating if the check for files on the remote storage should be omitted
        /// </summary>
        public bool NoBackendverification => GetBool("no-backend-verification");

        /// <summary>
        /// Gets the percentage of samples to test during a backup operation
        /// </summary>
        public decimal BackupTestPercentage
        {
            get
            {
                m_options.TryGetValue("backup-test-percentage", out var s);
                if (string.IsNullOrEmpty(s))
                    return 0.1m;

                decimal percentage;
                try
                {
                    percentage = decimal.Parse(s, CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException("The value provided for the backup-test-percentage option must lie between 0 and 100.", ex);
                }

                if ((percentage < 0) || (percentage > 100))
                {
                    throw new ArgumentOutOfRangeException(nameof(percentage), "The value provided for the backup-test-percentage option must lie between 0 and 100.");
                }

                return percentage;
            }
        }

        /// <summary>
        /// Gets the number of samples to test during a backup operation
        /// </summary>
        public long BackupTestSampleCount => Math.Max(0, GetLong("backup-test-samples", DEFAULT_BACKUP_TEST_SAMPLES));

        /// <summary>
        /// Gets a flag indicating if compacting should not be done automatically
        /// </summary>
        public bool NoAutoCompact => GetBool("no-auto-compact");

        /// <summary>
        /// Gets the minimum time that must elapse after last compaction before running next automatic compaction
        /// </summary>
        public TimeSpan AutoCompactInterval => Library.Utility.Utility.ParseTimespanOption(m_options, "auto-compact-interval", "0s");

        /// <summary>
        /// Gets a flag indicating if missing source elements should be ignored
        /// </summary>
        public bool AllowMissingSource => GetBool("allow-missing-source");
        /// <summary>
        /// Gets a flag indicating if empty source elements should cause backups to fail
        /// </summary>
        public bool PreventEmptySource => GetBool("prevent-empty-source");

        /// <summary>
        /// Gets a value indicating if a verification file should be uploaded after changing the remote store
        /// </summary>
        public bool UploadVerificationFile => GetBool("upload-verification-file");

        /// <summary>
        /// Gets a value indicating if a passphrase change is allowed
        /// </summary>
        public bool AllowPassphraseChange => GetBool("allow-passphrase-change");

        /// <summary>
        /// Gets a flag indicating if the current operation should merely output the changes
        /// </summary>
        public bool Dryrun => GetBool("dry-run") || GetBool("dryrun");

        /// <summary>
        /// Gets a value indicating if the remote verification is deep
        /// </summary>
        public RemoteTestStrategy FullRemoteVerification => GetEnum("full-remote-verification", RemoteTestStrategy.True);

        /// <summary>
        /// Gets a value indicating if defective index files should be replaced
        /// </summary>
        public bool ReplaceFaultyIndexFiles => !GetBool("dont-replace-faulty-index-files");

        /// <summary>
        /// The block hash algorithm to use
        /// </summary>
        public string BlockHashAlgorithm => GetString("block-hash-algorithm", DEFAULT_BLOCK_HASH_ALGORITHM);

        /// <summary>
        /// The file hash algorithm to use
        /// </summary>
        public string FileHashAlgorithm => GetString("file-hash-algorithm", DEFAULT_FILE_HASH_ALGORITHM);

        /// <summary>
        /// Gets a value indicating whether local blocks usage should be used for restore.
        /// </summary>
        /// <value><c>true</c> if no local blocks; otherwise, <c>false</c>.</value>
        public bool UseLocalBlocks => GetBool("restore-with-local-blocks");

        /// <summary>
        /// Gets a flag indicating if the local database should not be used
        /// </summary>
        /// <value><c>true</c> if no local db is used; otherwise, <c>false</c>.</value>
        public bool NoLocalDb => GetBool("no-local-db");

        /// <summary>
        /// Gets a flag indicating if the local database should not be used
        /// </summary>
        /// <value><c>true</c> if no local db is used; otherwise, <c>false</c>.</value>
        public bool DontCompressRestorePaths => GetBool("dont-compress-restore-paths");

        /// <summary>
        /// Gets a value indicating whether to allow restore outside target directory.
        /// </summary>
        public bool AllowRestoreOutsideTargetDirectory => GetBool("allow-restore-outside-target-directory");

        /// <summary>
        /// Gets a flag indicating if block hashes are checked before being applied
        /// </summary>
        /// <value><c>true</c> if block hashes are checked; otherwise, <c>false</c>.</value>
        public bool FullBlockVerification => GetBool("full-block-verification");

        /// <summary>
        /// Gets a flag indicating if the repair process will only restore paths
        /// </summary>
        /// <value><c>true</c> if only paths are restored; otherwise, <c>false</c>.</value>
        public bool RepairOnlyPaths => GetBool("repair-only-paths");

        /// <summary>
        /// Gets a flag indicating if the repair process will ignore outdated database
        /// </summary>
        /// <value><c>true</c> if repair process will ignore outdated database; otherwise, <c>false</c>.</value>
        public bool RepairIgnoreOutdatedDatabase => GetBool("repair-ignore-outdated-database");

        /// <summary>
        /// Gets a flag indicating if the repair process will always use blocks
        /// </summary>
        /// <value><c>true</c> if repair process always use blocks; otherwise, <c>false</c>.</value>
        public bool RepairForceBlockUse => GetBool("repair-force-block-use");

        /// <summary>
        /// Gets a flag indicating if the repair process should refresh lock information from the backend
        /// </summary>
        /// <value><c>true</c> if repair process should refresh lock information; otherwise, <c>false</c>.</value>
        public bool RepairRefreshLockInfo => GetBool("repair-refresh-lock-info");

        /// <summary>
        /// Gets a flag indicating if the repair process should refresh lock information for all volumes
        /// </summary>
        public bool RefreshLockInfoComplete => GetBool("refresh-lock-info-complete");

        /// <summary>
        /// Gets a flag indicating whether the VACUUM operation should ever be run automatically.
        /// </summary>
        public bool AutoVacuum => GetBool("auto-vacuum");

        /// <summary>
        /// Gets the minimum time that must elapse after last vacuum before running next automatic vacuum
        /// </summary>
        public TimeSpan AutoVacuumInterval => Library.Utility.Utility.ParseTimespanOption(m_options, "auto-vacuum-interval", "0s");

        /// <summary>
        /// Gets a flag indicating if the local filescanner should be disabled
        /// </summary>
        /// <value><c>true</c> if the filescanner should be disabled; otherwise, <c>false</c>.</value>
        public bool DisableFileScanner => GetBool("disable-file-scanner");

        /// <summary>
        /// Gets a flag indicating if the filelist consistency checks should be disabled
        /// </summary>
        /// <value><c>true</c> if the filelist consistency checks should be disabled; otherwise, <c>false</c>.</value>
        public bool DisableFilelistConsistencyChecks => GetBool("disable-filelist-consistency-checks");

        /// <summary>
        /// Gets a flag indicating whether the backup should be disabled when on battery power.
        /// </summary>
        /// <value><c>true</c> if the backup should be disabled when on battery power; otherwise, <c>false</c>.</value>
        public bool DisableOnBattery => GetBool("disable-on-battery");

        /// <summary>
        /// Gets a value indicating if missing dblock files are attempted created
        /// </summary>
        public bool RebuildMissingDblockFiles => GetBool("rebuild-missing-dblock-files");

        /// <summary>
        /// Gets a value indicating if partial dblock recovery is disabled
        /// </summary>
        public bool DisablePartialDblockRecovery => GetBool("disable-partial-dblock-recovery");

        /// <summary>
        /// Gets a value indicating if missing metadata is replaced with empty content on purge-broken-files
        /// </summary>
        public bool DisableReplaceMissingMetadata => GetBool("disable-replace-missing-metadata");

        /// <summary>
        /// Gets a value indicating if the purge-broken-files command should skip calculating the size of the removed files
        /// </summary>
        public bool ReducedPurgeStatistics => GetBool("reduced-purge-statistics");

        /// <summary>
        /// Gets the threshold for when log data should be cleaned
        /// </summary>
        public DateTime LogRetention
        {
            get
            {
                var value = GetString("log-retention", DEFAULT_LOG_RETENTION);
                return Library.Utility.Timeparser.ParseTimeInterval(value, DateTime.Now, true);
            }
        }

        /// <summary>
        /// Gets the number of concurrent threads
        /// </summary>
        public int ConcurrencyMaxThreads => GetInt("concurrency-max-threads", 0);

        /// <summary>
        /// Gets the number of concurrent block hashers
        /// </summary>
        public int ConcurrencyBlockHashers => Math.Max(1, GetInt("concurrency-block-hashers", DEFAULT_BLOCK_HASHERS));

        /// <summary>
        /// Gets the number of concurrent block hashers
        /// </summary>
        public int ConcurrencyCompressors => Math.Max(1, GetInt("concurrency-compressors", DEFAULT_COMPRESSORS));

        /// <summary>
        /// Gets the number of concurrent file processors
        /// </summary>
        public int ConcurrencyFileprocessors => Math.Max(1, GetInt("concurrency-fileprocessors", DEFAULT_FILE_PROCESSORS));

        /// <summary>
        /// Gets a lookup table with compression hints, the key is the file extension with the leading period
        /// </summary>
        public IDictionary<string, CompressionHint> CompressionHints
        {
            get
            {
                if (m_compressionHints == null)
                {
                    var hints = new Dictionary<string, CompressionHint>(StringComparer.OrdinalIgnoreCase); // Ignore file system case sensitivity, since file extensions case rarely indicates type

                    var file = GetString("compression-extension-file", DEFAULT_COMPRESSED_EXTENSION_FILE);
                    if (!string.IsNullOrEmpty(file) && System.IO.File.Exists(file))
                        foreach (var _line in Library.Utility.Utility.ReadFileWithDefaultEncoding(file).Split('\n'))
                        {
                            var line = _line.Trim();
                            var lix = line.IndexOf(' ');
                            if (lix > 0)
                                line = line.Substring(0, lix);
                            if (line.Length >= 2 && line[0] == '.')
                                hints[line] = CompressionHint.Noncompressible;
                        }

                    //Don't try again, if the file does not exist
                    m_compressionHints = hints;
                }

                return m_compressionHints;
            }
        }

        /// <summary>
        /// Gets the CPU intensity level indicating target CPU utilization. 1 is the lowest, 10 is the highest. Default is 10.
        /// </summary>
        public int CPUIntensity => Math.Max(1, Math.Min(10, GetInt("cpu-intensity", 10)));

        /// <summary>
        /// Gets a compression hint from a filename
        /// </summary>
        /// <param name="filename">The filename to get the hint for</param>
        /// <returns>The compression hint</returns>
        public CompressionHint GetCompressionHintFromFilename(string filename)
        {
            if (!CompressionHints.TryGetValue(System.IO.Path.GetExtension(filename), out var h))
                return CompressionHint.Default;
            return h;
        }


        protected readonly List<IGenericModule> m_loadedModules = new();

        /// <summary>
        /// Gets a list of loaded modules, in their activation order
        /// </summary>
        public IEnumerable<IGenericModule> LoadedModules => m_loadedModules
            .OrderByDescending(x => (x as IGenericPriorityModule)?.Priority ?? 0);

        /// <summary>
        /// Adds a loaded module to the list of loaded modules.
        /// </summary>
        /// <param name="module">The module to add</param>
        public void AddLoadedModule(IGenericModule module)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));
            m_loadedModules.Add(module);
        }

        /// <summary>
        /// Clears the list of loaded modules.
        /// </summary>
        public void ClearLoadedModules()
        {
            m_loadedModules.Clear();
        }

        /// <summary>
        /// Helper method to extract boolean values.
        /// If the option is not present, it it's value is false.
        /// If the option is present it's value is true, unless the option's value is false, off or 0
        /// </summary>
        /// <param name="name">The name of the option to read</param>
        /// <returns>The interpreted value of the option</returns>
        private bool GetBool(string name)
            => Library.Utility.Utility.ParseBoolOption(m_options, name);

        /// <summary>
        /// Helper method to extract string values.
        /// </summary>
        /// <param name="name">The option name</param>
        /// <param name="default">The default value</param>
        /// <returns>The value of the option, or the default value if the option is not present or empty</returns>
        [return: NotNullIfNotNull("default")]
        private string? GetString(string name, string? @default)
        {
            var value = m_options.GetValueOrDefault(name);
            return string.IsNullOrEmpty(value) ? @default : value;
        }

        /// <summary>
        /// Helper method to extract integer values.
        /// </summary>
        /// <param name="name">The option name</param>
        /// <param name="default">The default value</param>
        /// <returns>The value of the option, or the default value if the option is not present or empty</returns>
        private int GetInt(string name, int @default)
            => Library.Utility.Utility.ParseIntOption(m_options, name, @default);

        /// <summary>
        /// Helper method to extract long values.
        /// </summary>
        /// <param name="name">The option name</param>
        /// <param name="default">The default value</param>
        /// <returns>The value of the option, or the default value if the option is not present or empty</returns>
        private long GetLong(string name, long @default)
            => Library.Utility.Utility.ParseLongOption(m_options, name, @default);

        /// <summary>
        /// Helper method to extract enum values.
        /// </summary>
        /// <typeparam name="T">The enum type</typeparam>
        /// <param name="name">The option name</param>
        /// <param name="default">The default value</param>
        /// <returns>The value of the option, or the default value if the option is not present or empty</returns>
        private T GetEnum<T>(string name, T @default) where T : struct, Enum
            => Library.Utility.Utility.ParseEnumOption(m_options, name, @default);

        /// <summary>
        /// Helper method to extract size values.
        /// </summary>
        /// <param name="name">The option name</param>
        /// <param name="unit">The default unit</param>
        /// <param name="default">The default value</param>
        /// <returns>The value of the option, or the default value if the option is not present or empty</returns>
        private long GetSize(string name, string unit, string @default)
            => Library.Utility.Utility.ParseSizeOption(m_options, name, unit, @default);

        /// <summary>
        /// Gets the maximum number of data blocks to keep in the cache. If set to 0, the cache is effictively disabled, but some is still kept for bookkeeping.
        /// </summary>
        public long RestoreCacheMax
        {
            get
            {
                var max_cache = GetSize("restore-cache-max", "mb", DEFAULT_RESTORE_CACHE_MAX);
                if (max_cache > 0 && max_cache < Blocksize)
                    throw new ArgumentOutOfRangeException(nameof(max_cache), string.Format("The maximum cache size cannot be less than the blocksize if not explicitly 0: {0} < {1}", max_cache, Blocksize));

                return max_cache / Blocksize;
            }
        }

        /// <summary>
        /// Gets the ratio of cache entries to evict when the cache is full
        /// </summary>
        public float RestoreCacheEvict
        {
            get
            {
                m_options.TryGetValue("restore-cache-evict", out var s);
                if (string.IsNullOrEmpty(s))
                    return DEFAULT_RESTORE_CACHE_EVICT / 100f;

                int percentage;
                try
                {
                    percentage = int.Parse(s, CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException("The value provided for the restore-cache-evict option must lie between 0 and 100", ex);
                }

                if ((percentage < 0) || (percentage > 100))
                {
                    throw new ArgumentOutOfRangeException(nameof(percentage), "The value provided for the restore-cache-evict option must lie between 0 and 100");
                }

                return percentage / 100f;
            }
        }

        /// <summary>
        /// Gets the number of file processors to use in the restore process
        /// </summary>
        public int RestoreFileProcessors => GetInt("restore-file-processors", DEFAULT_RESTORE_FILE_PROCESSORS);

        /// <summary>
        /// Gets whether to use the legacy restore method
        /// </summary>
        public bool RestoreLegacy => GetBool("restore-legacy");

        /// <summary>
        /// Gets whether to preallocate files during restore
        /// </summary>
        public bool RestorePreAllocate => GetBool("restore-pre-allocate");

        /// <summary>
        /// Gets whether to handle MacOS Photo Libraries specially
        /// </summary>
        public MacOSPhotosHandling HandleMacOSPhotoLibrary => GetEnum("photos-handling", DEFAULT_MACOS_PHOTOS_HANDLING);

        /// <summary>
        /// Gets the path to the MacOS Photos Library
        /// </summary>
        public string? MacOSPhotoLibraryPath => GetString("photos-library-path", null);

        /// <summary>
        /// Gets the maximum size of the restore volume cache in bytes.
        /// Returns -1 when unset (unlimited/disk-space-aware mode), 0 to disable caching, or a positive byte count for a hard cap.
        /// </summary>
        public long RestoreVolumeCacheHint
        {
            get
            {
                m_options.TryGetValue("restore-volume-cache-hint", out var s);

                if (string.IsNullOrEmpty(s))
                    return -1L; // Unlimited: disk-space-aware eviction bounded by RestoreVolumeCacheMinFree

                return GetSize("restore-volume-cache-hint", "mb", $"{VolumeSize * 100}b");
            }
        }

        /// <summary>
        /// Gets the minimum free disk space (in bytes) to maintain in the temp directory during restore.
        /// Only used when <see cref="RestoreVolumeCacheHint"/> returns -1 (unlimited mode).
        /// </summary>
        public long RestoreVolumeCacheMinFree
            => GetSize("restore-volume-cache-min-free", "gb", DEFAULT_RESTORE_VOLUME_CACHE_MIN_FREE);

        /// <summary>
        /// Gets the number of volume decryptors to use in the restore process
        /// </summary>
        public int RestoreVolumeDecryptors => GetInt("restore-volume-decryptors", DEFAULT_RESTORE_VOLUME_DECRYPTORS);

        /// <summary>
        /// Gets the number of volume decompressors to use in the restore process
        /// </summary>
        public int RestoreVolumeDecompressors => GetInt("restore-volume-decompressors", DEFAULT_RESTORE_VOLUME_DECOMPRESSORS);

        /// <summary>
        /// Gets the number of volume downloaders to use in the restore process
        /// </summary>
        public int RestoreVolumeDownloaders => GetInt("restore-volume-downloaders", DEFAULT_RESTORE_VOLUME_DOWNLOADERS);

        /// <summary>
        /// Gets the size of the buffer used for the restore channel
        /// </summary>
        public int RestoreChannelBufferSize => GetInt("restore-channel-buffer-size", DEFAULT_RESTORE_CHANNEL_BUFFER_SIZE);

        /// <summary>
        /// Toggles whether internal profiling is enabled and should be logged.
        /// </summary>
        public bool InternalProfiling => GetBool("internal-profiling");

        /// <summary>
        /// Ignores the update if the version already exists in the database.
        /// </summary>
        public bool IgnoreUpdateIfVersionExists
            => GetBool("ignore-update-if-version-exists");

        /// <summary>
        /// Class for handling a single RetentionPolicy timeframe-interval-pair
        /// </summary>
        public class RetentionPolicyValue
        {
            public readonly TimeSpan Timeframe;
            public readonly TimeSpan Interval;

            public RetentionPolicyValue(TimeSpan timeframe, TimeSpan interval)
            {
                if (timeframe < TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(timeframe), string.Format("The timeframe cannot be negative: '{0}'", timeframe));
                }
                if (interval < TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(interval), string.Format("The interval cannot be negative: '{0}'", interval));
                }

                this.Timeframe = timeframe;
                this.Interval = interval;
            }

            /// <summary>
            /// Returns whether this is an unlimited timeframe or not
            /// </summary>
            /// <returns></returns>
            public Boolean IsUnlimtedTimeframe()
            {
                // Timeframes equal or bigger than the maximum TimeSpan effectively represent an unlimited timeframe
                return Timeframe >= TimeSpan.MaxValue;
            }

            /// <summary>
            /// Returns whether all versions in this timeframe should be kept or not
            /// </summary>
            /// <returns></returns>
            public Boolean IsKeepAllVersions()
            {
                /// Intervals between two versions that are equal or smaller than zero effectivly result in
                /// all versions in that timeframe being kept.
                return Interval <= TimeSpan.Zero;
            }

            public override string ToString()
            {
                return (IsUnlimtedTimeframe() ? "Unlimited" : Timeframe.ToString()) + " / " + (IsKeepAllVersions() ? "Keep all" : Interval.ToString());
            }

            /// <summary>
            /// Parses a string representation of a timeframe-interval-pair and returns a RetentionPolicyValue object
            /// </summary>
            /// <returns></returns>
            public static RetentionPolicyValue CreateFromString(string rententionPolicyValueString)
            {
                var periodInterval = rententionPolicyValueString.Split(':');

                TimeSpan timeframe;
                // Timeframe "U" (= Unlimited) means: For unlimited time keep one version every X interval.
                // So the timeframe has to span the maximum time possible.
                if (String.Equals(periodInterval[0], "U", StringComparison.OrdinalIgnoreCase))
                {
                    timeframe = TimeSpan.MaxValue;
                }
                else
                {
                    timeframe = Library.Utility.Timeparser.ParseTimeSpan(periodInterval[0]);
                }

                TimeSpan interval;
                // Interval "U" (= Unlimited) means: For period X keep all versions.
                // So the interval between two versions has to be zero.
                if (String.Equals(periodInterval[1], "U", StringComparison.OrdinalIgnoreCase))
                {
                    interval = TimeSpan.Zero;
                }
                else
                {
                    interval = Library.Utility.Timeparser.ParseTimeSpan(periodInterval[1]);
                }

                return new RetentionPolicyValue(timeframe, interval);
            }
        }
    }
}
