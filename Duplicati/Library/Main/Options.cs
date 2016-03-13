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
using System.Text;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// A class for keeping all Duplicati options in one place,
    /// and provide typesafe access to the options
    /// </summary>
    public class Options
    {
        private const string DEFAULT_BLOCK_HASH_LOOKUP_SIZE = "64mb";
        private const string DEFAULT_METADATA_HASH_LOOKUP_SIZE = "64mb";
        private const string DEFAULT_FILE_HASH_LOOKUP_SIZE = "32mb";
        
        private const string DEFAULT_BLOCK_HASH_ALGORITHM = "SHA256";
        private const string DEFAULT_FILE_HASH_ALGORITHM = "SHA256";
        
        /// <summary>
        /// The default block size
        /// </summary>
        private const string DEFAULT_BLOCKSIZE = "100kb";
        
        /// <summary>
        /// The default size of the read-ahead buffer
        /// </summary>
        private const string DEFAULT_READ_BUFFER_SIZE = "5mb";
        
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
            foreach(var h in new string[] {"SHA1", "MD5", "SHA256", "SHA384", "SHA512"})
            try 
            {
                var p = System.Security.Cryptography.HashAlgorithm.Create(h);
                if (p != null)
                    r.Add(h);
            }
            catch
            {
            }
            
            return r.ToArray();
        }

        private static readonly string DEFAULT_COMPRESSED_EXTENSION_FILE = System.IO.Path.Combine(Duplicati.Library.AutoUpdater.UpdaterManager.InstalledBaseDir, "default_compressed_extensions.txt");

        /// <summary>
        /// Lock that protects the options collection
        /// </summary>
        protected object m_lock = new object();

        protected Dictionary<string, string> m_options;

        protected List<KeyValuePair<bool, Library.Interface.IGenericModule>> m_loadedModules = new List<KeyValuePair<bool, IGenericModule>>();

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
        /// Gets all commands that effect a backup
        /// </summary>
        public static string[] BackupOptions
        {
            get
            {
                return new string[] {
                    "dblock-size",
                    "disable-autocreate-folder",
                    "disable-filetime-check",
                    "disable-time-tolerance",
                    "allow-missing-source",
                    "skip-files-larger-than",
                    "upload-unchanged-backups",
                    "list-verify-uploads",
                    "control-files",
                    "snapshot-policy",
                    "vss-exclude-writers",
                    "vss-use-mapping",
                    "usn-policy",
                    "symlink-policy",
                    "hardlink-policy",
                    "exclude-files-attributes",
                    "compression-extension-file",
                    "full-remote-verification"
                };
            }
        }

        /// <summary>
        /// Gets all options that affect a connection
        /// </summary>
        public static string[] ConnectionOptions
        {
            get
            {
                return new string[] {
                    "thread-priority",
                    "number-of-retries",
                    "retry-delay",
                    "synchronous-upload",
                    "asynchronous-upload-limit",
                    "asynchronous-upload-folder",
                    "disable-streaming-transfer",
                    "max-upload-pr-second",
                    "max-download-pr-second",
                    "no-connection-reuse",
                    "allow-sleep"
                };
            }
        }

        /// <summary>
        /// Gets all options that affect a filename parsing
        /// </summary>
        public static string[] FilenameOptions
        {
            get
            {
                return new string[] {
                    "prefix",
                    "tempdir"
                };
            }
        }

        /// <summary>
        /// Gets all options that can be used for debugging
        /// </summary>
        public static string[] DebugOptions
        {
            get
            {
                return new string[] {
                    "debug-output",
                    "debug-retry-errors",
                    "log-file",
                    "log-level",
                };
            }
        }

        /// <summary>
        /// Gets all options that affect module loading
        /// </summary>
        public static string[] ModuleOptions
        {
            get
            {
                return new string[] {
                    "encryption-module",
                    "compression-module",
                    "enable-module",
                    "disable-module",
                    "no-encryption"
                };
            }
        }

        /// <summary>
        /// Gets all options that affect encryption
        /// </summary>
        public static string[] EncryptionOptions
        {
            get
            {
                return new string[] {
                    "encryption-module",
                    "passphrase",
                    "no-encryption"
                };
            }
        }

        /// <summary>
        /// Gets all options that affect cleanup commands
        /// </summary>
        public static string[] CleanupOptions
        {
            get
            {
                return new string[] {
                    "dry-run"
                };
            }
        }

        /// <summary>
        /// Gets all options that affect restore commands
        /// </summary>
        public static string[] RestoreOptions
        {
            get
            {
                return new string[] {
                    "skip-file-hash-checks",
                    "dont-read-manifests",
                    "restore-path",
                    "time",
                    "version",
                    "allow-passphrase-change",
                    "no-local-db",
                    "no-local-blocks",
                    "full-block-verification"
                };
            }
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
                    //new CommandLineArgument("disable-usn-diff-check", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisableusndiffcheckShort, Strings.Options.DisableusndiffcheckLong, "false"),
                    new CommandLineArgument("disable-time-tolerance", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisabletimetoleranceShort, Strings.Options.DisabletimetoleranceLong, "false"),

                    new CommandLineArgument("tempdir", CommandLineArgument.ArgumentType.Path, Strings.Options.TempdirShort, Strings.Options.TempdirLong, System.IO.Path.GetTempPath()),
                    new CommandLineArgument("thread-priority", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.ThreadpriorityShort, Strings.Options.ThreadpriorityLong, "normal", null, new string[] {"highest", "high", "abovenormal", "normal", "belownormal", "low", "lowest", "idle" }),

                    new CommandLineArgument("prefix", CommandLineArgument.ArgumentType.String, Strings.Options.PrefixShort, Strings.Options.PrefixLong, "duplicati"),

                    new CommandLineArgument("passphrase", CommandLineArgument.ArgumentType.Password, Strings.Options.PassphraseShort, Strings.Options.PassphraseLong),
                    new CommandLineArgument("no-encryption", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NoencryptionShort, Strings.Options.NoencryptionLong, "false"),

                    new CommandLineArgument("number-of-retries", CommandLineArgument.ArgumentType.Integer, Strings.Options.NumberofretriesShort, Strings.Options.NumberofretriesLong, "5"),
                    new CommandLineArgument("retry-delay", CommandLineArgument.ArgumentType.Timespan, Strings.Options.RetrydelayShort, Strings.Options.RetrydelayLong, "10s"),

                    new CommandLineArgument("synchronous-upload", CommandLineArgument.ArgumentType.Boolean, Strings.Options.SynchronousuploadShort, Strings.Options.SynchronousuploadLong, "false"),
                    new CommandLineArgument("asynchronous-upload-limit", CommandLineArgument.ArgumentType.Integer, Strings.Options.AsynchronousuploadlimitShort, Strings.Options.AsynchronousuploadlimitLong, "4"),
                    new CommandLineArgument("asynchronous-upload-folder", CommandLineArgument.ArgumentType.Path, Strings.Options.AsynchronousuploadfolderShort, Strings.Options.AsynchronousuploadfolderLong, System.IO.Path.GetTempPath()),

                    new CommandLineArgument("disable-streaming-transfers", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisableStreamingShort, Strings.Options.DisableStreamingLong, "false"),

                    new CommandLineArgument("throttle-upload", CommandLineArgument.ArgumentType.Size, Strings.Options.ThrottleuploadShort, Strings.Options.ThrottleuploadLong, "0"),
                    new CommandLineArgument("throttle-download", CommandLineArgument.ArgumentType.Size, Strings.Options.ThrottledownloadShort, Strings.Options.ThrottledownloadLong, "0"),
                    new CommandLineArgument("skip-files-larger-than", CommandLineArgument.ArgumentType.Size, Strings.Options.SkipfileslargerthanShort, Strings.Options.SkipfileslargerthanLong),
                    
                    new CommandLineArgument("upload-unchanged-backups", CommandLineArgument.ArgumentType.Boolean, Strings.Options.UploadUnchangedBackupsShort, Strings.Options.UploadUnchangedBackupsLong, "false"),

                    new CommandLineArgument("snapshot-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.SnapshotpolicyShort, Strings.Options.SnapshotpolicyLong, "off", null, Enum.GetNames(typeof(OptimizationStrategy))),
                    new CommandLineArgument("vss-exclude-writers", CommandLineArgument.ArgumentType.String, Strings.Options.VssexcludewritersShort, Strings.Options.VssexcludewritersLong),
                    new CommandLineArgument("vss-use-mapping", CommandLineArgument.ArgumentType.Boolean, Strings.Options.VssusemappingShort, Strings.Options.VssusemappingLong, "false"),
                    new CommandLineArgument("usn-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.UsnpolicyShort, Strings.Options.UsnpolicyLong, "off", null, Enum.GetNames(typeof(OptimizationStrategy))),

                    new CommandLineArgument("encryption-module", CommandLineArgument.ArgumentType.String, Strings.Options.EncryptionmoduleShort, Strings.Options.EncryptionmoduleLong, "aes"),
                    new CommandLineArgument("compression-module", CommandLineArgument.ArgumentType.String, Strings.Options.CompressionmoduleShort, Strings.Options.CompressionmoduleLong, "zip"),

                    new CommandLineArgument("enable-module", CommandLineArgument.ArgumentType.String, Strings.Options.EnablemoduleShort, Strings.Options.EnablemoduleLong),
                    new CommandLineArgument("disable-module", CommandLineArgument.ArgumentType.String, Strings.Options.DisablemoduleShort, Strings.Options.DisablemoduleLong),

                    new CommandLineArgument("debug-output", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DebugoutputShort, Strings.Options.DebugoutputLong, "false"),
                    new CommandLineArgument("debug-retry-errors", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DebugretryerrorsShort, Strings.Options.DebugretryerrorsLong, "false"),

                    new CommandLineArgument("log-file", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Options.LogfileShort, Strings.Options.LogfileShort),
                    new CommandLineArgument("log-level", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Enumeration, Strings.Options.LoglevelShort, Strings.Options.LoglevelLong, "Warning", null, Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType))),

                    new CommandLineArgument("list-verify-uploads", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ListverifyuploadsShort, Strings.Options.ListverifyuploadsShort, "false"),
                    new CommandLineArgument("allow-sleep", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllowsleepShort, Strings.Options.AllowsleepShort, "false"),
                    new CommandLineArgument("no-connection-reuse", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NoconnectionreuseShort, Strings.Options.NoconnectionreuseLong, "false"),
                    
                    new CommandLineArgument("quota-size", CommandLineArgument.ArgumentType.Size, Strings.Options.QuotasizeShort, Strings.Options.QuotasizeLong),

                    new CommandLineArgument("symlink-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.SymlinkpolicyShort, Strings.Options.SymlinkpolicyLong("store", "ignore", "follow"), Enum.GetName(typeof(SymlinkStrategy), SymlinkStrategy.Store), null, Enum.GetNames(typeof(SymlinkStrategy))),
                    new CommandLineArgument("hardlink-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.HardlinkpolicyShort, Strings.Options.HardlinkpolicyLong("first", "all", "none"), Enum.GetName(typeof(HardlinkStrategy), HardlinkStrategy.All), null, Enum.GetNames(typeof(HardlinkStrategy))),
                    new CommandLineArgument("exclude-files-attributes", CommandLineArgument.ArgumentType.String, Strings.Options.ExcludefilesattributesShort, Strings.Options.ExcludefilesattributesLong(Enum.GetNames(typeof(System.IO.FileAttributes)))),
                    new CommandLineArgument("backup-name", CommandLineArgument.ArgumentType.String, Strings.Options.BackupnameShort, Strings.Options.BackupnameLong, DefaultBackupName),
                    new CommandLineArgument("compression-extension-file", CommandLineArgument.ArgumentType.Path, Strings.Options.CompressionextensionfileShort, Strings.Options.CompressionextensionfileLong(DEFAULT_COMPRESSED_EXTENSION_FILE), DEFAULT_COMPRESSED_EXTENSION_FILE),

                    new CommandLineArgument("verbose", CommandLineArgument.ArgumentType.Boolean, Strings.Options.VerboseShort, Strings.Options.VerboseLong, "false"),

                    new CommandLineArgument("overwrite", CommandLineArgument.ArgumentType.Boolean, Strings.Options.OverwriteShort, Strings.Options.OverwriteLong, "false"),

                    new CommandLineArgument("dbpath", CommandLineArgument.ArgumentType.Path, Strings.Options.DbpathShort, Strings.Options.DbpathLong),
                    new CommandLineArgument("blocksize", CommandLineArgument.ArgumentType.Size, Strings.Options.BlocksizeShort, Strings.Options.BlocksizeLong, DEFAULT_BLOCKSIZE),
                    new CommandLineArgument("file-read-buffer-size", CommandLineArgument.ArgumentType.Size, Strings.Options.FilereadbuffersizeShort, Strings.Options.FilereadbuffersizeLong, "0"),
                    new CommandLineArgument("store-metadata", CommandLineArgument.ArgumentType.Boolean, Strings.Options.StoremetadataShort, Strings.Options.StoremetadataLong, "true", null, null, Strings.Options.StoremetadataDeprecated),
                    new CommandLineArgument("skip-metadata", CommandLineArgument.ArgumentType.Boolean, Strings.Options.SkipmetadataShort, Strings.Options.SkipmetadataLong, "false"),
                    new CommandLineArgument("restore-permissions", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RestorepermissionsShort, Strings.Options.RestorepermissionsLong, "false"),
                    new CommandLineArgument("skip-restore-verification", CommandLineArgument.ArgumentType.Boolean, Strings.Options.SkiprestoreverificationShort, Strings.Options.SkiprestoreverificationLong, "false"),
                    new CommandLineArgument("blockhash-lookup-memory", CommandLineArgument.ArgumentType.Size, Strings.Options.BlockhashlookupsizeShort, Strings.Options.BlockhashlookupsizeLong, "0"),
                    new CommandLineArgument("filehash-lookup-memory", CommandLineArgument.ArgumentType.Size, Strings.Options.FilehashlookupsizeShort, Strings.Options.FilehashlookupsizeLong, "0"),
                    new CommandLineArgument("metadatahash-lookup-memory", CommandLineArgument.ArgumentType.Size, Strings.Options.MetadatahashlookupsizeShort, Strings.Options.MetadatahashlookupsizeLong, "0"),
                    new CommandLineArgument("old-lookup-memory-defaults", CommandLineArgument.ArgumentType.Size, Strings.Options.OldmemorylookupdefaultsShort, Strings.Options.OldmemorylookupdefaultsLong, "false"),
                    new CommandLineArgument("disable-filepath-cache", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisablefilepathcacheShort, Strings.Options.DisablefilepathcacheLong, "true"),
                    new CommandLineArgument("changed-files", CommandLineArgument.ArgumentType.Path, Strings.Options.ChangedfilesShort, Strings.Options.ChangedfilesLong),
                    new CommandLineArgument("deleted-files", CommandLineArgument.ArgumentType.Path, Strings.Options.DeletedfilesShort, Strings.Options.DeletedfilesLong("changed-files")),

                    new CommandLineArgument("threshold", CommandLineArgument.ArgumentType.Size, Strings.Options.ThresholdShort, Strings.Options.ThresholdLong, DEFAULT_THRESHOLD.ToString()),
                    new CommandLineArgument("index-file-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.IndexfilepolicyShort, Strings.Options.IndexfilepolicyLong, IndexFileStrategy.Full.ToString(), null, Enum.GetNames(typeof(IndexFileStrategy))),
                    new CommandLineArgument("no-backend-verification", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NobackendverificationShort, Strings.Options.NobackendverificationLong, "false"),
                    new CommandLineArgument("backup-test-samples", CommandLineArgument.ArgumentType.Integer, Strings.Options.BackendtestsamplesShort, Strings.Options.BackendtestsamplesLong("no-backend-verification"), "1"),
                    new CommandLineArgument("full-remote-verification", CommandLineArgument.ArgumentType.Boolean, Strings.Options.FullremoteverificationShort, Strings.Options.FullremoteverificationLong("no-backend-verification"), "false"),
                    new CommandLineArgument("dry-run", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DryrunShort, Strings.Options.DryrunLong, "false", new string[] { "dryrun" }),

                    new CommandLineArgument("block-hash-algorithm", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.BlockhashalgorithmShort, Strings.Options.BlockhashalgorithmLong, DEFAULT_BLOCK_HASH_ALGORITHM, null, GetSupportedHashes()),
                    new CommandLineArgument("file-hash-algorithm", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.FilehashalgorithmShort, Strings.Options.FilehashalgorithmLong, DEFAULT_FILE_HASH_ALGORITHM, null, GetSupportedHashes()),

                    new CommandLineArgument("no-auto-compact", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NoautocompactShort, Strings.Options.NoautocompactLong, "false"),
                    new CommandLineArgument("small-file-size", CommandLineArgument.ArgumentType.Size, Strings.Options.SmallfilesizeShort, Strings.Options.SmallfilesizeLong),
                    new CommandLineArgument("small-file-max-count", CommandLineArgument.ArgumentType.Size, Strings.Options.SmallfilemaxcountShort, Strings.Options.SmallfilemaxcountLong, DEFAULT_SMALL_FILE_MAX_COUNT.ToString()),

                    new CommandLineArgument("patch-with-local-blocks", CommandLineArgument.ArgumentType.Boolean, Strings.Options.PatchwithlocalblocksShort, Strings.Options.PatchwithlocalblocksLong, "false"),
                    new CommandLineArgument("no-local-db", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NolocaldbShort, Strings.Options.NolocaldbLong, "false"),
                    
                    new CommandLineArgument("keep-versions", CommandLineArgument.ArgumentType.Integer, Strings.Options.KeepversionsShort, Strings.Options.KeepversionsLong, DEFAULT_KEEP_VERSIONS.ToString()),
                    new CommandLineArgument("keep-time", CommandLineArgument.ArgumentType.Timespan, Strings.Options.KeeptimeShort, Strings.Options.KeeptimeLong),
                    new CommandLineArgument("upload-verification-file", CommandLineArgument.ArgumentType.Boolean, Strings.Options.UploadverificationfileShort, Strings.Options.UploadverificationfileLong, "false"),
                    new CommandLineArgument("allow-passphrase-change", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllowpassphrasechangeShort, Strings.Options.AllowpassphrasechangeLong, "false"),
                    new CommandLineArgument("no-local-blocks", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NolocalblocksShort, Strings.Options.NolocalblocksLong, "false"),
                    new CommandLineArgument("full-block-verification", CommandLineArgument.ArgumentType.Boolean, Strings.Options.FullblockverificationShort, Strings.Options.FullblockverificationLong, "false"),

                    new CommandLineArgument("log-retention", CommandLineArgument.ArgumentType.Timespan, Strings.Options.LogretentionShort, Strings.Options.LogretentionLong, DEFAULT_LOG_RETENTION),

                    new CommandLineArgument("repair-only-paths", CommandLineArgument.ArgumentType.Boolean, Strings.Options.RepaironlypathsShort, Strings.Options.RepaironlypathsLong, "false"),
                    new CommandLineArgument("force-locale", CommandLineArgument.ArgumentType.String, Strings.Options.ForcelocaleShort, Strings.Options.ForcelocaleLong),

                    new CommandLineArgument("enable-piped-downstreams", CommandLineArgument.ArgumentType.Boolean, Strings.Options.EnablepipingShort, Strings.Options.EnablepipingLong, "false"),
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
                    return System.IO.Path.GetTempPath();
                else
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
        /// Gets the filesets selected for deletion
        /// </summary>
        /// <returns>The filesets to delete</returns>
        /// <param name="backups">The list of backups that can be deleted</param>
        public DateTime[] GetFilesetsToDelete (DateTime[] backups)
        {
            if (backups.Length == 0)
                return backups;
                
            List<DateTime> res = new List<DateTime>();
                
            var versions = this.Version;
            if (versions != null && versions.Length > 0) 
                foreach (var ix in versions.Distinct())
                	if (ix >= 0 && ix < backups.Length)
                    	res.Add(backups[ix]);
            
            var keepVersions = this.KeepVersions;
            if (keepVersions > 0 && keepVersions < backups.Length)
                res.AddRange(backups.Skip(keepVersions));
                    
            var keepTime = this.KeepTime;
            if (keepTime.Ticks > 0)
                res.AddRange(backups.SkipWhile(x => x >= keepTime));
            
            var filtered = res.Distinct().OrderByDescending(x => x).AsEnumerable();
            
            var removeCount = filtered.Count();
            if (removeCount >= backups.Length)
                filtered = filtered.Skip(removeCount - backups.Length + (AllowFullRemoval ? 0 : 1));
            
            return filtered.ToArray();
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
        /// Helper method to set the default encryption mode based on the settings of the previous backup
        /// </summary>
        /// <param name="lastSetting">The encryption module used for the last entry</param>
        public void SetEncryptionModuleDefault(string lastSetting)
        {
            //If the encryption module was specified explicitly, don't change it
            if (m_options.ContainsKey("no-encryption") || m_options.ContainsKey("encryption-module"))
                return;

            if (string.IsNullOrEmpty(lastSetting))
                m_options["no-encryption"] = "";
            else
                m_options["encryption-module"] = lastSetting;
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
        /// Helper method to set the default compression mode based on the settings of the previous backup
        /// </summary>
        /// <param name="lastSetting">The compression module used for the last entry</param>
        public void SetCompressionModuleDefault(string lastSetting)
        {
            //If a compression module is explicitly selected, don't change it
            if (m_options.ContainsKey("compression-module"))
                return;

            m_options["compression-module"] = lastSetting;
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
                        throw new Exception("Invalid count for number-of-retries");

                    return x;
                }
            }
        }

        /// <summary>
        /// A value indicating if backups are transmitted on a separate thread
        /// </summary>
        public bool SynchronousUpload { get { return Library.Utility.Utility.ParseBoolOption(m_options, "synchronous-upload"); } }

        /// <summary>
        /// A value indicating if system is allowed to enter sleep power states during backup/restore ops (win32 only)
        /// </summary>
        public bool AllowSleep { get { return GetBool("allow-sleep"); } }

        /// <summary>
        /// A value indicating if use of the streaming interface is disallowed
        /// </summary>
        public bool DisableStreamingTransfers { get { return GetBool("disable-streaming-transfers"); } }

        /// <summary>
        /// A value indicating if multithreaded pipes may be used for decryption on downloads
        /// </summary>
        public bool EnablePipedDownstreams { get { return GetBool("enable-piped-downstreams"); } }

        /// <summary>
        /// Gets the timelimit for removal
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
                    return m_options["enable-module"].Trim().ToLower().Split(',');
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
                    return m_options["disable-module"].Trim().ToLower().Split(',');
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

                if (string.Equals(strategy, "on", StringComparison.InvariantCultureIgnoreCase))
                    return OptimizationStrategy.On;
                else if (string.Equals(strategy, "off", StringComparison.InvariantCultureIgnoreCase))
                    return OptimizationStrategy.Off;
                else if (string.Equals(strategy, "required", StringComparison.InvariantCultureIgnoreCase))
                    return OptimizationStrategy.Required;
                else if (string.Equals(strategy, "auto", StringComparison.InvariantCultureIgnoreCase))
                    return OptimizationStrategy.Auto;
                else
                    return OptimizationStrategy.Off;
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
        /// Gets the snapshot strategy to use
        /// </summary>
        public OptimizationStrategy UsnStrategy
        {
            get
            {
                string strategy;
                if (!m_options.TryGetValue("usn-policy", out strategy))
                    strategy = "";

                if (string.Equals(strategy, "on", StringComparison.InvariantCultureIgnoreCase))
                    return OptimizationStrategy.On;
                else if (string.Equals(strategy, "off", StringComparison.InvariantCultureIgnoreCase))
                    return OptimizationStrategy.Off;
                else if (string.Equals(strategy, "required", StringComparison.InvariantCultureIgnoreCase))
                    return OptimizationStrategy.Required;
                else if (string.Equals(strategy, "auto", StringComparison.InvariantCultureIgnoreCase))
                    return OptimizationStrategy.Auto;
                else
                    return OptimizationStrategy.Off;
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
        /// Gets the temporary folder to use for asyncronous transfers
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
        /// Gets a value indicating if the log level has been set
        /// </summary>
        public bool HasLoglevel
        {
            get
            {
                string value;
                if (!m_options.TryGetValue("log-level", out value))
                    value = null;

                foreach (string s in Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType)))
                    if (s.Equals(value, StringComparison.InvariantCultureIgnoreCase))
                        return true;

                return false;
            }
        }

        /// <summary>
        /// Gets the log detail level
        /// </summary>
        public Duplicati.Library.Logging.LogMessageType Loglevel
        {
            get
            {
                string value;
                if (!m_options.TryGetValue("log-level", out value))
                    value = null;

                foreach (string s in Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType)))
                    if (s.Equals(value, StringComparison.InvariantCultureIgnoreCase))
                        return (Duplicati.Library.Logging.LogMessageType)Enum.Parse(typeof(Duplicati.Library.Logging.LogMessageType), s);

                return Duplicati.Library.Logging.LogMessageType.Warning;
            }
        }

        /// <summary>
        /// Gets the attribute filter used to exclude files and folders.
        /// </summary>
        public System.IO.FileAttributes FileAttributeFilter
        {
            get
            {
                System.IO.FileAttributes res = (System.IO.FileAttributes)0;
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
        /// A value indicating if the output should be verbose
        /// </summary>
        public bool Verbose { get { return GetBool("verbose"); } }
        
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
        /// Gets the size of file-blocks
        /// </summary>
        public int Blocksize
        {
            get
            {
                string tmp;
                if (!m_options.TryGetValue("blocksize", out tmp))
                    tmp = DEFAULT_BLOCKSIZE;

                long t = Library.Utility.Sizeparser.ParseSize(tmp, "kb");
                if (t > int.MaxValue || t < 1024)
                    throw new ArgumentOutOfRangeException("blocksize", string.Format("The blocksize cannot be less than {0}, nor larger than {1}", 1024, int.MaxValue));
                
                return (int)t;
            }
        }

        /// <summary>
        /// Gets the size of the blockhash.
        /// </summary>
        /// <value>The size of the blockhash.</value>
        public int BlockhashSize
        {
            get
            {
                return System.Security.Cryptography.HashAlgorithm.Create(BlockHashAlgorithm).HashSize / 8;
            }
        }

        /// <summary>
        /// Gets the size the read-ahead buffer
        /// </summary>
        public long FileReadBufferSize
        {
            get
            {
                string tmp;
                if (!m_options.TryGetValue("file-read-buffer-size", out tmp))
                    tmp = DEFAULT_READ_BUFFER_SIZE;

                long t = Library.Utility.Sizeparser.ParseSize(tmp, "mb");                
                return (int)t;
            }
        }
        
        /// <summary>
        /// Gets a flag indicating if metadata for files and folders should be ignored
        /// </summary>
        public bool StoreMetadata
        {
            get 
            { 
                if (m_options.ContainsKey("skip-metadata"))
                    return !Library.Utility.Utility.ParseBoolOption(m_options, "skip-metadata");

                if (m_options.ContainsKey("store-metadata"))
                    return Library.Utility.Utility.ParseBoolOption(m_options, "store-metadata"); 

                return true;
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
        /// Gets a flag indicating whether this <see cref="Duplicati.Library.Main.Options"/> old memory defaults.
        /// </summary>
        public bool OldMemoryDefaults
        {
            get { return Library.Utility.Utility.ParseBoolOption(m_options, "old-lookup-memory-defaults"); }
        }

        /// <summary>
        /// Gets the block hash lookup size
        /// </summary>
        public long BlockHashLookupMemory
        {
            get
            {
                string v;
                m_options.TryGetValue("blockhash-lookup-memory", out v);
                if (string.IsNullOrEmpty(v))
                    v = OldMemoryDefaults ? DEFAULT_BLOCK_HASH_LOOKUP_SIZE : "0";

                return Library.Utility.Sizeparser.ParseSize(v, "mb");
            }
        }

        /// <summary>
        /// Gets the file hash size
        /// </summary>
        public long FileHashLookupMemory
        {
            get
            {
                string v;
                m_options.TryGetValue("filehash-lookup-memory", out v);
                if (string.IsNullOrEmpty(v))
                    v = OldMemoryDefaults ? DEFAULT_FILE_HASH_LOOKUP_SIZE : "0";

                return Library.Utility.Sizeparser.ParseSize(v, "mb");
            }
        }

        /// <summary>
        /// Gets the block hash size
        /// </summary>
        public long MetadataHashMemory
        {
            get
            {
                string v;
                m_options.TryGetValue("metadatahash-lookup-memory", out v);
                if (string.IsNullOrEmpty(v))
                    v = OldMemoryDefaults ? DEFAULT_METADATA_HASH_LOOKUP_SIZE : "0";
                
                return Library.Utility.Sizeparser.ParseSize(v, "mb");
            }
        }
        
        /// <summary>
        /// Gets the file hash size
        /// </summary>
        public bool UseFilepathCache
        {
            get
            {

                if (OldMemoryDefaults)
                    return !Library.Utility.Utility.ParseBoolOption(m_options, "disable-filepath-cache");
                else
                {
                    string s;
                    m_options.TryGetValue("disable-filepath-cache", out s);
                    return !Library.Utility.Utility.ParseBool(s, true);
                }
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
        /// Gets a flag indicating if the current operation is intended to delete files older than a certain threshold
        /// </summary>
        public bool HasDeleteOlderThan
        {
            get { return m_options.ContainsKey("delete-older-than"); }
        }

        /// <summary>
        /// Gets a flag indicating if the current operation is intended to delete files older than a certain threshold
        /// </summary>
        public bool HasDeleteAllButN
        {
            get { return m_options.ContainsKey("delete-all-but-n") || m_options.ContainsKey("delete-all-but-n-full"); }
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
            get { return m_options.ContainsKey("patch-with-local-blocks"); }
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
        /// Gets a lookup table with compression hints, the key is the file extension with the leading period
        /// </summary>
        public IDictionary<string, CompressionHint> CompressionHints
        {
            get
            {
                if (m_compressionHints == null)
                {
                    //Don't try again, if the file does not exist
                    m_compressionHints = new Dictionary<string, CompressionHint>(Library.Utility.Utility.ClientFilenameStringComparer);

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
                                m_compressionHints[line] = CompressionHint.Noncompressible;
                        }
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

    }
}
