#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
        /// The possible settings for the open file strategy
        /// </summary>
        public enum OpenFileStrategy
        {
            /// <summary>
            /// Means that locked files are not backed up
            /// </summary>
            Ignore,

            /// <summary>
            /// Reads locked files as-is
            /// </summary>
            Snapshot,

            /// <summary>
            /// Copies the file to prevent partial writes
            /// </summary>
            Copy
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
        /// Lock that protects the options collection
        /// </summary>
        private object m_lock = new object();

        private Dictionary<string, string> m_options;

        private List<KeyValuePair<bool, Library.Interface.IGenericModule>> m_loadedModules = new List<KeyValuePair<bool, IGenericModule>>();

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
                    "restore",
                    "delete-older-than",
                    "delete-all-but-n-full",
                    "delete-all-but-n",
                    "filter",
                    "main-action"
                };
            }
        }

        /// <summary>
        /// Returns a list of options that are intentionally duplicate
        /// </summary>
        public static string[] KnownDuplicates
        {
            get { return new string[] { "ftp-password", "ftp-username" }; }
        }


        /// <summary>
        /// Gets all commands that effect a backup
        /// </summary>
        public static string[] BackupOptions
        {
            get
            {
                return new string[] {
                    "full",
                    "full-if-older-than",
                    "full-if-more-than-n-incrementals",
                    "volsize",
                    "totalsize",
                    "disable-autocreate-folder",
                    "disable-filetime-check",
                    "disable-time-tolerance",
                    "include",
                    "exclude",
                    "include-regexp",
                    "exclude-regexp",
                    "sorted-filelist",
                    "skip-files-larger-than",
                    "allow-sourcefolder-change",
                    "full-if-sourcefolder-changed",
                    "upload-unchanged-backups",
                    "list-verify-uploads",
                    "create-verification-file",
                    "signature-control-files",
                    "snapshot-policy",
                    "vss-exclude-writers",
                    "vss-use-mapping",
                    "usn-policy",
                    "open-file-policy",
                    "exclude-empty-folders",
                    "symlink-policy",
                    "exclude-files-attributes"
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
                    "asynchronous-upload",
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
                    "backup-prefix",
                    "time-separator",
                    "short-filenames",
                    "old-filenames",
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
                    "backend-log-database"
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
                    "gpg-encryption",
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
                    "gpg-encryption",
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
                    "allow-full-removal",
                    "force"
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
                    "file-to-restore",
                    "restore-time",
                    "best-effort-restore"
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
                return System.IO.Path.GetFileNameWithoutExtension(Utility.Utility.getEntryAssembly().Location);
            }
        }

        /// <summary>
        /// Gets all supported commands
        /// </summary>
        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("full", CommandLineArgument.ArgumentType.Boolean, Strings.Options.FullShort, Strings.Options.FullLong),
                    new CommandLineArgument("volsize", CommandLineArgument.ArgumentType.Size, Strings.Options.VolsizeShort, Strings.Options.VolsizeLong, "10mb"),
                    new CommandLineArgument("totalsize", CommandLineArgument.ArgumentType.Size, Strings.Options.TotalsizeShort, Strings.Options.TotalsizeLong),
                    new CommandLineArgument("auto-cleanup", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AutocleanupShort, Strings.Options.AutocleanupLong),
                    new CommandLineArgument("full-if-older-than", CommandLineArgument.ArgumentType.Timespan, Strings.Options.FullifolderthanShort, Strings.Options.FullifolderthanLong),
                    new CommandLineArgument("full-if-more-than-n-incrementals", CommandLineArgument.ArgumentType.Integer, Strings.Options.FullifmorethannincrementalsShort, Strings.Options.FullifmorethannincrementalsLong),
                    new CommandLineArgument("allow-full-removal", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllowfullremoveShort, Strings.Options.AllowfullremoveLong),

                    new CommandLineArgument("signature-control-files", CommandLineArgument.ArgumentType.Path, Strings.Options.SignaturecontrolfilesShort, Strings.Options.SignaturecontrolfilesLong),
                    new CommandLineArgument("signature-cache-path", CommandLineArgument.ArgumentType.Path, Strings.Options.SignaturecachepathShort, Strings.Options.SignaturecachepathLong),
                    new CommandLineArgument("skip-file-hash-checks", CommandLineArgument.ArgumentType.Boolean, Strings.Options.SkipfilehashchecksShort, Strings.Options.SkipfilehashchecksLong),
                    new CommandLineArgument("dont-read-manifests", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DontreadmanifestsShort, Strings.Options.DontreadmanifestsLong),
                    new CommandLineArgument("best-effort-restore", CommandLineArgument.ArgumentType.Boolean, Strings.Options.BesteffortrestoreShort, Strings.Options.BesteffortrestoreLong),
                    new CommandLineArgument("file-to-restore", CommandLineArgument.ArgumentType.String, Strings.Options.FiletorestoreShort, Strings.Options.FiletorestoreLong),
                    new CommandLineArgument("restore-time", CommandLineArgument.ArgumentType.String, Strings.Options.RestoretimeShort, Strings.Options.RestoretimeLong, "now"),
                    new CommandLineArgument("disable-autocreate-folder", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisableautocreatefolderShort, Strings.Options.DisableautocreatefolderLong, "false"),

                    new CommandLineArgument("disable-filetime-check", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisablefiletimecheckShort, Strings.Options.DisablefiletimecheckLong, "false"),
                    //new CommandLineArgument("disable-usn-diff-check", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisableusndiffcheckShort, Strings.Options.DisableusndiffcheckLong, "false"),
                    new CommandLineArgument("disable-time-tolerance", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisabletimetoleranceShort, Strings.Options.DisabletimetoleranceLong, "false"),

                    new CommandLineArgument("force", CommandLineArgument.ArgumentType.String, Strings.Options.ForceShort, Strings.Options.ForceLong),
                    new CommandLineArgument("tempdir", CommandLineArgument.ArgumentType.Path, Strings.Options.TempdirShort, Strings.Options.TempdirLong, System.IO.Path.GetTempPath()),
                    new CommandLineArgument("thread-priority", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.ThreadpriorityShort, Strings.Options.ThreadpriorityLong, "normal", null, new string[] {"highest", "high", "abovenormal", "normal", "belownormal", "low", "lowest", "idle" }),

                    new CommandLineArgument("backup-prefix", CommandLineArgument.ArgumentType.String, Strings.Options.BackupprefixShort, Strings.Options.BackupprefixLong, "duplicati"),

                    new CommandLineArgument("time-separator", CommandLineArgument.ArgumentType.String, Strings.Options.TimeseparatorShort, Strings.Options.TimeseparatorLong, ":", new string[] {"time-seperator"}, null, Strings.Options.TimeseparatorDeprecated),
                    new CommandLineArgument("short-filenames", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ShortfilenamesShort, Strings.Options.ShortfilenamesLong, "false", null, null, Strings.Options.ShortfilenamesDeprecated),
                    new CommandLineArgument("old-filenames", CommandLineArgument.ArgumentType.Boolean, Strings.Options.OldfilenamesShort, Strings.Options.OldfilenamesLong, "false", null, null, Strings.Options.OldfilenamesDeprecated),

                    new CommandLineArgument("include", CommandLineArgument.ArgumentType.String, Strings.Options.IncludeShort, Strings.Options.IncludeLong),
                    new CommandLineArgument("exclude", CommandLineArgument.ArgumentType.String, Strings.Options.ExcludeShort, Strings.Options.ExcludeLong),
                    new CommandLineArgument("include-regexp", CommandLineArgument.ArgumentType.String, Strings.Options.IncluderegexpShort, Strings.Options.IncluderegexpLong),
                    new CommandLineArgument("exclude-regexp", CommandLineArgument.ArgumentType.String, Strings.Options.ExcluderegexpShort, Strings.Options.ExcluderegexpLong),

                    new CommandLineArgument("passphrase", CommandLineArgument.ArgumentType.Password, Strings.Options.PassphraseShort, Strings.Options.PassphraseLong),
                    new CommandLineArgument("gpg-encryption", CommandLineArgument.ArgumentType.Boolean, Strings.Options.GpgencryptionShort, Strings.Options.GpgencryptionLong, "false", null, null, Strings.Options.GpgencryptionDeprecated),
                    new CommandLineArgument("no-encryption", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NoencryptionShort, Strings.Options.NoencryptionLong, "false"),

                    new CommandLineArgument("number-of-retries", CommandLineArgument.ArgumentType.Integer, Strings.Options.NumberofretriesShort, Strings.Options.NumberofretriesLong, "5"),
                    new CommandLineArgument("retry-delay", CommandLineArgument.ArgumentType.Timespan, Strings.Options.RetrydelayShort, Strings.Options.RetrydelayLong, "10s"),
                    new CommandLineArgument("sorted-filelist", CommandLineArgument.ArgumentType.Boolean, Strings.Options.SortedfilelistShort, Strings.Options.SortedfilelistLong, "false"),

                    new CommandLineArgument("synchronous-upload", CommandLineArgument.ArgumentType.Boolean, Strings.Options.SynchronousuploadShort, Strings.Options.SynchronousuploadLong, "false"),
                    new CommandLineArgument("asynchronous-upload", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AsynchronousuploadShort, Strings.Options.AsynchronousuploadLong, "false", null, null, string.Format(Strings.Options.AsynchronousuploadDeprecated, "synchronous-upload")),
                    new CommandLineArgument("asynchronous-upload-limit", CommandLineArgument.ArgumentType.Integer, Strings.Options.AsynchronousuploadlimitShort, Strings.Options.AsynchronousuploadlimitLong, "2"),
                    new CommandLineArgument("asynchronous-upload-folder", CommandLineArgument.ArgumentType.Path, Strings.Options.AsynchronousuploadfolderShort, Strings.Options.AsynchronousuploadfolderLong),

                    new CommandLineArgument("disable-streaming-transfers", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisableStreamingShort, Strings.Options.DisableStreamingLong, "false"),

                    new CommandLineArgument("max-upload-pr-second", CommandLineArgument.ArgumentType.Size, Strings.Options.MaxuploadprsecondShort, Strings.Options.MaxuploadprsecondLong),
                    new CommandLineArgument("max-download-pr-second", CommandLineArgument.ArgumentType.Size, Strings.Options.MaxdownloadprsecondShort, Strings.Options.MaxdownloadprsecondLong),
                    new CommandLineArgument("skip-files-larger-than", CommandLineArgument.ArgumentType.Size, Strings.Options.SkipfileslargerthanShort, Strings.Options.SkipfileslargerthanLong),
                    
                    new CommandLineArgument("allow-sourcefolder-change", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllowsourcefolderchangeShort, Strings.Options.AllowsourcefolderchangeLong, "false"),
                    new CommandLineArgument("full-if-sourcefolder-changed", CommandLineArgument.ArgumentType.Boolean, Strings.Options.FullifsourcefolderchangedShort, Strings.Options.FullifsourcefolderchangedLong, "false"),
                    new CommandLineArgument("upload-unchanged-backups", CommandLineArgument.ArgumentType.Boolean, Strings.Options.UploadUnchangedBackupsShort, Strings.Options.UploadUnchangedBackupsLong, "false"),

                    new CommandLineArgument("snapshot-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.SnapshotpolicyShort, Strings.Options.SnapshotpolicyLong, "off", null, new string[] {"auto", "off", "on", "required"}),
                    new CommandLineArgument("vss-exclude-writers", CommandLineArgument.ArgumentType.String, Strings.Options.VssexcludewritersShort, Strings.Options.VssexcludewritersLong),
                    new CommandLineArgument("vss-use-mapping", CommandLineArgument.ArgumentType.Boolean, Strings.Options.VssusemappingShort, Strings.Options.VssusemappingLong, "false"),
                    new CommandLineArgument("usn-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.UsnpolicyShort, Strings.Options.UsnpolicyLong, "off", null, new string[] {"auto", "off", "on", "required"}),
                    new CommandLineArgument("open-file-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.OpenfilepolicyShort, Strings.Options.OpenfilepolicyLong, "snapshot", null, new string[] { "ignore", "snapshot", "copy" }),

                    new CommandLineArgument("encryption-module", CommandLineArgument.ArgumentType.String, Strings.Options.EncryptionmoduleShort, Strings.Options.EncryptionmoduleLong, "aes"),
                    new CommandLineArgument("compression-module", CommandLineArgument.ArgumentType.String, Strings.Options.CompressionmoduleShort, Strings.Options.CompressionmoduleLong, "zip"),

                    new CommandLineArgument("enable-module", CommandLineArgument.ArgumentType.String, Strings.Options.EnablemoduleShort, Strings.Options.EnablemoduleLong),
                    new CommandLineArgument("disable-module", CommandLineArgument.ArgumentType.String, Strings.Options.DisablemoduleShort, Strings.Options.DisablemoduleLong),

                    new CommandLineArgument("debug-output", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DebugoutputShort, Strings.Options.DebugoutputLong, "false"),
                    new CommandLineArgument("debug-retry-errors", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DebugretryerrorsShort, Strings.Options.DebugretryerrorsLong, "false"),
                    new CommandLineArgument("exclude-empty-folders", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ExcludeemptyfoldersShort, Strings.Options.ExcludeemptyfoldersLong, "false"),

                    new CommandLineArgument("log-file", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Options.LogfileShort, Strings.Options.LogfileShort),
                    new CommandLineArgument("log-level", Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Enumeration, Strings.Options.LoglevelShort, Strings.Options.LoglevelLong, "Warning", null, Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType))),

                    new CommandLineArgument("verification-level", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.VerificationLevelShort, Strings.Options.VerificationLevelLong, "Manifest", null, Enum.GetNames(typeof(Duplicati.Library.Main.VerificationLevel))),
                    new CommandLineArgument("create-verification-file", CommandLineArgument.ArgumentType.Boolean, Strings.Options.CreateverificationfileShort, Strings.Options.CreateverificationfileLong, "false"),
                    new CommandLineArgument("list-verify-uploads", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ListverifyuploadsShort, Strings.Options.ListverifyuploadsShort, "false"),
                    new CommandLineArgument("allow-sleep", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllowsleepShort, Strings.Options.AllowsleepShort, "false"),
                    new CommandLineArgument("no-connection-reuse", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NoconnectionreuseShort, Strings.Options.NoconnectionreuseLong, "false"),
                    
                    new CommandLineArgument("backend-log-database", CommandLineArgument.ArgumentType.Path, Strings.Options.BackendlogdatabaseShort, Strings.Options.BackendlogdatabaseLong),

                    new CommandLineArgument("symlink-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.SymlinkpolicyShort, string.Format(Strings.Options.SymlinkpolicyLong, "store", "ignore", "follow"), "store", null, Enum.GetNames(typeof(SymlinkStrategy))),
                    new CommandLineArgument("exclude-files-attributes", CommandLineArgument.ArgumentType.String, Strings.Options.ExcludefilesattributesShort, string.Format(Strings.Options.ExcludefilesattributesLong, string.Join(", ", Enum.GetNames(typeof(System.IO.FileAttributes))))),
                });
            }
        }

        /// <summary>
        /// A value indicating if the backup is a full backup
        /// </summary>
        public bool Full { get { return GetBool("full"); } }

        /// <summary>
        /// Gets or sets the current main action of the instance
        /// </summary>
        public DuplicatiOperationMode MainAction 
        {
            get { return (DuplicatiOperationMode)Enum.Parse(typeof(DuplicatiOperationMode), m_options["main-action"]); }
            set { m_options["main-action"] = value.ToString(); }
        }

        /// <summary>
        /// Gets the size of each volume in bytes
        /// </summary>
        public long VolumeSize
        {
            get
            {
                string volsize = "10mb";
                if (m_options.ContainsKey("volsize"))
                    volsize = m_options["volsize"];

#if DEBUG
                return Math.Max(1024 * 10, Utility.Sizeparser.ParseSize(volsize, "mb"));
#else
                return Math.Max(1024 * 1024, Utility.Sizeparser.ParseSize(volsize, "mb"));
#endif
            }
        }

        /// <summary>
        /// Gets the total size in bytes allowed for a single backup run
        /// </summary>
        public long MaxSize
        {
            get
            {
                if (!m_options.ContainsKey("totalsize") || string.IsNullOrEmpty(m_options["totalsize"]))
                    return long.MaxValue;
                else
                    return Math.Max(VolumeSize, Utility.Sizeparser.ParseSize(m_options["totalsize"], "mb"));
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
                    return Utility.Sizeparser.ParseSize(m_options["skip-files-larger-than"], "mb");
            }
        }

        /// <summary>
        /// Gets the time at which a full backup should be performed
        /// </summary>
        /// <param name="offsettime">The time the last full backup was created</param>
        /// <returns>The time at which a full backup should be performed</returns>
        public DateTime FullIfOlderThan(DateTime offsettime)
        {
            if (!m_options.ContainsKey("full-if-older-than") || string.IsNullOrEmpty(m_options["full-if-older-than"]))
                return DateTime.Now.AddYears(1); //We assume that the check will occur in less than one year :)
            else
            {
                TimeSpan tolerance = 
                    this.DisableTimeTolerance ?
                    TimeSpan.FromSeconds(0) :
                    TimeSpan.FromSeconds(Math.Min(Utility.Timeparser.ParseTimeSpan(m_options["full-if-older-than"]).TotalSeconds / 100, 60.0 * 60.0));

                return Utility.Timeparser.ParseTimeInterval(m_options["full-if-older-than"], offsettime) - tolerance;
            }
        }

        /// <summary>
        /// Gets the string describing when a full backup should be performed
        /// </summary>
        public string FullIfOlderThanValue
        {
            get
            {
                string v;
                if (!m_options.TryGetValue("full-if-older-than", out v))
                    return null;
                else if (string.IsNullOrEmpty(v))
                    return null;
                return v;
            }
        }

        /// <summary>
        /// A value indicating how many incrementals are required to trigger a full backup
        /// </summary>
        public int FullIfMoreThanNIncrementals
        {
            get
            {
                string countdata;
                int count;
                if (!m_options.TryGetValue("full-if-more-than-n-incrementals", out countdata))
                    return 0;
                if (int.TryParse(countdata, out count))
                    return count;

                return 0;
            }
        }

        /// <summary>
        /// A value indicating if orphan files are deleted automatically
        /// </summary>
        public bool AutoCleanup { get { return GetBool("auto-cleanup"); } }

        /// <summary>
        /// Gets a list of files to add to the signature volumes
        /// </summary>
        public string SignatureControlFiles
        {
            get
            {
                if (!m_options.ContainsKey("signature-control-files") || string.IsNullOrEmpty(m_options["signature-control-files"]))
                    return null;
                else
                    return m_options["signature-control-files"];
            }
        }

        /// <summary>
        /// Gets the path to the folder where signatures are cached
        /// </summary>
        public string SignatureCachePath
        {
            get
            {
                string tmp;
                m_options.TryGetValue("signature-cache-path", out tmp);
                if (string.IsNullOrEmpty(tmp))
                    return null;
                else
                    return tmp;
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    if (m_options.ContainsKey("signature-cache-path"))
                        m_options.Remove("signature-cache-path");
                }
                else
                {
                    m_options["signature-cache-path"] = value; 
                }
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
        /// A value indicating if the restore continues, even if errors occur
        /// </summary>
        public bool BestEffortRestore { get { return GetBool("best-effort-restore"); } }

        /// <summary>
        /// A value indicating if the source folder is allowed to change
        /// </summary>
        public bool AllowSourceFolderChange { get { return GetBool("allow-sourcefolder-change"); } }

        /// <summary>
        /// A value indicating if the backup should be a full backup if the source folder has changed
        /// </summary>
        public bool FullIfSourceFolderChanged { get { return GetBool("full-if-sourcefolder-changed"); } }

        /// <summary>
        /// Gets a list of files to restore
        /// </summary>
        public string FileToRestore
        {
            get
            {
                if (!m_options.ContainsKey("file-to-restore") || string.IsNullOrEmpty(m_options["file-to-restore"]))
                    return null;
                else
                    return m_options["file-to-restore"];
            }
        }

        /// <summary>
        /// Gets the backup that should be restored
        /// </summary>
        public DateTime RestoreTime
        {
            get
            {
                if (!m_options.ContainsKey("restore-time") || string.IsNullOrEmpty(m_options["restore-time"]))
                    return DateTime.Now.AddYears(1); //We assume that the check will occur in less than one year :)
                else
                    return Utility.Timeparser.ParseTimeInterval(m_options["restore-time"], DateTime.Now);
            }
        }

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
        /// A value indicating if file deletes are forced
        /// </summary>
        public bool Force { get { return GetBool("force"); } }

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
        /// A value indicating if short filenames are used
        /// </summary>
        public bool UseShortFilenames { get { return GetBool("short-filenames"); } }

        /// <summary>
        /// A value indicating if old filenames are used
        /// </summary>
        public bool UseOldFilenames { get { return GetBool("old-filenames"); } }

        /// <summary>
        /// A value indicating if missing folders should be created automatically
        /// </summary>
        public bool AutocreateFolders { get { return !GetBool("disable-autocreate-folder"); } }

        /// <summary>
        /// Gets the backup prefix
        /// </summary>
        public string BackupPrefix
        {
            get
            {
                if (!m_options.ContainsKey("backup-prefix") || string.IsNullOrEmpty(m_options["backup-prefix"]))
                    return this.UseShortFilenames ? "dpl" : "duplicati";
                else
                    return m_options["backup-prefix"];
            }
        }

        /// <summary>
        /// Gets the character used to separate the time components of a file timestamp
        /// </summary>
        public string TimeSeparatorChar
        {
            get
            {
                string key;
                if (m_options.TryGetValue("time-separator", out key) && !string.IsNullOrEmpty(key))
                    return key;
                if (m_options.TryGetValue("time-seperator", out key) && !string.IsNullOrEmpty(key))
                    return key;

                return null;
            }
        }


        /// <summary>
        /// Gets the filter used to include or exclude files
        /// </summary>
        public Utility.FilenameFilter Filter
        {
            get
            {
                if (m_options.ContainsKey("filter") && !string.IsNullOrEmpty(m_options["filter"]))
                    return new Duplicati.Library.Utility.FilenameFilter(Utility.FilenameFilter.DecodeFilter(m_options["filter"]));
                else
                    return new Duplicati.Library.Utility.FilenameFilter(new List<KeyValuePair<bool, string>>());
            }
        }

        /// <summary>
        /// Returns a value indicating if a filter is specified
        /// </summary>
        public bool HasFilter { get { return m_options.ContainsKey("filter"); } }

        /// <summary>
        /// Gets the number of old backups to keep
        /// </summary>
        public int DeleteAllButNFull
        {
            get
            {
                string key = "delete-all-but-n-full";
                if (!m_options.ContainsKey(key) || string.IsNullOrEmpty(m_options[key]))
                {
                    key = "delete-all-but-n";
                    if (!m_options.ContainsKey(key) || string.IsNullOrEmpty(m_options[key]))
                        throw new Exception("No count given for \"Delete All But N (Full)\"");
                }

                int x = int.Parse(m_options[key]);
                if (x < 0)
                    throw new Exception("Invalid count for delete-all-but-n(-full), must be greater than zero");

                return x;
            }
        }

        /// <summary>
        /// Gets the timelimit for removal
        /// </summary>
        public DateTime RemoveOlderThan
        {
            get
            {
                if (!m_options.ContainsKey("delete-older-than"))
                    throw new Exception("No count given for \"Delete Older Than\"");

                TimeSpan tolerance =
                    this.DisableTimeTolerance ?
                    TimeSpan.FromSeconds(0) :
                    TimeSpan.FromSeconds(Math.Min(Utility.Timeparser.ParseTimeSpan(m_options["delete-older-than"]).TotalSeconds / 100, 60.0 * 60.0));

                return Utility.Timeparser.ParseTimeInterval(m_options["delete-older-than"], DateTime.Now, true) - tolerance;
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

                //Default, support the deprecated --gpg-encryption flag
                if (GPGEncryption)
                    return "gpg";
                else
                    return "aes";
            }
        }

        /// <summary>
        /// [DEPRECATED] A value indicating if GPG encryption is used
        /// </summary>
        private bool GPGEncryption 
        { 
            get { return GetBool("gpg-encryption"); }
            set { m_options["gpg-encryption"] = value.ToString(); }
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
        public bool AsynchronousUpload 
        { 
            get 
            {
                if (m_options.ContainsKey("synchronous-upload"))
                    return !GetBool("synchronous-upload");
                else if (m_options.ContainsKey("asynchronous-upload"))
                    return GetBool("asynchronous-upload");
                else
                    return true;
            } 
        }

        /// <summary>
        /// A value indicating if system is allowed to enter sleep power states during backup/restore ops (win32 only)
        /// </summary>
        public bool AllowSleep { get { return GetBool("allow-sleep"); } }

        /// <summary>
        /// A value indicating if use of the streaming interface is disallowed
        /// </summary>
        public bool DisableStreamingTransfers { get { return GetBool("disable-streaming-transfers"); } }

        /// <summary>
        /// A value indicating if files are processed in a sorted order, rather than random
        /// </summary>
        public bool SortedFilelist { get { return GetBool("sorted-filelist"); } }


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
                    return Utility.Timeparser.ParseTimeSpan(m_options["retry-delay"]);
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
                    if (!m_options.ContainsKey("max-upload-pr-second") || string.IsNullOrEmpty(m_options["max-upload-pr-second"]))
                        return 0;
                    else
                        return Utility.Sizeparser.ParseSize(m_options["max-upload-pr-second"], "kb");
            }
            set
            {
                lock (m_lock)
                    if (value <= 0)
                        m_options["max-upload-pr-second"] = "";
                    else
                        m_options["max-upload-pr-second"] = value.ToString() + "b";
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
                    if (!m_options.ContainsKey("max-download-pr-second") || string.IsNullOrEmpty(m_options["max-download-pr-second"]))
                        return 0;
                    else
                        return Utility.Sizeparser.ParseSize(m_options["max-download-pr-second"], "kb");
            }
            set
            {
                lock (m_lock)
                    if (value <= 0)
                        m_options["max-download-pr-second"] = "";
                    else
                        m_options["max-download-pr-second"] = value.ToString() + "b";
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
        /// A value indicating if retry debug output is enabled
        /// </summary>
        public bool VerboseRetryErrors { get { return GetBool("debug-retry-errors"); } }

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
        /// Gets the snapshot strategy to use
        /// </summary>
        public SymlinkStrategy SymlinkPolicy
        {
            get
            {
                string strategy;
                if (!m_options.TryGetValue("symlink-policy", out strategy))
                    strategy = "";

                SymlinkStrategy r;
                if (!EnumTryParse(strategy, true, out r))
                    r = SymlinkStrategy.Store;

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
        /// Gets the open file strategy setting
        /// </summary>
        public OpenFileStrategy OpenFilePolicy
        {
            get
            {
                string strategy;
                if (!m_options.TryGetValue("open-file-policy", out strategy))
                    strategy = "";

                if (string.Equals(strategy, "snapshot", StringComparison.InvariantCultureIgnoreCase))
                    return OpenFileStrategy.Snapshot;
                else if (string.Equals(strategy, "ignore", StringComparison.InvariantCultureIgnoreCase))
                    return OpenFileStrategy.Ignore;
                else if (string.Equals(strategy, "copy", StringComparison.InvariantCultureIgnoreCase))
                    return OpenFileStrategy.Copy;
                else
                    return OpenFileStrategy.Snapshot;
            }
        }

        /// <summary>
        /// A value indicating if empty folders are excluded from the backup
        /// </summary>
        public bool ExcludeEmptyFolders { get { return GetBool("exclude-empty-folders"); } }

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
                    return 2;
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
        /// Gets the verification detail level
        /// </summary>
        public Duplicati.Library.Main.VerificationLevel Verificationlevel
        {
            get
            {
                string value;
                if (!m_options.TryGetValue("verification-level", out value))
                    value = null;

                foreach (string s in Enum.GetNames(typeof(Duplicati.Library.Main.VerificationLevel)))
                    if (s.Equals(value, StringComparison.InvariantCultureIgnoreCase))
                        return (Duplicati.Library.Main.VerificationLevel)Enum.Parse(typeof(Duplicati.Library.Main.VerificationLevel), s);

                return Duplicati.Library.Main.VerificationLevel.Manifest;
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
                    if (EnumTryParse(s.Trim(), true, out f))
                        res |= f;
                }

                return res;
            }
        }

        /// <summary>
        /// Helper method that adds Enum.TryParse to .Net 2.0
        /// </summary>
        /// <typeparam name="TEnum">The enum type to process</typeparam>
        /// <param name="s">The string to look for</param>
        /// <param name="ignoreCase">True to ignore case, false otherwise</param>
        /// <param name="result">The parsed value</param>
        /// <returns>True if the string was parsed, false otherwise</returns>
        private bool EnumTryParse<TEnum>(string s, bool ignoreCase, out TEnum result)
            where TEnum : struct
        {
            string[] names = Enum.GetNames(typeof(TEnum));
            for(int i = 0; i < names.Length; i++)
                if (string.Equals(names[i], s, ignoreCase ? StringComparison.InvariantCultureIgnoreCase : StringComparison.InvariantCulture))
                {
                    result = (TEnum)(Enum.GetValues(typeof(TEnum)).GetValue(i));
                    return true;
                }

            result = default(TEnum);
            return false;
        }

        /// <summary>
        /// A value indicating if a verification file is placed on the server
        /// </summary>
        public bool CreateVerificationFile { get { return GetBool("create-verification-file"); } }

        /// <summary>
        /// A value indicating if server uploads are verified by listing the folder contents
        /// </summary>
        public bool ListVerifyUploads { get { return GetBool("list-verify-uploads"); } }

        /// <summary>
        /// A value indicating if connections cannot be re-used
        /// </summary>
        public bool NoConnectionReuse { get { return GetBool("no-connection-reuse"); } }

        /// <summary>
        /// The path to a log database file, or null
        /// </summary>
        public string Backendlogdatabase 
        { 
            get 
            { 
                string value;
                if (!m_options.TryGetValue("backend-log-database", out value))
                    value = null;
                
                return value;
            } 
        }
        
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
                    return Utility.Sizeparser.ParseSize(m_options["quota-size"], "mb");
            }
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
            return Utility.Utility.ParseBoolOption(m_options, name);
        }

    }
}
