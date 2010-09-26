#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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
        /// An enumeration that describes the supported snapshot modes
        /// </summary>
        public enum SnapShotMode
        {
            /// <summary>
            /// A snapshot is created if possible, but silently ignored if it fails to start
            /// </summary>
            Auto,
            /// <summary>
            /// A snapshot is created if possible, but an error is logged if it fails to start
            /// </summary>
            On,
            /// <summary>
            /// Snapshots are deactivated
            /// </summary>
            Off,
            /// <summary>
            /// A snapshot is created, and the backup is aborted if it fails
            /// </summary>
            Required
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
                    "filter",
                    "main-action"
                };
            }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("full", CommandLineArgument.ArgumentType.Boolean, Strings.Options.FullShort, Strings.Options.FullLong),
                    new CommandLineArgument("volsize", CommandLineArgument.ArgumentType.Size, Strings.Options.VolsizeShort, Strings.Options.VolsizeLong, "5mb"),
                    new CommandLineArgument("totalsize", CommandLineArgument.ArgumentType.Size, Strings.Options.TotalsizeShort, Strings.Options.TotalsizeLong),
                    new CommandLineArgument("auto-cleanup", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AutocleanupShort, Strings.Options.AutocleanupLong),
                    new CommandLineArgument("full-if-older-than", CommandLineArgument.ArgumentType.Timespan, Strings.Options.FullifolderthanShort, Strings.Options.FullifolderthanLong),
                    new CommandLineArgument("allow-full-removal", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllowfullremoveShort, Strings.Options.AllowfullremoveLong),

                    new CommandLineArgument("signature-control-files", CommandLineArgument.ArgumentType.Path, Strings.Options.SignaturecontrolfilesShort, Strings.Options.SignaturecontrolfilesLong),
                    new CommandLineArgument("signature-cache-path", CommandLineArgument.ArgumentType.Path, Strings.Options.SignaturecachepathShort, Strings.Options.SignaturecachepathLong),
                    new CommandLineArgument("skip-file-hash-checks", CommandLineArgument.ArgumentType.Boolean, Strings.Options.SkipfilehashchecksShort, Strings.Options.SkipfilehashchecksLong),
                    new CommandLineArgument("dont-read-manifests", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DontreadmanifestsShort, Strings.Options.DontreadmanifestsLong),
                    new CommandLineArgument("file-to-restore", CommandLineArgument.ArgumentType.String, Strings.Options.FiletorestoreShort, Strings.Options.FiletorestoreLong),
                    new CommandLineArgument("restore-time", CommandLineArgument.ArgumentType.String, Strings.Options.RestoretimeShort, Strings.Options.RestoretimeLong, "now"),

                    new CommandLineArgument("disable-filetime-check", CommandLineArgument.ArgumentType.String, Strings.Options.DisablefiletimecheckShort, Strings.Options.DisablefiletimecheckLong),
                    new CommandLineArgument("force", CommandLineArgument.ArgumentType.String, Strings.Options.ForceShort, Strings.Options.ForceLong),
                    new CommandLineArgument("tempdir", CommandLineArgument.ArgumentType.Path, Strings.Options.TempdirShort, Strings.Options.TempdirLong),
                    new CommandLineArgument("thread-priority", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.ThreadpriorityShort, Strings.Options.ThreadpriorityLong, "normal", null, new string[] {"high", "abovenormal", "normal", "belownormal", "low", "idle" }),

                    new CommandLineArgument("backup-prefix", CommandLineArgument.ArgumentType.String, Strings.Options.BackupprefixShort, Strings.Options.BackupprefixLong, "duplicati"),

                    new CommandLineArgument("time-separator", CommandLineArgument.ArgumentType.String, Strings.Options.TimeseparatorShort, Strings.Options.TimeseparatorLong, ":", new string[] {"time-seperator"}, null, Strings.Options.TimeseparatorDeprecated),
                    new CommandLineArgument("short-filenames", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ShortfilenamesShort, Strings.Options.ShortfilenamesLong, "false", null, null, Strings.Options.ShortfilenamesDeprecated),
                    new CommandLineArgument("old-filenames", CommandLineArgument.ArgumentType.Boolean, Strings.Options.OldfilenamesShort, Strings.Options.OldfilenamesLong, "false", null, null, Strings.Options.OldfilenamesDeprecated),

                    new CommandLineArgument("include", CommandLineArgument.ArgumentType.String, Strings.Options.IncludeShort, Strings.Options.IncludeLong),
                    new CommandLineArgument("exclude", CommandLineArgument.ArgumentType.String, Strings.Options.ExcludeShort, Strings.Options.ExcludeLong),
                    new CommandLineArgument("include-regexp", CommandLineArgument.ArgumentType.String, Strings.Options.IncluderegexpShort, Strings.Options.IncluderegexpLong),
                    new CommandLineArgument("exclude-regexp", CommandLineArgument.ArgumentType.String, Strings.Options.ExcluderegexpShort, Strings.Options.ExcluderegexpLong),

                    new CommandLineArgument("passphrase", CommandLineArgument.ArgumentType.String, Strings.Options.PassphraseShort, Strings.Options.PassphraseLong),
                    new CommandLineArgument("gpg-encryption", CommandLineArgument.ArgumentType.Boolean, Strings.Options.GpgencryptionShort, Strings.Options.GpgencryptionLong, "false", null, null, Strings.Options.GpgencryptionDeprecated),
                    new CommandLineArgument("no-encryption", CommandLineArgument.ArgumentType.Boolean, Strings.Options.NoencryptionShort, Strings.Options.NoencryptionLong, "false"),

                    new CommandLineArgument("number-of-retries", CommandLineArgument.ArgumentType.Integer, Strings.Options.NumberofretriesShort, Strings.Options.NumberofretriesLong, "5"),
                    new CommandLineArgument("retry-delay", CommandLineArgument.ArgumentType.Timespan, Strings.Options.RetrydelayShort, Strings.Options.RetrydelayLong, "10s"),
                    
                    new CommandLineArgument("asynchronous-upload", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AsynchronousuploadShort, Strings.Options.AsynchronousuploadLong, "false"),
                    new CommandLineArgument("asynchronous-upload-limit", CommandLineArgument.ArgumentType.Integer, Strings.Options.AsynchronousuploadlimitShort, Strings.Options.AsynchronousuploadlimitLong, "2"),
                    new CommandLineArgument("asynchronous-upload-folder", CommandLineArgument.ArgumentType.Path, Strings.Options.AsynchronousuploadfolderShort, Strings.Options.AsynchronousuploadfolderLong),

                    new CommandLineArgument("disable-streaming-transfers", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DisableStreamingShort, Strings.Options.DisableStreamingLong, "false"),

                    new CommandLineArgument("max-upload-pr-second", CommandLineArgument.ArgumentType.Size, Strings.Options.MaxuploadprsecondShort, Strings.Options.MaxuploadprsecondLong),
                    new CommandLineArgument("max-download-pr-second", CommandLineArgument.ArgumentType.Size, Strings.Options.MaxdownloadprsecondShort, Strings.Options.MaxdownloadprsecondLong),
                    new CommandLineArgument("skip-files-larger-than", CommandLineArgument.ArgumentType.Size, Strings.Options.SkipfileslargerthanShort, Strings.Options.SkipfileslargerthanLong),
                    
                    new CommandLineArgument("allow-sourcefolder-change", CommandLineArgument.ArgumentType.Boolean, Strings.Options.AllowsourcefolderchangeShort, Strings.Options.AllowsourcefolderchangeLong, "false"),
                    new CommandLineArgument("full-if-sourcefolder-changed", CommandLineArgument.ArgumentType.Boolean, Strings.Options.FullifsourcefolderchangedShort, Strings.Options.FullifsourcefolderchangedLong, "false"),

                    new CommandLineArgument("snapshot-policy", CommandLineArgument.ArgumentType.Enumeration, Strings.Options.SnapshotpolicyShort, Strings.Options.SnapshotpolicyLong, "off", null, new string[] {"auto", "off", "on", "required"}),

                    new CommandLineArgument("encryption-module", CommandLineArgument.ArgumentType.String, Strings.Options.EncryptionmoduleShort, Strings.Options.EncryptionmoduleLong, "aes"),
                    new CommandLineArgument("compression-module", CommandLineArgument.ArgumentType.String, Strings.Options.CompressionmoduleShort, Strings.Options.CompressionmoduleLong, "zip"),

                    new CommandLineArgument("enable-module", CommandLineArgument.ArgumentType.String, Strings.Options.EnablemoduleShort, Strings.Options.EnablemoduleLong),
                    new CommandLineArgument("disable-module", CommandLineArgument.ArgumentType.String, Strings.Options.DisablemoduleShort, Strings.Options.DisablemoduleLong),

                    new CommandLineArgument("debug-output", CommandLineArgument.ArgumentType.Boolean, Strings.Options.DebugoutputShort, Strings.Options.DebugoutputLong, "false"),
                    new CommandLineArgument("exclude-empty-folders", CommandLineArgument.ArgumentType.Boolean, Strings.Options.ExcludeemptyfoldersShort, Strings.Options.ExcludeemptyfoldersLong, "false"),
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
                string volsize = "5mb";
                if (m_options.ContainsKey("volsize"))
                    volsize = m_options["volsize"];

#if DEBUG
                return Math.Max(1024 * 10, Core.Sizeparser.ParseSize(volsize, "mb"));
#else
                return Math.Max(1024 * 1024, Core.Sizeparser.ParseSize(volsize, "mb"));
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
                    return Math.Max(VolumeSize, Core.Sizeparser.ParseSize(m_options["totalsize"], "mb"));
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
                    return Core.Sizeparser.ParseSize(m_options["skip-files-larger-than"], "mb");
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
                return Core.Timeparser.ParseTimeInterval(m_options["full-if-older-than"], offsettime);
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
        /// Gets a list of files to add to the signature volumes
        /// </summary>
        public string SignatureCachePath
        {
            get
            {
                if (!m_options.ContainsKey("signature-cache-path") || string.IsNullOrEmpty(m_options["signature-cache-path"]))
                    return null;
                else
                    return m_options["signature-cache-path"];
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
                    return Core.Timeparser.ParseTimeInterval(m_options["restore-time"], DateTime.Now);
            }
        }

        /// <summary>
        /// A value indicating if file time checks are skipped
        /// </summary>
        public bool DisableFiletimeCheck { get { return GetBool("disable-filetime-check"); } }

        /// <summary>
        /// A value indicating if file deletes are forced
        /// </summary>
        public bool Force { get { return GetBool("force"); } }

        /// <summary>
        /// Gets the folder where temporary files are stored
        /// </summary>
        public string TempDir
        {
            get
            {
                if (!m_options.ContainsKey("tempdir") || string.IsNullOrEmpty(m_options["tempdir"]))
                    return null;
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
        public Core.FilenameFilter Filter
        {
            get
            {
                if (m_options.ContainsKey("filter") && !string.IsNullOrEmpty(m_options["filter"]))
                    return new Duplicati.Library.Core.FilenameFilter(Core.FilenameFilter.DecodeFilter(m_options["filter"]));
                else
                    return new Duplicati.Library.Core.FilenameFilter(new List<KeyValuePair<bool, string>>());
            }
        }

        /// <summary>
        /// Returns a value indiciating if a filter is specified
        /// </summary>
        public bool HasFilter { get { return m_options.ContainsKey("filter"); } }

        /// <summary>
        /// Gets the number of old backups to keep
        /// </summary>
        public int DeleteAllButNFull
        {
            get
            {
                if (!m_options.ContainsKey("delete-all-but-n-full") || string.IsNullOrEmpty(m_options["delete-all-but-n-full"]))
                    throw new Exception("No count given for \"Delete All But N Full\"");

                int x = int.Parse(m_options["delete-all-but-n-full"]);
                if (x < 0)
                    throw new Exception("Invalid count for delete-all-but-n-full, must be greater than zero");

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

                return Core.Timeparser.ParseTimeInterval(m_options["delete-older-than"], DateTime.Now, true);
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
        public bool AsynchronousUpload { get { return GetBool("asynchronous-upload"); } }

        /// <summary>
        /// A value indicating if use of the streaming interface is disallowed
        /// </summary>
        public bool DisableStreamingTransfers { get { return GetBool("disable-streaming-transfers"); } }


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
                    return Core.Timeparser.ParseTimeSpan(m_options["retry-delay"]);
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
                        return Core.Sizeparser.ParseSize(m_options["max-upload-pr-second"], "kb");
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
                        return Core.Sizeparser.ParseSize(m_options["max-download-pr-second"], "kb");
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
        public SnapShotMode SnapShotStrategy
        {
            get
            {
                string strategy;
                if (!m_options.TryGetValue("snapshot-policy", out strategy))
                    strategy = "";

                if (string.Equals(strategy, "on", StringComparison.InvariantCultureIgnoreCase))
                    return SnapShotMode.On;
                else if (string.Equals(strategy, "off", StringComparison.InvariantCultureIgnoreCase))
                    return SnapShotMode.Off;
                else if (string.Equals(strategy, "required", StringComparison.InvariantCultureIgnoreCase))
                    return SnapShotMode.Required;
                else
                    return SnapShotMode.Off;
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
                    return this.TempDir ?? Core.TempFolder.SystemTempPath;
                else
                    return value;
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
            string value;
            
            if (m_options.TryGetValue(name, out value))
                return Core.Utility.ParseBool(value, true);
            else
                return false;
        }

    }
}
