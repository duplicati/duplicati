#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using System.Globalization;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// A class for keeping all Duplicati options in one place,
    /// and provide typesafe access to the options
    /// </summary>
    public class Options
    {
        private const string DEFAULT_BLOCK_HASH_ALGORITHM = "SHA256";
        private const string DEFAULT_FILE_HASH_ALGORITHM = "SHA256";
        
        /// <summary>
        /// The default block size
        /// </summary>
        private const string DEFAULT_BLOCKSIZE = "100kb";

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
        /// The default number of compressor instances
        /// </summary>
        private readonly int DEFAULT_COMPRESSORS = Math.Max(1, Environment.ProcessorCount / 2);

        /// <summary>
        /// The default number of hasher instances
        /// </summary>
        private readonly int DEFAULT_BLOCK_HASHERS = Math.Max(1, Environment.ProcessorCount / 2);
        
        /// <summary>
        /// The default threshold for warning about coming close to quota
        /// </summary>
        private const int DEFAULT_QUOTA_WARNING_THRESHOLD = 10;

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

        private static string[] GetSupportedHashes()
        {
            var r = new List<string>();
            foreach (var h in new string[] { "SHA1", "MD5", "SHA256", "SHA384", "SHA512" })
            {
                try
                {
                    var p = System.Security.Cryptography.HashAlgorithm.Create(h);
                    if (p != null)
                        r.Add(h);
                }
                catch
                {
                }
            }
            
            return r.ToArray();
        }

        private static readonly string DEFAULT_COMPRESSED_EXTENSION_FILE = System.IO.Path.Combine(Duplicati.Library.AutoUpdater.UpdaterManager.InstalledBaseDir, "default_compressed_extensions.txt");

        /// <summary>
        /// Lock that protects the options collection
        /// </summary>
        protected readonly object m_lock = new object();

        protected readonly Dictionary<string, string> m_options;

        protected readonly List<KeyValuePair<bool, Library.Interface.IGenericModule>> m_loadedModules = new List<KeyValuePair<bool, IGenericModule>>();

        /// <summary>
        /// Lookup table for compression hints
        /// </summary>
        private Dictionary<string, CompressionHint> m_compressionHints;

        public Options(Dictionary<string, string> options)
        {
            m_options = options;
        }

        public Dictionary<string, string> RawOptions { get { return m_options; } }

        /// <summary>
        /// Returns a list of strings that are not supported on the commandline as options, but used internally
        /// </summary>
        public static string[] InternalOptions
        {
            get
            {
                return new string[] {
                    "main-action"
                };
            }
        }

        /// <summary>
        /// Returns a list of options that are intentionally duplicate
        /// </summary>
        public static string[] KnownDuplicates
        {
            get { return new string[] { "auth-password", "auth-username" }; }
        }

        /// <summary>
        /// A default backup name
        /// </summary>
        public static string DefaultBackupName
        {
            get
            {
                return System.IO.Path.GetFileNameWithoutExtension(Library.Utility.Utility.getEntryAssembly().Location);
            }
        }

        /// <summary>
        /// Gets all supported commands
        /// </summary>
        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                var lst = new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("dblock-size", CommandLineArgument.ArgumentType.Size, Strings.Options.DblocksizeShort, Strings.Options.DblocksizeLong, DEFAULT_VOLUME_SIZE),
                    new CommandLineArgument("auto-cleanup", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AutocleanupShort, Strings.Options.AutocleanupLong, "false"),
                    new CommandLineArgument("unittest-mode", CommandLineArgument.ArgumentType.Boolean, Strings.Options.UnittestmodeShort, Strings.Options.UnittestmodeLong, "false"),

                    new CommandLineArgument("control-files", CommandLineArgument.ArgumentType.Path, Strings.Options.ControlfilesShort, Strings.Options.ControlfilesLong),
                    new CommandLineArgument("skip-file-hash-checks", CommandLineArgument.ArgumentType.Boolean, Strings.Options.SkipfilehashchecksShort, Strings.Options.SkipfilehashchecksLong, "false"),
                    new CommandLineArgument("dont-read-manifests", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DontreadmanifestsShort, Strings.Options.DontreadmanifestsLong, "false"),
                    new CommandLineArgument("restore-path", CommandLineArgument.ArgumentType.String, Strings.Options.RestorepathShort, Strings.Options.RestorepathLong),
                    new CommandLineArgument("time", CommandLineArgument.ArgumentType.Timespan, Strings.Options.TimeShort, Strings.Options.TimeLong, "now"),
                    new CommandLineArgument("version", CommandLineArgument.ArgumentType.String, Strings.Options.VersionShort, Strings.Options.VersionLong, ""),
                    new CommandLineArgument("all-versions", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllversionsShort, Strings.Options.AllversionsLong, "false"),
                    new CommandLineArgument("list-prefix-only", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ListprefixonlyShort, Strings.Options.ListprefixonlyLong, "false"),
                    new CommandLineArgument("list-folder-contents", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ListfoldercontentsShort, Strings.Options.ListfoldercontentsLong, "false"),
                    new CommandLineArgument("list-sets-only", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ListsetsonlyShort, Strings.Options.ListsetsonlyLong, "false"),
                    new CommandLineArgument("disable-autocreate-folder", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisableautocreatefolderShort, Strings.Options.DisableautocreatefolderLong, "false"),
                    new CommandLineArgument("allow-missing-source", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllowmissingsourceShort, Strings.Options.AllowmissingsourceLong, "false"),

                    new CommandLineArgument("disable-filetime-check", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisablefiletimecheckShort, Strings.Options.DisablefiletimecheckLong, "false"),
                    new CommandLineArgument("check-filetime-only", CommandLineArgument.ArgumentType.Boolean, Strings.Options.CheckfiletimeonlyShort, Strings.Options.CheckfiletimeonlyLong, "false"),
                    new CommandLineArgument("disable-time-tolerance", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisabletimetoleranceShort, Strings.Options.DisabletimetoleranceLong, "false"),

                    new CommandLineArgument("tempdir", CommandLineArgument.ArgumentType.Path, Strings.Options.TempdirShort, Strings.Options.TempdirLong, System.IO.Path.GetTempPath()),
                    new CommandLineArgument("thread-priority", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.ThreadpriorityShort, Strings.Options.ThreadpriorityLong, "normal", null, new string[] {"highest", "high", "abovenormal", "normal", "belownormal", "low", "lowest", "idle" }),

                    new CommandLineArgument("prefix", CommandLineArgument.ArgumentType.String, Strings.Options.PrefixShort, Strings.Options.PrefixLong, "duplicati"),

                    new CommandLineArgument("passphrase", CommandLineArgument.ArgumentType.Password, Strings.Options.PassphraseShort, Strings.Options.PassphraseLong),
                    new CommandLineArgument("no-encryption", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NoencryptionShort, Strings.Options.NoencryptionLong, "false"),

                    new CommandLineArgument("number-of-retries", CommandLineArgument.ArgumentType.Integer, Strings.Options.NumberofretriesShort, Strings.Options.NumberofretriesLong, "5"),
                    new CommandLineArgument("retry-delay", CommandLineArgument.ArgumentType.Timespan, Strings.Options.RetrydelayShort, Strings.Options.RetrydelayLong, "10s"),
                    new CommandLineArgument("retry-with-exponential-backoff", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RetrywithexponentialbackoffShort, Strings.Options.RetrywithexponentialbackoffLong, "false"),

                    new CommandLineArgument("synchronous-upload", CommandLineArgument.ArgumentType.Boolean, Strings.Options.SynchronousuploadShort, Strings.Options.SynchronousuploadLong, "false"),
                    new CommandLineArgument("asynchronous-upload-limit", CommandLineArgument.ArgumentType.Integer, Strings.Options.AsynchronousuploadlimitShort, Strings.Options.AsynchronousuploadlimitLong, "4"),
                    new CommandLineArgument("asynchronous-concurrent-upload-limit", CommandLineArgument.ArgumentType.Integer, Strings.Options.AsynchronousconcurrentuploadlimitShort, Strings.Options.AsynchronousconcurrentuploadlimitLong, "4"),
                    new CommandLineArgument("asynchronous-upload-folder", CommandLineArgument.ArgumentType.Path, Strings.Options.AsynchronousuploadfolderShort, Strings.Options.AsynchronousuploadfolderLong, System.IO.Path.GetTempPath()),
                    
                    new CommandLineArgument("disable-streaming-transfers", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisableStreamingShort, Strings.Options.DisableStreamingLong, "false"),

                    new CommandLineArgument("throttle-upload", CommandLineArgument.ArgumentType.Size, Strings.Options.ThrottleuploadShort, Strings.Options.ThrottleuploadLong, "0kb"),
                    new CommandLineArgument("throttle-download", CommandLineArgument.ArgumentType.Size, Strings.Options.ThrottledownloadShort, Strings.Options.ThrottledownloadLong, "0kb"),
                    new CommandLineArgument("skip-files-larger-than", CommandLineArgument.ArgumentType.Size, Strings.Options.SkipfileslargerthanShort, Strings.Options.SkipfileslargerthanLong),
                    
                    new CommandLineArgument("upload-unchanged-backups", CommandLineArgument.ArgumentType.Boolean, Strings.Options.UploadUnchangedBackupsShort, Strings.Options.UploadUnchangedBackupsLong, "false"),

                    new CommandLineArgument("snapshot-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.SnapshotpolicyShort, Strings.Options.SnapshotpolicyLong, "off", null, Enum.GetNames(typeof(OptimizationStrategy))),
                    new CommandLineArgument("vss-exclude-writers", CommandLineArgument.ArgumentType.String, Strings.Options.VssexcludewritersShort, Strings.Options.VssexcludewritersLong, "{e8132975-6f93-4464-a53e-1050253ae220}"),
                    new CommandLineArgument("vss-use-mapping", CommandLineArgument.ArgumentType.Boolean, Strings.Options.VssusemappingShort, Strings.Options.VssusemappingLong, "false"),
                    new CommandLineArgument("usn-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.UsnpolicyShort, Strings.Options.UsnpolicyLong, "off", null, Enum.GetNames(typeof(OptimizationStrategy))),

                    new CommandLineArgument("encryption-module", CommandLineArgument.ArgumentType.String, Strings.Options.EncryptionmoduleShort, Strings.Options.EncryptionmoduleLong, "aes"),
                    new CommandLineArgument("compression-module", CommandLineArgument.ArgumentType.String, Strings.Options.CompressionmoduleShort, Strings.Options.CompressionmoduleLong, "zip"),

                    new CommandLineArgument("enable-module", CommandLineArgument.ArgumentType.String, Strings.Options.EnablemoduleShort, Strings.Options.EnablemoduleLong),
                    new CommandLineArgument("disable-module", CommandLineArgument.ArgumentType.String, Strings.Options.DisablemoduleShort, Strings.Options.DisablemoduleLong),

                    new CommandLineArgument("debug-output", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DebugoutputShort, Strings.Options.DebugoutputLong, "false"),
                    new CommandLineArgument("debug-retry-errors", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DebugretryerrorsShort, Strings.Options.DebugretryerrorsLong, "false"),

                    new CommandLineArgument("log-file", CommandLineArgument.ArgumentType.Path, Strings.Options.LogfileShort, Strings.Options.LogfileLong),
                    new CommandLineArgument("log-file-log-level", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.LogfileloglevelShort, Strings.Options.LogfileloglevelShort, "Warning", null, Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType))),
                    new CommandLineArgument("log-file-log-filter", CommandLineArgument.ArgumentType.String, Strings.Options.LogfilelogfiltersShort, Strings.Options.LogfilelogfiltersLong(System.IO.Path.PathSeparator.ToString()), null),

                    new CommandLineArgument("console-log-level", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.ConsoleloglevelShort, Strings.Options.ConsoleloglevelShort, "Warning", null, Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType))),
                    new CommandLineArgument("console-log-filter", CommandLineArgument.ArgumentType.String, Strings.Options.ConsolelogfiltersShort, Strings.Options.ConsolelogfiltersLong(System.IO.Path.PathSeparator.ToString()), null),

                    new CommandLineArgument("log-level", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.LoglevelShort, Strings.Options.LoglevelLong, "Warning", null, Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType)), Strings.Options.LogLevelDeprecated("log-file-log-level", "console-log-level")),

                    new CommandLineArgument("profile-all-database-queries", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ProfilealldatabasequeriesShort, Strings.Options.ProfilealldatabasequeriesLong, "false"),

                    new CommandLineArgument("list-verify-uploads", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ListverifyuploadsShort, Strings.Options.ListverifyuploadsShort, "false"),
                    new CommandLineArgument("allow-sleep", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllowsleepShort, Strings.Options.AllowsleepLong, "false"),
                    new CommandLineArgument("use-background-io-priority", CommandLineArgument.ArgumentType.Boolean, Strings.Options.UsebackgroundiopriorityShort, Strings.Options.UsebackgroundiopriorityLong, "false"),
                    new CommandLineArgument("no-connection-reuse", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NoconnectionreuseShort, Strings.Options.NoconnectionreuseLong, "false"),

                    new CommandLineArgument("quota-size", CommandLineArgument.ArgumentType.Size, Strings.Options.QuotasizeShort, Strings.Options.QuotasizeLong),
                    new CommandLineArgument("quota-warning-threshold", CommandLineArgument.ArgumentType.Integer, Strings.Options.QuotaWarningThresholdShort, Strings.Options.QuotaWarningThresholdLong, DEFAULT_QUOTA_WARNING_THRESHOLD.ToString()),

                    new CommandLineArgument("symlink-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.SymlinkpolicyShort, Strings.Options.SymlinkpolicyLong("store", "ignore", "follow"), Enum.GetName(typeof(SymlinkStrategy), SymlinkStrategy.Store), null, Enum.GetNames(typeof(SymlinkStrategy))),
                    new CommandLineArgument("hardlink-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.HardlinkpolicyShort, Strings.Options.HardlinkpolicyLong("first", "all", "none"), Enum.GetName(typeof(HardlinkStrategy), HardlinkStrategy.All), null, Enum.GetNames(typeof(HardlinkStrategy))),
                    new CommandLineArgument("exclude-files-attributes", CommandLineArgument.ArgumentType.String, Strings.Options.ExcludefilesattributesShort, Strings.Options.ExcludefilesattributesLong(Enum.GetNames(typeof(System.IO.FileAttributes)))),
                    new CommandLineArgument("backup-name", CommandLineArgument.ArgumentType.String, Strings.Options.BackupnameShort, Strings.Options.BackupnameLong, DefaultBackupName),
                    new CommandLineArgument("compression-extension-file", CommandLineArgument.ArgumentType.Path, Strings.Options.CompressionextensionfileShort, Strings.Options.CompressionextensionfileLong(DEFAULT_COMPRESSED_EXTENSION_FILE), DEFAULT_COMPRESSED_EXTENSION_FILE),

                    new CommandLineArgument("verbose", CommandLineArgument.ArgumentType.Boolean, Strings.Options.VerboseShort, Strings.Options.VerboseLong, "false", null, null, Strings.Options.VerboseDeprecated),
                    new CommandLineArgument("full-result", CommandLineArgument.ArgumentType.Boolean, Strings.Options.FullresultShort, Strings.Options.FullresultLong, "false"),

                    new CommandLineArgument("overwrite", CommandLineArgument.ArgumentType.Boolean, Strings.Options.OverwriteShort, Strings.Options.OverwriteLong, "false"),

                    new CommandLineArgument("dbpath", CommandLineArgument.ArgumentType.Path, Strings.Options.DbpathShort, Strings.Options.DbpathLong),
                    new CommandLineArgument("blocksize", CommandLineArgument.ArgumentType.Size, Strings.Options.BlocksizeShort, Strings.Options.BlocksizeLong, DEFAULT_BLOCKSIZE),
                    new CommandLineArgument("file-read-buffer-size", CommandLineArgument.ArgumentType.Size, Strings.Options.FilereadbuffersizeShort, Strings.Options.FilereadbuffersizeLong, "0kb", null, null, @"The ""file-read-buffer-size"" option is no longer used and has been deprecated."),
                    new CommandLineArgument("skip-metadata", CommandLineArgument.ArgumentType.Boolean, Strings.Options.SkipmetadataShort, Strings.Options.SkipmetadataLong, "false"),
                    new CommandLineArgument("restore-permissions", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RestorepermissionsShort, Strings.Options.RestorepermissionsLong, "false"),
                    new CommandLineArgument("skip-restore-verification", CommandLineArgument.ArgumentType.Boolean, Strings.Options.SkiprestoreverificationShort, Strings.Options.SkiprestoreverificationLong, "false"),
                    new CommandLineArgument("disable-filepath-cache", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisablefilepathcacheShort, Strings.Options.DisablefilepathcacheLong, "true", null, null, @"The ""disable-filepath-cache"" option is no longer used and has been deprecated."),
                    new CommandLineArgument("use-block-cache", CommandLineArgument.ArgumentType.Boolean, Strings.Options.UseblockcacheShort, Strings.Options.UseblockcacheLong, "false"),
                    new CommandLineArgument("changed-files", CommandLineArgument.ArgumentType.Path, Strings.Options.ChangedfilesShort, Strings.Options.ChangedfilesLong),
                    new CommandLineArgument("deleted-files", CommandLineArgument.ArgumentType.Path, Strings.Options.DeletedfilesShort, Strings.Options.DeletedfilesLong("changed-files")),
                    new CommandLineArgument("disable-synthetic-filelist", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisablesyntheticfilelistShort, Strings.Options.DisablesyntehticfilelistLong, "false"),

                    new CommandLineArgument("threshold", CommandLineArgument.ArgumentType.Integer, Strings.Options.ThresholdShort, Strings.Options.ThresholdLong, DEFAULT_THRESHOLD.ToString()),
                    new CommandLineArgument("index-file-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.IndexfilepolicyShort, Strings.Options.IndexfilepolicyLong, IndexFileStrategy.Full.ToString(), null, Enum.GetNames(typeof(IndexFileStrategy))),
                    new CommandLineArgument("no-backend-verification", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NobackendverificationShort, Strings.Options.NobackendverificationLong, "false"),
                    new CommandLineArgument("backup-test-samples", CommandLineArgument.ArgumentType.Integer, Strings.Options.BackendtestsamplesShort, Strings.Options.BackendtestsamplesLong("no-backend-verification"), "1"),
                    new CommandLineArgument("backup-test-percentage", CommandLineArgument.ArgumentType.Integer, Strings.Options.BackendtestpercentageShort, Strings.Options.BackendtestpercentageLong, "0"),
                    new CommandLineArgument("full-remote-verification", CommandLineArgument.ArgumentType.Boolean, Strings.Options.FullremoteverificationShort, Strings.Options.FullremoteverificationLong("no-backend-verification"), "false"),
                    new CommandLineArgument("dry-run", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DryrunShort, Strings.Options.DryrunLong, "false", new string[] { "dryrun" }),

                    new CommandLineArgument("block-hash-algorithm", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.BlockhashalgorithmShort, Strings.Options.BlockhashalgorithmLong, DEFAULT_BLOCK_HASH_ALGORITHM, null, GetSupportedHashes()),
                    new CommandLineArgument("file-hash-algorithm", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.FilehashalgorithmShort, Strings.Options.FilehashalgorithmLong, DEFAULT_FILE_HASH_ALGORITHM, null, GetSupportedHashes()),

                    new CommandLineArgument("no-auto-compact", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NoautocompactShort, Strings.Options.NoautocompactLong, "false"),
                    new CommandLineArgument("small-file-size", CommandLineArgument.ArgumentType.Size, Strings.Options.SmallfilesizeShort, Strings.Options.SmallfilesizeLong),
                    new CommandLineArgument("small-file-max-count", CommandLineArgument.ArgumentType.Integer, Strings.Options.SmallfilemaxcountShort, Strings.Options.SmallfilemaxcountLong, DEFAULT_SMALL_FILE_MAX_COUNT.ToString()),

                    new CommandLineArgument("patch-with-local-blocks", CommandLineArgument.ArgumentType.Boolean, Strings.Options.PatchwithlocalblocksShort, Strings.Options.PatchwithlocalblocksLong, "false"),
                    new CommandLineArgument("no-local-db", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NolocaldbShort, Strings.Options.NolocaldbLong, "false"),
                    new CommandLineArgument("dont-compress-restore-paths", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DontcompressrestorepathsShort, Strings.Options.DontcompressrestorepathsLong, "false"),

                    new CommandLineArgument("keep-versions", CommandLineArgument.ArgumentType.Integer, Strings.Options.KeepversionsShort, Strings.Options.KeepversionsLong, DEFAULT_KEEP_VERSIONS.ToString()),
                    new CommandLineArgument("keep-time", CommandLineArgument.ArgumentType.Timespan, Strings.Options.KeeptimeShort, Strings.Options.KeeptimeLong),
                    new CommandLineArgument("retention-policy", CommandLineArgument.ArgumentType.String, Strings.Options.RetentionPolicyShort, Strings.Options.RetentionPolicyLong),
                    new CommandLineArgument("upload-verification-file", CommandLineArgument.ArgumentType.Boolean, Strings.Options.UploadverificationfileShort, Strings.Options.UploadverificationfileLong, "false"),
                    new CommandLineArgument("allow-passphrase-change", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllowpassphrasechangeShort, Strings.Options.AllowpassphrasechangeLong, "false"),
                    new CommandLineArgument("no-local-blocks", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NolocalblocksShort, Strings.Options.NolocalblocksLong, "false"),
                    new CommandLineArgument("full-block-verification", CommandLineArgument.ArgumentType.Boolean, Strings.Options.FullblockverificationShort, Strings.Options.FullblockverificationLong, "false"),
                    new CommandLineArgument("allow-full-removal", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllowfullremovalShort, Strings.Options.AllowfullremovalLong, "false"),

                    new CommandLineArgument("log-retention", CommandLineArgument.ArgumentType.Timespan, Strings.Options.LogretentionShort, Strings.Options.LogretentionLong, DEFAULT_LOG_RETENTION),

                    new CommandLineArgument("repair-only-paths", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RepaironlypathsShort, Strings.Options.RepaironlypathsLong, "false"),
                    new CommandLineArgument("repair-force-block-use", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RepaironlypathsShort, Strings.Options.RepaironlypathsLong, "false"),
                    new CommandLineArgument("force-locale", CommandLineArgument.ArgumentType.String, Strings.Options.ForcelocaleShort, Strings.Options.ForcelocaleLong),
                    new CommandLineArgument("force-actual-date", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ForceActualDateShort, Strings.Options.ForceActualDateLong, "false"),

                    new CommandLineArgument("disable-piped-streaming", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisablepipingShort, Strings.Options.DisablepipingLong, "false"),

                    new CommandLineArgument("concurrency-max-threads", CommandLineArgument.ArgumentType.Integer, Strings.Options.ConcurrencymaxthreadsShort, Strings.Options.ConcurrencymaxthreadsLong, "0"),
                    new CommandLineArgument("concurrency-block-hashers", CommandLineArgument.ArgumentType.Integer, Strings.Options.ConcurrencyblockhashersShort, Strings.Options.ConcurrencyblockhashersLong, DEFAULT_BLOCK_HASHERS.ToString()),
                    new CommandLineArgument("concurrency-compressors", CommandLineArgument.ArgumentType.Integer, Strings.Options.ConcurrencycompressorsShort, Strings.Options.ConcurrencycompressorsLong, DEFAULT_COMPRESSORS.ToString()),
                    
                    new CommandLineArgument("auto-vacuum", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AutoVacuumShort, Strings.Options.AutoVacuumLong, "false"),
                    new CommandLineArgument("disable-file-scanner", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisablefilescannerShort, Strings.Options.DisablefilescannerLong, "false"),
                    new CommandLineArgument("disable-filelist-consistency-checks", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisablefilelistconsistencychecksShort, Strings.Options.DisablefilelistconsistencychecksLong, "false"),
                    new CommandLineArgument("disable-on-battery", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisableOnBatteryShort, Strings.Options.DisableOnBatteryLong, "false"),

                    new CommandLineArgument("exclude-empty-folders", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ExcludeemptyfoldersShort, Strings.Options.ExcludeemptyfoldersLong, "false"),
                    new CommandLineArgument("ignore-filenames", CommandLineArgument.ArgumentType.Path, Strings.Options.IgnorefilenamesShort, Strings.Options.IgnorefilenamesLong),
                    new CommandLineArgument("restore-symlink-metadata", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RestoresymlinkmetadataShort, Strings.Options.RestoresymlinkmetadataLong, "false"),
                    new CommandLineArgument("rebuild-missing-dblock-files", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RebuildmissingdblockfilesShort, Strings.Options.RebuildmissingdblockfilesLong, "false"),

                    new CommandLineArgument("auto-compact-interval", CommandLineArgument.ArgumentType.Timespan, Strings.Options.AutoCompactIntervalShort, Strings.Options.AutoCompactIntervalLong, "0m"),
                    new CommandLineArgument("auto-vacuum-interval", CommandLineArgument.ArgumentType.Timespan, Strings.Options.AutoVacuumIntervalShort, Strings.Options.AutoVacuumIntervalLong, "0m"),
                });

                return lst;
            }
        }

        /// <summary>
        /// Gets or sets the current main action of the instance
        /// </summary>
        public OperationMode MainAction 
        {
            get { return (OperationMode)Enum.Parse(typeof(OperationMode), m_options["main-action"]); }
            set { m_options["main-action"] = value.ToString(); }
        }

        /// <summary>
        /// Gets the size of each volume in bytes
        /// </summary>
        public long VolumeSize
        {
            get
            {
                string volsize;
                m_options.TryGetValue("dblock-size", out volsize);
                if (string.IsNullOrEmpty(volsize))
                    volsize = DEFAULT_VOLUME_SIZE;

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
                if (!m_options.ContainsKey("skip-files-larger-than") || string.IsNullOrEmpty(m_options["skip-files-larger-than"]))
                    return long.MaxValue;
                else
                    return Library.Utility.Sizeparser.ParseSize(m_options["skip-files-larger-than"], "mb");
            }
        }

        /// <summary>
        /// A value indicating if orphan files are deleted automatically
        /// </summary>
        public bool AutoCleanup { get { return GetBool("auto-cleanup"); } }

        /// <summary>
        /// A value indicating if we are running in unittest mode
        /// </summary>
        public bool UnittestMode { get { return GetBool("unittest-mode"); } }


        /// <summary>
        /// Gets a list of files to add to the signature volumes
        /// </summary>
        public string ControlFiles
        {
            get
            {
                string v;
                m_options.TryGetValue("control-files", out v);
                return v;
            }
        }

        /// <summary>
        /// A value indicating if file hash checks are skipped
        /// </summary>
        public bool SkipFileHashChecks { get { return GetBool("skip-file-hash-checks"); } }

        /// <summary>
        /// A value indicating if the manifest files are not read
        /// </summary>
        public bool DontReadManifests { get { return GetBool("dont-read-manifests"); } }

        /// <summary>
        /// Gets the backup that should be restored
        /// </summary>
        public DateTime Time
        {
            get
            {
                if (!m_options.ContainsKey("time") || string.IsNullOrEmpty(m_options["time"]))
                    return new DateTime(0, DateTimeKind.Utc);
                else
                    return Library.Utility.Timeparser.ParseTimeInterval(m_options["time"], DateTime.Now);
            }
        }
        
        /// <summary>
        /// Gets the versions the restore or list operation is limited to
        /// </summary>
        public long[] Version
        {
            get
            {
                string v;
                m_options.TryGetValue("version", out v);
                if (string.IsNullOrEmpty(v))
                    return null;
                
                var versions = v.Split(new char[]{','}, StringSplitOptions.RemoveEmptyEntries);
                if (v.Length == 0)
                    return null;
                
                var res = new List<long>();
                foreach(var n in versions)
                    if (n.Contains('-'))
                    {
                        //TODO: Throw errors if too many entries?
                        var parts = n.Split(new char[]{'-'}, StringSplitOptions.RemoveEmptyEntries).Select(x => Convert.ToInt64(x.Trim())).ToArray();
                        for(var i = Math.Min(parts[0], parts[1]); i <= Math.Max(parts[0], parts[1]); i++)
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
        public bool AllVersions { get { return GetBool("all-versions"); } }

        /// <summary>
        /// A value indicating if only the largest common prefix is returned
        /// </summary>
        public bool ListPrefixOnly { get { return GetBool("list-prefix-only"); } }

        /// <summary>
        /// A value indicating if only folder contents are returned
        /// </summary>
        public bool ListFolderContents { get { return GetBool("list-folder-contents"); } }

        /// <summary>
        /// A value indicating that only filesets are returned
        /// </summary>
        public bool ListSetsOnly { get { return GetBool("list-sets-only"); } }

        /// <summary>
        /// A value indicating if file time checks are skipped
        /// </summary>
        public bool DisableFiletimeCheck { get { return GetBool("disable-filetime-check"); } }

        /// <summary>
        /// A value indicating if file time checks are skipped
        /// </summary>
        public bool CheckFiletimeOnly { get { return GetBool("check-filetime-only"); } }

        /// <summary>
        /// A value indicating if USN numbers are used to get list of changed files
        /// </summary>
        //public bool DisableUSNDiffCheck { get { return GetBool("disable-usn-diff-check"); } }

        /// <summary>
        /// A value indicating if time tolerance is disabled
        /// </summary>
        public bool DisableTimeTolerance { get { return GetBool("disable-time-tolerance"); } }

        /// <summary>
        /// Gets a value indicating whether a temporary folder has been specified
        /// </summary>
        public bool HasTempDir { get { return m_options.ContainsKey("tempdir") && !string.IsNullOrEmpty(m_options["tempdir"]); } }

        /// <summary>
        /// Gets the folder where temporary files are stored
        /// </summary>
        public string TempDir
        {
            get
            {
                if (!m_options.ContainsKey("tempdir") || string.IsNullOrEmpty(m_options["tempdir"]))
                {
                    return Duplicati.Library.Utility.TempFolder.SystemTempPath;
                }

                return m_options["tempdir"];
            }
        }

        /// <summary>
        /// Gets a value indicating whether the user has forced the locale
        /// </summary>
        public bool HasForcedLocale { get { return m_options.ContainsKey("force-locale"); } }

        /// <summary>
        /// Gets the forced locale for the current user
        /// </summary>
        public System.Globalization.CultureInfo ForcedLocale
        {
            get
            {
                if (!m_options.ContainsKey("force-locale"))
                    return System.Threading.Thread.CurrentThread.CurrentCulture;
                else
                {
                    var localestring = m_options["force-locale"];
                    if (string.IsNullOrWhiteSpace(localestring))
                        return System.Globalization.CultureInfo.InvariantCulture;
                    else
                        return new System.Globalization.CultureInfo(localestring);
                }
            }
        }

        /// <summary>
        /// Gets the process priority
        /// </summary>
        public string ThreadPriority
        {
            get
            {
                if (!m_options.ContainsKey("thread-priority") || string.IsNullOrEmpty(m_options["thread-priority"]))
                    return null;
                else
                    return m_options["thread-priority"];
            }
        }

        /// <summary>
        /// A value indicating if missing folders should be created automatically
        /// </summary>
        public bool AutocreateFolders { get { return !GetBool("disable-autocreate-folder"); } }

        /// <summary>
        /// Gets the backup prefix
        /// </summary>
        public string Prefix
        {
            get
            {
                string v;
                m_options.TryGetValue("prefix", out v);
                if (!string.IsNullOrEmpty(v))
                    return v;
                    
                return "duplicati";
            }
        }

        /// <summary>
        /// Gets the number of old backups to keep
        /// </summary>
        public int KeepVersions
        {
            get
            {
                string v;
                m_options.TryGetValue("keep-versions", out v);
                if (string.IsNullOrEmpty(v))
                    return DEFAULT_KEEP_VERSIONS;
                
                return Math.Max(0, int.Parse(v));
            }
        }

        /// <summary>
        /// Gets the timelimit for removal
        /// </summary>
        public DateTime KeepTime
        {
            get
            {
                string v;
                m_options.TryGetValue("keep-time", out v);
                
                if (string.IsNullOrEmpty(v))
                    return new DateTime(0);

                TimeSpan tolerance =
                    this.DisableTimeTolerance ?
                    TimeSpan.FromSeconds(0) :
                    TimeSpan.FromSeconds(Math.Min(Library.Utility.Timeparser.ParseTimeSpan(v).TotalSeconds / 100, 60.0 * 60.0));

                return Library.Utility.Timeparser.ParseTimeInterval(v, DateTime.Now, true) - tolerance;
            }
        }

        /// <summary>
        /// Gets the time frames and intervals for the retention policy
        /// </summary>        
        public List<RetentionPolicyValue> RetentionPolicy
        {
            get {
                var retentionPolicyConfig = new List<RetentionPolicyValue>();

                string v;
                m_options.TryGetValue("retention-policy", out v);
                if (string.IsNullOrEmpty(v)) { 
                    return retentionPolicyConfig;
                }

                var periodIntervalStrings = v.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var periodIntervalString in periodIntervalStrings)
                {
                    retentionPolicyConfig.Add(RetentionPolicyValue.CreateFromString(periodIntervalString));
                }

                return retentionPolicyConfig;
            }
        }

        /// <summary>
        /// Gets the encryption passphrase
        /// </summary>
        public string Passphrase
        {
            get
            {
                if (!m_options.ContainsKey("passphrase") || string.IsNullOrEmpty(m_options["passphrase"]))
                    return null;
                else
                    return m_options["passphrase"];
            }
        }

        /// <summary>
        /// A value indicating if backups are not encrypted
        /// </summary>
        public bool NoEncryption { get { return GetBool("no-encryption"); } }

        /// <summary>
        /// Gets the module used for encryption
        /// </summary>
        public string EncryptionModule
        {
            get
            {
                //Disabled?
                if (NoEncryption)
                    return null;

                //Specified?
                if (m_options.ContainsKey("encryption-module"))
                    return m_options["encryption-module"];

                return "aes";
            }
        }

        /// <summary>
        /// Gets the module used for compression
        /// </summary>
        public string CompressionModule
        {
            get
            {
                if (m_options.ContainsKey("compression-module"))
                    return m_options["compression-module"];
                else
                    return "zip";
            }
        }


        /// <summary>
        /// Gets the number of time to retry transmission if it fails
        /// </summary>
        public int NumberOfRetries
        {
            get
            {
                if (!m_options.ContainsKey("number-of-retries") || string.IsNullOrEmpty(m_options["number-of-retries"]))
                    return 5;
                else
                {
                    int x = int.Parse(m_options["number-of-retries"]);
                    if (x < 0)
                        throw new UserInformationException("Invalid count for number-of-retries", "NumberOfRetriesInvalid");

                    return x;
                }
            }
        }

        /// <summary>
        /// A value indicating if backups are transmitted on a separate thread
        /// </summary>
        public bool SynchronousUpload { get { return Library.Utility.Utility.ParseBoolOption(m_options, "synchronous-upload"); } }

        /// <summary>
        /// A value indicating if system is allowed to enter sleep power states during backup/restore
        /// </summary>
        public bool AllowSleep { get { return GetBool("allow-sleep"); } }

        /// <summary>
        /// A value indicating if system should use the low-priority IO during backup/restore
        /// </summary>
        public bool UseBackgroundIOPriority { get { return GetBool("use-background-io-priority"); } }

        /// <summary>
        /// A value indicating if use of the streaming interface is disallowed
        /// </summary>
        public bool DisableStreamingTransfers { get { return GetBool("disable-streaming-transfers"); } }

        /// <summary>
        /// A value indicating if multithreaded pipes may be used for hashing and crypting on up-/downloads
        /// </summary>
        public bool DisablePipedStreaming { get { return GetBool("disable-piped-streaming"); } }

        /// <summary>
        /// Gets the delay period to retry uploads
        /// </summary>
        public TimeSpan RetryDelay
        {
            get
            {
                if (!m_options.ContainsKey("retry-delay") || string.IsNullOrEmpty(m_options["retry-delay"]))
                    return new TimeSpan(TimeSpan.TicksPerSecond * 10);
                else
                    return Library.Utility.Timeparser.ParseTimeSpan(m_options["retry-delay"]);
            }
        }

        /// <summary>
        /// Gets whether exponential backoff is enabled
        /// </summary>
        public Boolean RetryWithExponentialBackoff
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "retry-with-exponential-backoff"); }
        }

        /// <summary>
        /// Gets the max upload speed in bytes pr. second
        /// </summary>
        public long MaxUploadPrSecond
        {
            get
            {
                lock(m_lock)
                {
                    string v;
                    m_options.TryGetValue("throttle-upload", out v);
                    if (string.IsNullOrEmpty(v))
                        return 0;
                    else
                        return Library.Utility.Sizeparser.ParseSize(v, "kb");
                }
            }
            set
            {
                lock (m_lock)
                    if (value <= 0)
                        m_options["throttle-upload"] = "";
                    else
                        m_options["throttle-upload"] = value.ToString() + "b";
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
                {
                    string v;
                    m_options.TryGetValue("throttle-download", out v);
                    if (string.IsNullOrEmpty(v))
                        return 0;
                    else
                        return Library.Utility.Sizeparser.ParseSize(v, "kb");
                }
            }
            set
            {
                lock (m_lock)
                    if (value <= 0)
                        m_options["throttle-download"] = "";
                    else
                        m_options["throttle-download"] = value.ToString() + "b";
            }
        }

        /// <summary>
        /// A value indicating if the backup is a full backup
        /// </summary>
        public bool AllowFullRemoval { get { return GetBool("allow-full-removal"); } }

        /// <summary>
        /// A value indicating if debug output is enabled
        /// </summary>
        public bool DebugOutput { get { return GetBool("debug-output"); } }

        /// <summary>
        /// A value indicating if unchanged backups are uploaded
        /// </summary>
        public bool UploadUnchangedBackups { get { return GetBool("upload-unchanged-backups"); } }

        /// <summary>
        /// Gets a list of modules that should be loaded
        /// </summary>
        public string[] EnableModules
        {
            get
            {
                if (m_options.ContainsKey("enable-module"))
                    return m_options["enable-module"].Trim().ToLower(CultureInfo.InvariantCulture).Split(',');
                else
                    return new string[0];
            }
        }

        /// <summary>
        /// Gets a list of modules that should not be loaded
        /// </summary>
        public string[] DisableModules
        {
            get
            {
                if (m_options.ContainsKey("disable-module"))
                    return m_options["disable-module"].Trim().ToLower(CultureInfo.InvariantCulture).Split(',');
                else
                    return new string[0];
            }
        }

        /// <summary>
        /// Gets the snapshot strategy to use
        /// </summary>
        public OptimizationStrategy SnapShotStrategy
        {
            get
            {
                string strategy;
                if (!m_options.TryGetValue("snapshot-policy", out strategy))
                    strategy = "";

                OptimizationStrategy r;
                if (!Enum.TryParse(strategy, true, out r))
                    r = OptimizationStrategy.Off;

                return r;
            }
        }

        /// <summary>
        /// Gets the symlink strategy to use
        /// </summary>
        public SymlinkStrategy SymlinkPolicy
        {
            get
            {
                string policy;
                if (!m_options.TryGetValue("symlink-policy", out policy))
                    policy = "";

                SymlinkStrategy r;
                if (!Enum.TryParse(policy, true, out r))
                    r = SymlinkStrategy.Store;

                return r;
            }
        }

        /// <summary>
        /// Gets the hardlink strategy to use
        /// </summary>
        public HardlinkStrategy HardlinkPolicy
        {
            get
            {
                string policy;
                if (!m_options.TryGetValue("hardlink-policy", out policy))
                    policy = "";

                HardlinkStrategy r;
                if (!Enum.TryParse(policy, true, out r))
                    r = HardlinkStrategy.All;

                return r;
            }
        }
        /// <summary>
        /// Gets the update sequence number (USN) strategy to use
        /// </summary>
        public OptimizationStrategy UsnStrategy
        {
            get
            {
                string strategy;
                if (!m_options.TryGetValue("usn-policy", out strategy))
                    strategy = "";

                OptimizationStrategy r;
                if (!Enum.TryParse(strategy, true, out r))
                    r = OptimizationStrategy.Off;

                return r;
            }
        }

        /// <summary>
        /// Gets the number of concurrent volume uploads allowed. Zero for unlimited.
        /// </summary>
        public int AsynchronousConcurrentUploadLimit
        {
            get
            {
                if (!m_options.TryGetValue("asynchronous-concurrent-upload-limit", out var value))
                    value = null;

                if (string.IsNullOrEmpty(value))
                    return 4;
                else
                    return int.Parse(value);
            }
        }

        /// <summary>
        /// Gets the number of volumes to create ahead of time when using async transfers,
        /// a value of zero indicates no limit
        /// </summary>
        public long AsynchronousUploadLimit
        {
            get
            {
                string value;
                if (!m_options.TryGetValue("asynchronous-upload-limit", out value))
                    value = null;

                if (string.IsNullOrEmpty(value))
                    return 4;
                else
                    return long.Parse(value);
            }
        }

        /// <summary>
        /// Gets the temporary folder to use for asynchronous transfers
        /// </summary>
        public string AsynchronousUploadFolder
        {
            get
            {
                string value;
                if (!m_options.TryGetValue("asynchronous-upload-folder", out value))
                    value = null;

                if (string.IsNullOrEmpty(value))
                    return this.TempDir;
                else
                    return value;
            }
        }
        
        /// <summary>
        /// Gets the logfile filename
        /// </summary>
        public string Logfile
        {
            get
            {
                string value;
                if (!m_options.TryGetValue("log-file", out value))
                    value = null;
                return value;
            }
        }

        /// <summary>
        /// Gets the log-file detail level
        /// </summary>
        public Duplicati.Library.Logging.LogMessageType LogFileLoglevel
        {
            get
            {
                string value;
                if (!m_options.TryGetValue("log-file-log-level", out value))
                    value = null;

                if (string.IsNullOrWhiteSpace(value))
                    if (!m_options.TryGetValue("log-level", out value))
                        value = null;

                foreach (string s in Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType)))
                    if (s.Equals(value, StringComparison.OrdinalIgnoreCase))
                        return (Duplicati.Library.Logging.LogMessageType)Enum.Parse(typeof(Duplicati.Library.Logging.LogMessageType), s);

                return Duplicati.Library.Logging.LogMessageType.Warning;
            }
        }

        /// <summary>
        /// Gets the filter used for log-file messages.
        /// </summary>
        /// <value>The log file filter.</value>
        public IFilter LogFileLogFilter
        {
            get
            {
                m_options.TryGetValue("log-file-log-filter", out var value);
                return Library.Utility.FilterExpression.ParseLogFilter(value);
            }
        }

        /// <summary>
        /// Gets the filter used for console messages.
        /// </summary>
        /// <value>The log file filter.</value>
        public IFilter ConsoleLogFilter
        {
            get
            {
                m_options.TryGetValue("console-log-filter", out var value);
                return Library.Utility.FilterExpression.ParseLogFilter(value);
            }
        }

        /// <summary>
        /// Gets the console log detail level
        /// </summary>
        public Duplicati.Library.Logging.LogMessageType ConsoleLoglevel
        {
            get
            {
                string value;
                if (!m_options.TryGetValue("console-log-level", out value))
                    value = null;

                if (string.IsNullOrWhiteSpace(value))
                    if (!m_options.TryGetValue("log-level", out value))
                        value = null;

                foreach (string s in Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType)))
                    if (s.Equals(value, StringComparison.OrdinalIgnoreCase))
                        return (Duplicati.Library.Logging.LogMessageType)Enum.Parse(typeof(Duplicati.Library.Logging.LogMessageType), s);

                return Duplicati.Library.Logging.LogMessageType.Warning;
            }
        }

        /// <summary>
        /// A value indicating if all database queries should be logged
        /// </summary>
        public bool ProfileAllDatabaseQueries { get { return GetBool("profile-all-database-queries"); } }

        /// <summary>
        /// Gets the attribute filter used to exclude files and folders.
        /// </summary>
        public System.IO.FileAttributes FileAttributeFilter
        {
            get
            {
                System.IO.FileAttributes res = default(System.IO.FileAttributes);
                string v;
                if (!m_options.TryGetValue("exclude-files-attributes", out v))
                    return res;

                foreach(string s in v.Split(new string[] {","}, StringSplitOptions.RemoveEmptyEntries))
                {
                    System.IO.FileAttributes f;
                    if (Enum.TryParse(s.Trim(), true, out f))
                        res |= f;
                }

                return res;
            }
        }

        /// <summary>
        /// A value indicating if server uploads are verified by listing the folder contents
        /// </summary>
        public bool ListVerifyUploads { get { return GetBool("list-verify-uploads"); } }

        /// <summary>
        /// A value indicating if connections cannot be re-used
        /// </summary>
        public bool NoConnectionReuse { get { return GetBool("no-connection-reuse"); } }

        /// <summary>
        /// A value indicating if the returned value should not be truncated
        /// </summary>
        public bool FullResult { get { return GetBool("full-result"); } }

        /// <summary>
        /// A value indicating restored files overwrite existing ones
        /// </summary>
        public bool Overwrite { get { return GetBool("overwrite"); } }

        /// <summary>
        /// Gets the total size in bytes that the backend supports, returns -1 if there is no upper limit
        /// </summary>
        public long QuotaSize
        {
            get
            {
                if (!m_options.ContainsKey("quota-size") || string.IsNullOrEmpty(m_options["quota-size"]))
                    return -1;
                else
                    return Library.Utility.Sizeparser.ParseSize(m_options["quota-size"], "mb");
            }
        }

        /// <summary>
        /// Gets the threshold at which a quota warning should be generated.
        /// </summary>
        /// <remarks>
        /// This is treated as a percentage, where a warning is given when the amount of free space is less than this percentage of the backup size.
        /// </remarks>
        public int QuotaWarningThreshold
        {
            get
            {
                string tmp;
                m_options.TryGetValue("quota-warning-threshold", out tmp);
                if (string.IsNullOrEmpty(tmp))
                {
                    return DEFAULT_QUOTA_WARNING_THRESHOLD;
                }
                else
                {
                    return int.Parse(tmp);
                }
            }
        }

        /// <summary>
        /// Gets the display name of the backup
        /// </summary>
        public string BackupName
        {
            get
            {
                string tmp;
                m_options.TryGetValue("backup-name", out tmp);
                if (string.IsNullOrEmpty(tmp))
                    return DefaultBackupName;
                else
                    return tmp;
            }
            set
            {
                m_options["backup-name"] = value;
            }
        }

        /// <summary>
        /// Gets the path to the database
        /// </summary>
        public string Dbpath
        {
            get
            {
                string tmp;
                m_options.TryGetValue("dbpath", out tmp);
                return tmp;
            }
            set
            {
                m_options["dbpath"] = value;
            }
        }

        /// <summary>
        /// Gets a value indicating whether a blocksize has been specified
        /// </summary>
        public bool HasBlocksize { get { return m_options.ContainsKey("blocksize") && !string.IsNullOrEmpty(m_options["blocksize"]); } }

        /// <summary>
        /// Gets the size of file-blocks
        /// </summary>
        public int Blocksize
        {
            get
            {
                string tmp;
                if (!m_options.TryGetValue("blocksize", out tmp))
                    tmp = DEFAULT_BLOCKSIZE;

                long blocksize = Library.Utility.Sizeparser.ParseSize(tmp, "kb");
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
					m_cachedBlockHashSize = new KeyValuePair<string, int>(BlockHashAlgorithm, Duplicati.Library.Utility.HashAlgorithmHelper.Create(BlockHashAlgorithm).HashSize / 8);
				
				return m_cachedBlockHashSize.Value;
            }
        }

        /// <summary>
        /// Gets a flag indicating if metadata for files and folders should be ignored
        /// </summary>
        public bool SkipMetadata
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "skip-metadata"); }
        }

        /// <summary>
        /// Gets a flag indicating if empty folders should be ignored
        /// </summary>
        public bool ExcludeEmptyFolders
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "exclude-empty-folders"); }
        }

        /// <summary>
        /// Gets a flag indicating if during restores metadata should be applied to the symlink target.
        /// Setting this to true can result in errors if the target no longer exists.
        /// </summary>
        public bool RestoreSymlinkMetadata
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "restore-symlink-metadata"); }
        }

        /// <summary>
        /// Gets a flag indicating if permissions should be restored
        /// </summary>
        public bool RestorePermissions
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "restore-permissions"); }
        }


        /// <summary>
        /// Gets a flag indicating if file hashes are checked after a restore
        /// </summary>
        public bool PerformRestoredFileVerification
        {
            get { return !Library.Utility.Utility.ParseBoolOption(m_options, "skip-restore-verification"); }
        }

        /// <summary>
        /// Gets a flag indicating if synthetic filelist generation is disabled
        /// </summary>
        public bool DisableSyntheticFilelist
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "disable-synthetic-filelist"); }
        }

        /// <summary>
        /// Flag indicating if the in-memory block cache is used
        /// </summary>
        public bool UseBlockCache
        {
            get
            {
                return Library.Utility.Utility.ParseBoolOption(m_options, "use-block-cache");
            }
        }
        
        
        /// <summary>
        /// Gets the compact threshold
        /// </summary>
        public long Threshold
        {
            get
            {
                string v;
                m_options.TryGetValue("threshold", out v);
                if (string.IsNullOrEmpty(v))
                    return DEFAULT_THRESHOLD;

                return Convert.ToInt64(v);
            }
        }

        /// <summary>
        /// Gets the size of small volumes
        /// </summary>
        public long SmallFileSize
        {
            get
            {
                string v;
                m_options.TryGetValue("small-file-size", out v);
                if (string.IsNullOrEmpty(v))
                    return this.VolumeSize / 5;

                return Library.Utility.Sizeparser.ParseSize(v, "mb");
            }
        }
        
        /// <summary>
        /// Gets the maximum number of small volumes
        /// </summary>
        public long SmallFileMaxCount
        {
            get
            {
                string v;
                m_options.TryGetValue("small-file-max-count", out v);
                if (string.IsNullOrEmpty(v))
                    return DEFAULT_SMALL_FILE_MAX_COUNT;

                return Convert.ToInt64(v);
            }
        }
        
        /// <summary>
        /// List of files to check for changes
        /// </summary>
        public string[] ChangedFilelist
        {
            get
            {
                string v;
                m_options.TryGetValue("changed-files", out v);
                if (string.IsNullOrEmpty(v))
                    return null;

                return v.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        /// <summary>
        /// List of files to mark as deleted
        /// </summary>
        public string[] DeletedFilelist
        {
            get
            {
                string v;
                m_options.TryGetValue("deleted-files", out v);
                if (string.IsNullOrEmpty(v))
                    return null;

                return v.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        /// <summary>
        /// List of filenames that are used to exclude a folder
        /// </summary>
        public string[] IgnoreFilenames
        {
            get
            {
                string v;
                m_options.TryGetValue("ignore-filenames", out v);
                if (string.IsNullOrEmpty(v))
                    return null;

                return v.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            }
        }
        /// <summary>
        /// Alternate restore path
        /// </summary>
        public string Restorepath
        {
            get
            {
                string v;
                m_options.TryGetValue("restore-path", out v);
                return v;
            }
        }
        
        /// <summary>
        /// Gets the index file usage method
        /// </summary>
        public IndexFileStrategy IndexfilePolicy
        {
            get 
            { 
                string strategy;
                if (!m_options.TryGetValue("index-file-policy", out strategy))
                    strategy = "";
                
                IndexFileStrategy res;
                if (!Enum.TryParse(strategy, true, out res))
                    res = IndexFileStrategy.Full;
                    
                return res;
            }
        }
        
        /// <summary>
        /// Gets a flag indicating if the check for files on the remote storage should be omitted
        /// </summary>
        public bool NoBackendverification
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "no-backend-verification"); }
        }
                
        /// <summary>
        /// Gets the percentage of samples to test during a backup operation
        /// </summary>
        public long BackupTestPercentage
        {
            get
            {
                m_options.TryGetValue("backup-test-percentage", out string s);
                if (string.IsNullOrEmpty(s))
                {
                    return 0;
                }

                long percentage;
                try
                {
                    percentage = long.Parse(s);
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
        public long BackupTestSampleCount
        {
            get 
            { 
                string s;
                m_options.TryGetValue("backup-test-samples", out s);
                if (string.IsNullOrEmpty(s))
                    return 1;
                
                return long.Parse(s);
            }
        }
        
        /// <summary>
        /// Gets a flag indicating if compacting should not be done automatically
        /// </summary>
        public bool NoAutoCompact
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "no-auto-compact"); }
        }

        /// <summary>
        /// Gets the minimum time that must elapse after last compaction before running next automatic compaction
        /// </summary>
        public TimeSpan AutoCompactInterval
        {
            get
            {
                if (!m_options.ContainsKey("auto-compact-interval") || string.IsNullOrEmpty(m_options["auto-compact-interval"]))
                    return TimeSpan.Zero;
                else
                    return Library.Utility.Timeparser.ParseTimeSpan(m_options["auto-compact-interval"]);
            }
        }

        /// <summary>
        /// Gets a flag indicating if compacting should not be done automatically
        /// </summary>
        public bool AllowMissingSource
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "allow-missing-source"); }
        }
        
        /// <summary>
        /// Gets a value indicating if a verification file should be uploaded after changing the remote store
        /// </summary>
        public bool UploadVerificationFile
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "upload-verification-file"); } 
        }
        
        /// <summary>
        /// Gets a value indicating if a passphrase change is allowed
        /// </summary>
        public bool AllowPassphraseChange
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "allow-passphrase-change"); } 
        }

        /// <summary>
        /// Gets a flag indicating if the current operation should merely output the changes
        /// </summary>
        public bool Dryrun
        {
            get 
            {
                if (m_options.ContainsKey("dry-run"))
                    return Library.Utility.Utility.ParseBoolOption(m_options, "dry-run"); 
                else
                    return Library.Utility.Utility.ParseBoolOption(m_options, "dryrun"); 
            }
        }

        /// <summary>
        /// Gets a flag indicating if the remote verification is deep
        /// </summary>
        public bool FullRemoteVerification
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "full-remote-verification"); }
        }
        
        /// <summary>
        /// The block hash algorithm to use
        /// </summary>
        public string BlockHashAlgorithm
        {
            get
            {
                string v;
                m_options.TryGetValue("block-hash-algorithm", out v);
                if (string.IsNullOrEmpty(v))
                    return DEFAULT_BLOCK_HASH_ALGORITHM;

                return v;
            }
        }

        /// <summary>
        /// The file hash algorithm to use
        /// </summary>
        public string FileHashAlgorithm
        {
            get
            {
                string v;
                m_options.TryGetValue("file-hash-algorithm", out v);
                if (string.IsNullOrEmpty(v))
                    return DEFAULT_FILE_HASH_ALGORITHM;

                return v;
            }
        }

        /// <summary>
        /// Gets a flag indicating if the current operation is intended to delete files older than a certain threshold
        /// </summary>
        public bool PatchWithLocalBlocks
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "patch-with-local-blocks"); }
        }

        /// <summary>
        /// Gets a value indicating whether local blocks usage should be skipped.
        /// </summary>
        /// <value><c>true</c> if no local blocks; otherwise, <c>false</c>.</value>
        public bool NoLocalBlocks
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "no-local-blocks"); } 
        }

        /// <summary>
        /// Gets a flag indicating if the local database should not be used
        /// </summary>
        /// <value><c>true</c> if no local db is used; otherwise, <c>false</c>.</value>
        public bool NoLocalDb
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "no-local-db"); }
        }

        /// <summary>
        /// Gets a flag indicating if the local database should not be used
        /// </summary>
        /// <value><c>true</c> if no local db is used; otherwise, <c>false</c>.</value>
        public bool DontCompressRestorePaths
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "dont-compress-restore-paths"); }
        }

        /// <summary>
        /// Gets a flag indicating if block hashes are checked before being applied
        /// </summary>
        /// <value><c>true</c> if block hashes are checked; otherwise, <c>false</c>.</value>
        public bool FullBlockVerification
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "full-block-verification"); }
        }

        /// <summary>
        /// Gets a flag indicating if the repair process will only restore paths
        /// </summary>
        /// <value><c>true</c> if only paths are restored; otherwise, <c>false</c>.</value>
        public bool RepairOnlyPaths
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "repair-only-paths"); }
        }

        /// <summary>
        /// Gets a flag indicating if the repair process will always use blocks
        /// </summary>
        /// <value><c>true</c> if repair process always use blocks; otherwise, <c>false</c>.</value>
        public bool RepairForceBlockUse
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "repair-force-block-use"); }
        }

        /// <summary>
        /// Gets a flag indicating whether the VACUUM operation should ever be run automatically.
        /// </summary>
        public bool AutoVacuum
        {
            get { return GetBool("auto-vacuum"); }
        }

        /// <summary>
        /// Gets the minimum time that must elapse after last vacuum before running next automatic vacuum
        /// </summary>
        public TimeSpan AutoVacuumInterval
        {
            get
            {
                if (!m_options.ContainsKey("auto-vacuum-interval") || string.IsNullOrEmpty(m_options["auto-vacuum-interval"]))
                    return TimeSpan.Zero;
                else
                    return Library.Utility.Timeparser.ParseTimeSpan(m_options["auto-vacuum-interval"]);
            }
        }

        /// <summary>
        /// Gets a flag indicating if the local filescanner should be disabled
        /// </summary>
        /// <value><c>true</c> if the filescanner should be disabled; otherwise, <c>false</c>.</value>
        public bool DisableFileScanner
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "disable-file-scanner"); }
        }

        /// <summary>
        /// Gets a flag indicating if the filelist consistency checks should be disabled
        /// </summary>
        /// <value><c>true</c> if the filelist consistency checks should be disabled; otherwise, <c>false</c>.</value>
        public bool DisableFilelistConsistencyChecks
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "disable-filelist-consistency-checks"); }
        }

        /// <summary>
        /// Gets a flag indicating whether the backup should be disabled when on battery power.
        /// </summary>
        /// <value><c>true</c> if the backup should be disabled when on battery power; otherwise, <c>false</c>.</value>
        public bool DisableOnBattery
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "disable-on-battery"); }
        }

        /// <summary>
        /// Gets a value indicating if missing dblock files are attempted created
        /// </summary>
        public bool RebuildMissingDblockFiles
        {
            get { return GetBool("rebuild-missing-dblock-files"); }
        }

        /// <summary>
        /// Gets the threshold for when log data should be cleaned
        /// </summary>
        public DateTime LogRetention
        {
            get
            {
                string pts;
                if (!m_options.TryGetValue("log-retention", out pts))
                    pts = DEFAULT_LOG_RETENTION;

                return Library.Utility.Timeparser.ParseTimeInterval(pts, DateTime.Now, true);
            }
        }


        /// <summary>
        /// Gets the number of concurrent threads
        /// </summary>
        public int ConcurrencyMaxThreads
        {
            get
            {
                string value;
                if (!m_options.TryGetValue("concurrency-max-threads", out value))
                    value = null;

                if (string.IsNullOrEmpty(value))
                    return 0;
                else
                    return int.Parse(value);
            }
        }

        /// <summary>
        /// Gets the number of concurrent block hashers
        /// </summary>
        public int ConcurrencyBlockHashers
        {
            get
            {
                string value;
                if (!m_options.TryGetValue("concurrency-block-hashers", out value))
                    value = null;

                if (string.IsNullOrEmpty(value))
                    return DEFAULT_BLOCK_HASHERS;
                else
                    return Math.Max(1, int.Parse(value));
            }
        }

        /// <summary>
        /// Gets the number of concurrent block hashers
        /// </summary>
        public int ConcurrencyCompressors
        {
            get
            {
                string value;
                if (!m_options.TryGetValue("concurrency-compressors", out value))
                    value = null;

                if (string.IsNullOrEmpty(value))
                    return DEFAULT_COMPRESSORS;
                else
                    return Math.Max(1, int.Parse(value));
            }
        }

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

                    string file;
                    if (!m_options.TryGetValue("compression-extension-file", out file))
                        file = DEFAULT_COMPRESSED_EXTENSION_FILE;

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
        /// Gets a compression hint from a filename
        /// </summary>
        /// <param name="filename">The filename to get the hint for</param>
        /// <returns>The compression hint</returns>
        public CompressionHint GetCompressionHintFromFilename(string filename)
        {
            CompressionHint h;
            if (!CompressionHints.TryGetValue(System.IO.Path.GetExtension(filename), out h))
                return CompressionHint.Default;
            return h;
        }

        /// <summary>
        /// Gets a list of modules, the key indicates if they are loaded 
        /// </summary>
        public List<KeyValuePair<bool, Library.Interface.IGenericModule>> LoadedModules { get { return m_loadedModules; } }

        /// <summary>
        /// Helper method to extract boolean values.
        /// If the option is not present, it it's value is false.
        /// If the option is present it's value is true, unless the option's value is false, off or 0
        /// </summary>
        /// <param name="name">The name of the option to read</param>
        /// <returns>The interpreted value of the option</returns>
        private bool GetBool(string name)
        {
            return Library.Utility.Utility.ParseBoolOption(m_options, name);
        }

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
