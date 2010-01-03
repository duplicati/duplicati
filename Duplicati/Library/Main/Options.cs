#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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

namespace Duplicati.Library.Main
{
    public class Options
    {
        /// <summary>
        /// Lock that protects the options collection
        /// </summary>
        private object m_lock = new object();

        private Dictionary<string, string> m_options;

        public Options(Dictionary<string, string> options)
        {
            m_options = options;
        }

        public Dictionary<string, string> RawOptions { get { return m_options; } }

        public IList<Backend.ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<Backend.ICommandLineArgument>(new Backend.ICommandLineArgument[] {
                    new Backend.CommandLineArgument("full", Backend.CommandLineArgument.ArgumentType.Boolean, Strings.Options.FullShort, Strings.Options.FullLong),
                    new Backend.CommandLineArgument("volsize", Backend.CommandLineArgument.ArgumentType.Size, Strings.Options.VolsizeShort, Strings.Options.VolsizeLong, "5mb"),
                    new Backend.CommandLineArgument("totalsize", Backend.CommandLineArgument.ArgumentType.Size, Strings.Options.TotalsizeShort, Strings.Options.TotalsizeLong),
                    new Backend.CommandLineArgument("auto-cleanup", Backend.CommandLineArgument.ArgumentType.Boolean, Strings.Options.AutocleanupShort, Strings.Options.AutocleanupLong),
                    new Backend.CommandLineArgument("full-if-older-than", Backend.CommandLineArgument.ArgumentType.Timespan, Strings.Options.FullifolderthanShort, Strings.Options.FullifolderthanLong),

                    new Backend.CommandLineArgument("signature-control-files", Backend.CommandLineArgument.ArgumentType.Path, Strings.Options.SignaturecontrolfilesShort, Strings.Options.SignaturecontrolfilesLong),
                    new Backend.CommandLineArgument("signature-cache-path", Backend.CommandLineArgument.ArgumentType.Path, Strings.Options.SignaturecachepathShort, Strings.Options.SignaturecachepathLong),
                    new Backend.CommandLineArgument("skip-file-hash-checks", Backend.CommandLineArgument.ArgumentType.Boolean, Strings.Options.SkipfilehashchecksShort, Strings.Options.SkipfilehashchecksLong),
                    new Backend.CommandLineArgument("file-to-restore", Backend.CommandLineArgument.ArgumentType.String, Strings.Options.FiletorestoreShort, Strings.Options.FiletorestoreLong),
                    new Backend.CommandLineArgument("restore-time", Backend.CommandLineArgument.ArgumentType.String, Strings.Options.RestoretimeShort, Strings.Options.RestoretimeLong, "now"),

                    new Backend.CommandLineArgument("disable-filetime-check", Backend.CommandLineArgument.ArgumentType.String, Strings.Options.DisablefiletimecheckShort, Strings.Options.DisablefiletimecheckLong),
                    new Backend.CommandLineArgument("force", Backend.CommandLineArgument.ArgumentType.String, Strings.Options.ForceShort, Strings.Options.ForceLong),
                    new Backend.CommandLineArgument("tempdir", Backend.CommandLineArgument.ArgumentType.Path, Strings.Options.TempdirShort, Strings.Options.TempdirLong),
                    new Backend.CommandLineArgument("thread-priority", Backend.CommandLineArgument.ArgumentType.Enumeration, Strings.Options.ThreadpriorityShort, Strings.Options.ThreadpriorityLong, "normal", null, new string[] {"high", "abovenormal", "normal", "belownormal", "low", "idle" }),

                    new Backend.CommandLineArgument("backup-prefix", Backend.CommandLineArgument.ArgumentType.String, Strings.Options.BackupprefixShort, Strings.Options.BackupprefixLong, "duplicati"),
                    new Backend.CommandLineArgument("time-separator", Backend.CommandLineArgument.ArgumentType.String, Strings.Options.TimeseparatorShort, Strings.Options.TimeseparatorLong, ":", new string[] {"time-seperator"}),
                    new Backend.CommandLineArgument("short-filenames", Backend.CommandLineArgument.ArgumentType.Boolean, Strings.Options.ShortfilenamesShort, Strings.Options.ShortfilenamesLong),

                    new Backend.CommandLineArgument("include", Backend.CommandLineArgument.ArgumentType.String, Strings.Options.IncludeShort, Strings.Options.IncludeLong),
                    new Backend.CommandLineArgument("exclude", Backend.CommandLineArgument.ArgumentType.String, Strings.Options.ExcludeShort, Strings.Options.ExcludeLong),
                    new Backend.CommandLineArgument("include-regexp", Backend.CommandLineArgument.ArgumentType.String, Strings.Options.IncluderegexpShort, Strings.Options.IncluderegexpLong),
                    new Backend.CommandLineArgument("exclude-regexp", Backend.CommandLineArgument.ArgumentType.String, Strings.Options.ExcluderegexpShort, Strings.Options.ExcluderegexpLong),

                    new Backend.CommandLineArgument("passphrase", Backend.CommandLineArgument.ArgumentType.String, Strings.Options.PassphraseShort, Strings.Options.PassphraseLong),
                    new Backend.CommandLineArgument("gpg-encryption", Backend.CommandLineArgument.ArgumentType.Boolean, Strings.Options.GpgencryptionShort, Strings.Options.GpgencryptionLong),
                    new Backend.CommandLineArgument("gpg-program-path", Backend.CommandLineArgument.ArgumentType.Path, Strings.Options.GpgprogrampathShort, Strings.Options.GpgprogrampathLong, "gpg"),
                    new Backend.CommandLineArgument("sign-key", Backend.CommandLineArgument.ArgumentType.String, Strings.Options.SignkeyShort, Strings.Options.SignkeyLong),
                    new Backend.CommandLineArgument("no-encryption", Backend.CommandLineArgument.ArgumentType.Boolean, Strings.Options.NoencryptionShort, Strings.Options.NoencryptionLong),

                    new Backend.CommandLineArgument("number-of-retries", Backend.CommandLineArgument.ArgumentType.Integer, Strings.Options.NumberofretriesShort, Strings.Options.NumberofretriesLong, "5"),
                    new Backend.CommandLineArgument("retry-delay", Backend.CommandLineArgument.ArgumentType.Timespan, Strings.Options.RetrydelayShort, Strings.Options.RetrydelayLong, "10s"),
                    new Backend.CommandLineArgument("asynchronous-upload", Backend.CommandLineArgument.ArgumentType.Boolean, Strings.Options.AsynchronousuploadShort, Strings.Options.AsynchronousuploadLong, "false"),

                    new Backend.CommandLineArgument("max-upload-pr-second", Backend.CommandLineArgument.ArgumentType.Size, Strings.Options.MaxuploadprsecondShort, Strings.Options.MaxuploadprsecondLong),
                    new Backend.CommandLineArgument("max-download-pr-second", Backend.CommandLineArgument.ArgumentType.Size, Strings.Options.MaxdownloadprsecondShort, Strings.Options.MaxdownloadprsecondLong),
                    new Backend.CommandLineArgument("skip-files-larger-than", Backend.CommandLineArgument.ArgumentType.Size, Strings.Options.SkipfileslargerthanShort, Strings.Options.SkipfileslargerthanLong),
                });
            }
        }

        /// <summary>
        /// A value indicating if the backup is a full backup
        /// </summary>
        public bool Full { get { return GetBool("full"); } }

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

                return Math.Max(1024 * 1024, Core.Sizeparser.ParseSize(volsize, "mb"));
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
        /// A value indicating if file deletes are forced
        /// </summary>
        public bool UseShortFilenames { get { return GetBool("short-filenames"); } }

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
        /// Gets the process priority
        /// </summary>
        public string TimeSeperatorChar
        {
            get
            {
                if (!m_options.ContainsKey("time-separator") || string.IsNullOrEmpty(m_options["time-separator"]))
                    if (!m_options.ContainsKey("time-seperator") || string.IsNullOrEmpty(m_options["time-seperator"]))
                        return ":";
                    else
                        return m_options["time-seperator"];
                else
                    return m_options["time-separator"];
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
        public int RemoveAllButNFull
        {
            get
            {
                if (!m_options.ContainsKey("remove-all-but-n-full") || string.IsNullOrEmpty(m_options["remove-all-but-n-full"]))
                    throw new Exception("No count given for \"Remove All But N Full\"");

                int x = int.Parse(m_options["remove-all-but-n-full"]);
                if (x <= 0)
                    throw new Exception("Invalid count for remove-all-but-n-full, must be greater than zero");

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
                if (!m_options.ContainsKey("remove-older-than"))
                    throw new Exception("No count given for \"Remove Older Than\"");

                return Core.Timeparser.ParseTimeInterval(m_options["remove-older-than"], DateTime.Now, true);
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
        /// Gets GnuPG program path
        /// </summary>
        public string GPGPath
        {
            get
            {
                if (!m_options.ContainsKey("gpg-program-path") || string.IsNullOrEmpty(m_options["gpg-program-path"]))
                    return "gpg";
                else
                    return m_options["gpg-program-path"];
            }
        }

        /// <summary>
        /// Gets the GPG sign key
        /// </summary>
        public string GPGSignKey
        {
            get
            {
                if (!m_options.ContainsKey("sign-key") || string.IsNullOrEmpty(m_options["sign-key"]))
                    return null;
                else
                    return m_options["sign-key"];
            }
        }

        /// <summary>
        /// A value indicating if backups are not encrypted
        /// </summary>
        public bool NoEncryption { get { return GetBool("no-encryption"); } }

        /// <summary>
        /// A value indicating if GPG encryption is used
        /// </summary>
        public bool GPGEncryption 
        { 
            get { return GetBool("gpg-encryption"); }
            set { m_options["gpg-encryption"] = value.ToString(); }
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
        /// A value indicating if backups are transmitted on a seperate thread
        /// </summary>
        public bool AsynchronousUpload { get { return GetBool("asynchronous-upload"); } }


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

        private bool GetBool(string name)
        {
            if (!m_options.ContainsKey(name))
                return false;
            else
            {
                string v = m_options[name];
                if (string.IsNullOrEmpty(v))
                    return true;
                else
                {
                    v = v.ToLower().Trim();
                    if (v == "false" || v == "no" || v == "off" || v == "0")
                        return false;
                    else
                        return true;
                }

            }
        }

    }
}
