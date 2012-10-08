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
using System.IO;

namespace Duplicati.Library.Main.RSync
{
    /// <summary>
    /// This class wraps the process of creating a diff for an entire folder
    /// </summary>
    public class RSyncDir : IDisposable
    {
        /// <summary>
        /// The time between each progress event
        /// </summary>
        private static readonly TimeSpan PROGRESS_TIMESPAN = TimeSpan.FromSeconds(1);

        /// <summary>
        /// The margin that a file is allowed to grow during signature generation, 
        /// without incurring a performance penalty
        /// </summary>
        private const double FILESIZE_GROW_MARGIN_MULTIPLIER = 1.01;

        /// <summary>
        /// The possible filetypes in an archive
        /// </summary>
        public enum PatchFileType
        {
            /// <summary>
            /// Indicates that the entry represents a deleted folder
            /// </summary>
            DeletedFolder,
            /// <summary>
            /// Indicates that the entry represents an added folder
            /// </summary>
            AddedFolder,
            /// <summary>
            /// Indicates that the entry represents a deleted file
            /// </summary>
            DeletedFile,
            /// <summary>
            /// Indicates that the entry represents an added file
            /// </summary>
            AddedFile,
            /// <summary>
            /// Indicates that the entry represents an updated file
            /// </summary>
            UpdatedFile,
            /// <summary>
            /// Indicates that the entry represents an incomplete file
            /// </summary>
            IncompleteFile,
            /// <summary>
            /// Indicates that the entry represents a control file
            /// </summary>
            ControlFile,
            /// <summary>
            /// Indicates that the entry is a file that is either added or updated.
            /// This state is only possible in Duplicati 1.0 archives.
            /// </summary>
            AddedOrUpdatedFile
        };

        #region Helper classes
        /// <summary>
        /// An internal helper class that aids in supporting three different prefixes for signatures
        /// </summary>
        private class ArchiveWrapper
        {
            /// <summary>
            /// The archive to wrap
            /// </summary>
            private Library.Interface.ICompression m_archive;

            /// <summary>
            /// The prefix to append to request
            /// </summary>
            private string m_prefix;

            /// <summary>
            /// A value indicating if the archive dates are in UTC format
            /// </summary>
            private bool m_isDateInUtc;

            /// <summary>
            /// The time the archive was created
            /// </summary>
            private DateTime m_createTime;

            /// <summary>
            /// Constructs a new ArchiveWrapper with a given prefix
            /// </summary>
            /// <param name="arch">The archive to wrap</param>
            /// <param name="prefix">The prefix to use</param>
            /// <param name="backupTime">The time the backup was created</param>
            public ArchiveWrapper(Library.Interface.ICompression arch, DateTime createTime, string prefix)
            {
                m_archive = arch;
                m_prefix = prefix;
                m_isDateInUtc = arch.FileExists(UTC_TIME_MARKER);
                m_createTime = createTime.ToUniversalTime();
            }

            /// <summary>
            /// Gets a stream representing the content of the file
            /// </summary>
            /// <param name="relpath">The path to the file, excluding the prefix</param>
            /// <returns>An open stream for reading</returns>
            internal Stream OpenRead(string relpath)
            {
                return m_archive.OpenRead(System.IO.Path.Combine(m_prefix, relpath));
            }

            /// <summary>
            /// Gets the time the archive was created
            /// </summary>
            internal DateTime CreateTime { get { return m_createTime; } }
        }

        /// <summary>
        /// An internal helper class that allows a file to span multiple volumes
        /// </summary>
        private class PartialFileEntry : IDisposable
        {
            public readonly string fullname;
            public readonly string relativeName;
            private System.IO.Stream m_fs;
            private System.IO.Stream m_signatureStream;
            private System.IO.Stream m_originalSignatureStream;
            private string m_signaturePath;
            private DateTime m_lastWrite;

            public PartialFileEntry(System.IO.Stream fs, string relname, string fullname, long position, System.IO.Stream signatureFile, string signaturePath, DateTime lastWrite)
            {
                m_fs = fs;
                m_fs.Position = position;
                this.fullname = fullname;
                this.relativeName = relname;
                this.m_signatureStream = signatureFile;
                this.m_signaturePath = signaturePath;
                this.m_lastWrite = lastWrite;
            }

            public System.IO.Stream Stream { get { return m_fs; } }
            public DateTime LastWriteTime { get { return m_lastWrite; } }

            public System.IO.Stream OriginalSignatureStream { get { return m_originalSignatureStream; } set { m_originalSignatureStream = value; } }

            public bool DumpSignature(Library.Interface.ICompression signatureArchive)
            {
                bool success = true;
                //Add signature AFTER content.
                //If content is present, it is restoreable, if signature is missing, file will be backed up on next run
                //If signature is present, but not content, the entire differential sequence will be unable to recover the file

                if (m_fs is SharpRSync.ChecksumGeneratingStream)
                    m_fs.Flush();

                using (m_signatureStream)
                {
                    if (m_originalSignatureStream != null)
                    {
                        //Rewind both streams
                        m_originalSignatureStream.Position = 0;
                        m_signatureStream.Position = 0;

                        success = Utility.Utility.CompareStreams(m_originalSignatureStream, m_signatureStream, true);

                        //Rewind signature
                        m_signatureStream.Position = 0;
                    }

                    using (System.IO.Stream s3 = signatureArchive.CreateFile(this.m_signaturePath, m_lastWrite))
                        Utility.Utility.CopyStream(m_signatureStream, s3, true);
                }

                m_signatureStream = null;
                return success;
            }

            /// <summary>
            /// Gets a value representing a rough estimate of how many bytes this partial entry will use when written to the datastream.
            /// </summary>
            public long ExtraSize { get { return relativeName.Length + 100; } }

            #region IDisposable Members

            public void Dispose()
            {
                if (m_fs != null)
                {
                    m_fs.Dispose();
                    m_fs = null;
                }

                if (m_signatureStream != null)
                {
                    m_signatureStream.Dispose();
                    m_signatureStream = null;
                }

                if (m_originalSignatureStream != null)
                {
                    m_originalSignatureStream.Dispose();
                    m_originalSignatureStream = null;
                }
            }

            #endregion
        }

        /// <summary>
        /// Class that represents a partial record in the INCOMPLETE_FILE or COMPLETED_FILE
        /// </summary>
        public class PartialEntryRecord
        {
            public readonly string Filename;
            public readonly long StartOffset;
            public readonly long Length;
            public readonly long TotalFileSize;
            public readonly string PlatformConvertedFilename;

            public PartialEntryRecord(string[] items)
            {
                if (items.Length != 4)
                    throw new Exception(Strings.RSyncDir.InvalidPartialRecordError);
                this.Filename = items[0];
                this.StartOffset = long.Parse(items[1]);
                this.Length = long.Parse(items[2]);
                this.TotalFileSize = long.Parse(items[3]);
                this.PlatformConvertedFilename = ConvertedFilename(this.Filename);
            }

            public PartialEntryRecord(string filename, long start, long size, long totalSize)
            {
                this.Filename = filename;
                this.StartOffset = start;
                this.Length = size;
                this.TotalFileSize = totalSize;
                this.PlatformConvertedFilename = ConvertedFilename(this.Filename);
            }

            /// <summary>
            /// This function attempts to discover what path separator 
            /// was used to generate the filename, and then convert the
            /// filename to use the current OS separator.
            /// 
            /// This fixes an issue where the backup is made on
            /// one OS and attempted restored on another.
            /// 
            /// This is really a sub-optimal workaround for
            /// the fact that the filename should have been recorded
            /// as a platform independent filename.
            /// 
            /// Changing this requires increasing the manifest
            /// version number to ensure backwards compatibility,
            /// and then changing the recording function
            /// </summary>
            /// <param name="filename">The filename to convert</param>
            /// <returns>The filename adapted to the local OS filename convetions</returns>
            private static string ConvertedFilename(string filename)
            {
                //TODO: Once recorded correctly, this can just be:
                //return FromplatformIndependantFilename(filename);

                //We know that the filename is prefixed with either "snapshot" or "delta",
                // so we just look for the first seperator (assumes there is only / and \),
                int ix_linux = filename.IndexOf('/');
                int ix_win = filename.IndexOf('\\');
                
                char sep_char;
                if (ix_linux >= 0 && ix_win >= 0)
                    sep_char = filename[Math.Min(ix_linux, ix_win)];
                else if (ix_linux >= 0)
                    sep_char = filename[ix_linux];
                else if (ix_win >= 0)
                    sep_char = filename[ix_win];
                else
                    //No sep char means no need to convert
                    return filename;

                if (sep_char != System.IO.Path.DirectorySeparatorChar)
                    filename = filename.Replace(sep_char, System.IO.Path.DirectorySeparatorChar);
                
                return filename;
            }

            public string[] Serialize()
            {
                return new string[] {
                    this.Filename,
                    this.StartOffset.ToString(),
                    this.Length.ToString(),
                    this.TotalFileSize.ToString()
                };
            }
        }

        /// <summary>
        /// Class that represents a USN record
        /// </summary>
        public class USNRecord
        {
            /// <summary>
            /// The USN value list
            /// </summary>
            private Dictionary<string, KeyValuePair<long, long>> m_values;

            /// <summary>
            /// Constructs a new USNRecord
            /// </summary>
            public USNRecord()
            {
                m_values = new Dictionary<string, KeyValuePair<long, long>>(Utility.Utility.ClientFilenameStringComparer);
            }


            /// <summary>
            /// Reads an existing UNSRecord from an xml file
            /// </summary>
            /// <param name="stream">The stream with xml data</param>
            public USNRecord(System.IO.Stream stream)
                : this(CreateDoc(stream))
            {
            }

            /// <summary>
            /// Reads an existing USNRecord from an xml document
            /// </summary>
            /// <param name="doc">The XmlDocument with USN data</param>
            public USNRecord(System.Xml.XmlDocument doc)
                : this()
            {
                System.Xml.XmlNode root = doc["usnroot"];
                foreach (System.Xml.XmlNode n in root.SelectNodes("usnrecord"))
                    if (n.Attributes["root"] != null && n.Attributes["journalid"] != null && n.Attributes["usn"] != null)
                        m_values.Add(n.Attributes["root"].Value, new KeyValuePair<long, long>(long.Parse(n.Attributes["journalid"].Value), long.Parse(n.Attributes["usn"].Value)));
            }

            /// <summary>
            /// Internal helper that creates an XmlDocument from a stream
            /// </summary>
            /// <param name="stream">The stream data to read</param>
            /// <returns>An XmlDocument</returns>
            private static System.Xml.XmlDocument CreateDoc(System.IO.Stream stream)
            {
                System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                doc.Load(stream);
                return doc;
            }

            /// <summary>
            /// Saves the USN data to an XmlDocument
            /// </summary>
            /// <returns>An XmlDocument with the USN data</returns>
            public System.Xml.XmlDocument Save()
            {
                System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                System.Xml.XmlNode root = doc.AppendChild(doc.CreateElement("usnroot"));
                foreach (KeyValuePair<string, KeyValuePair<long, long>> kv in m_values)
                {
                    System.Xml.XmlNode n = root.AppendChild(doc.CreateElement("usnrecord"));
                    n.Attributes.Append(doc.CreateAttribute("root")).Value = kv.Key;
                    n.Attributes.Append(doc.CreateAttribute("journalid")).Value = kv.Value.Key.ToString();
                    n.Attributes.Append(doc.CreateAttribute("usn")).Value = kv.Value.Value.ToString();
                }

                return doc;
            }

            /// <summary>
            /// Saves the USN data to a stream as xml
            /// </summary>
            /// <param name="stream">The stream to write the xml data to</param>
            public void Save(System.IO.Stream stream)
            {
                Save().Save(stream);
            }

            /// <summary>
            /// Gets the USN values recorded
            /// </summary>
            public Dictionary<string, KeyValuePair<long, long>> Values
            {
                get { return m_values; }
            }

            /// <summary>
            /// Gets a value indicating if the root folder was found in this USN entry
            /// </summary>
            /// <param name="rootFolder">The root folder to look for</param>
            /// <returns>True if found, false otherwise</returns>
            public bool ContainsKey(string rootFolder)
            {
                return m_values.ContainsKey(rootFolder);
            }

            /// <summary>
            /// Gets the JournalId and USN number for the root folder
            /// </summary>
            /// <param name="index">The root folder to look for</param>
            /// <returns>The JournalId and USN number</returns>
            public KeyValuePair<long, long> this[string index]
            {
                get { return m_values[index]; }
            }

            /// <summary>
            /// Adds a new USN record
            /// </summary>
            /// <param name="rootfolder">The folder this entry represents</param>
            /// <param name="journalId">The volume journalId</param>
            /// <param name="usn">The volume USN</param>
            public void Add(string rootfolder, long journalId, long usn)
            {
                m_values.Add(rootfolder, new KeyValuePair<long, long>(journalId, usn));
            }
        }

        #endregion

        #region Filenames
        internal const string COMBINED_SIGNATURE_ROOT = "signature";
        internal const string CONTENT_SIGNATURE_ROOT = "content_signature";
        internal const string DELTA_SIGNATURE_ROOT = "delta_signature";

        internal const string CONTENT_ROOT = "snapshot";
        internal const string DELTA_ROOT = "diff";
        //private const string DACL_ROOT = "dacl";
        internal const string CONTROL_ROOT = "controlfiles";
        internal const string SYMLINK_ROOT = "symlinks"; 

        internal static readonly string DELETED_FILES = "deleted_files.txt";
        internal static readonly string DELETED_FOLDERS = "deleted_folders.txt";

        internal static readonly string ADDED_FOLDERS = "added_folders.txt";
        internal static readonly string ADDED_FOLDERS_TIMESTAMPS = "added_folders_timestamps.txt";
        internal static readonly string INCOMPLETE_FILE = "incomplete_file.txt";
        internal static readonly string COMPLETED_FILE = "completed_file.txt";
        internal static readonly string UTC_TIME_MARKER = "utc-times";
        internal static readonly string UPDATED_FOLDERS = "updated_folders.txt";
        internal static readonly string UPDATED_FOLDERS_TIMESTAMPS = "updated_folders_timestamps.txt";
        internal static readonly string UNMODIFIED_FILES = "unmodified-files.txt";
        internal static readonly string USN_VALUES = "usn-values.xml";
        #endregion

        public delegate void ProgressEventDelegate(int progress, string filename);
        public event ProgressEventDelegate ProgressEvent;

        #region Private instance variables
        /// <summary>
        /// This is the folders being backed up
        /// </summary>
        public string[] m_sourcefolder;

        /// <summary>
        /// This is a list of existing file signatures.
        /// Key is path to the file. 
        /// value is path to the signature file.
        /// </summary>
        private Dictionary<string, ArchiveWrapper> m_oldSignatures;
        /// <summary>
        /// This is a list of existing folders.
        /// </summary>
        private Dictionary<string, DateTime> m_oldFolders;
        /// <summary>
        /// This is a list of old symlinks
        /// </summary>
        private Dictionary<string, string> m_oldSymlinks;

        /// <summary>
        /// This is the list of added files
        /// </summary>
        private Dictionary<string, string> m_newfiles;
        /// <summary>
        /// This is the list of modified files
        /// </summary>
        private Dictionary<string, string> m_modifiedFiles;
        /// <summary>
        /// This is the list of deleted files
        /// </summary>
        private List<string> m_deletedfiles;
        /// <summary>
        /// This is the list of examined files that were unchanged
        /// </summary>
        private List<string> m_checkedUnchangedFiles;
        /// <summary>
        /// This is the list of added folders
        /// </summary>
        private List<KeyValuePair<string, DateTime>> m_newfolders;
        /// <summary>
        /// This is the list of updated folders
        /// </summary>
        private List<KeyValuePair<string, DateTime>> m_updatedfolders;
        /// <summary>
        /// This is the list of deleted folders
        /// </summary>
        private List<string> m_deletedfolders;
        /// <summary>
        /// This is a dictionary with the last time a file was examined (and not changed) with the current signature
        /// </summary>
        private Dictionary<string, DateTime> m_lastVerificationTime;

        /// <summary>
        /// The total number of files found
        /// </summary>
        private long m_totalfiles;
        /// <summary>
        /// The number of files examined
        /// </summary>
        private long m_examinedfiles;
        /// <summary>
        /// The number of files opened for matching
        /// </summary>
        private long m_filesopened;
        /// <summary>
        /// The combined size of the examined files
        /// </summary>
        private long m_examinedfilesize;
        /// <summary>
        /// The number of files that are found to be modified
        /// </summary>
        private long m_diffedfiles;
        /// <summary>
        /// The combined size of all the modified files
        /// </summary>
        private long m_diffedfilessize;
        /// <summary>
        /// The combines size of all delta files generated
        /// </summary>
        private long m_diffsize;
        /// <summary>
        /// The number of files added
        /// </summary>
        private long m_addedfiles;
        /// <summary>
        /// The combined size of all added files
        /// </summary>
        private long m_addedfilessize;

        /// <summary>
        /// The filter applied to restore or backup
        /// </summary>
        private Utility.FilenameFilter m_filter;

        /// <summary>
        /// Statistics reporting
        /// </summary>
        private CommunicationStatistics m_stat;
        /// <summary>
        /// Flag indicating if the final values are written to the signature file
        /// </summary>
        private bool m_finalized = false;

        /// <summary>
        /// A flag indicating if this entry is the first multipass
        /// </summary>
        private bool m_isfirstmultipass = false;

        /// <summary>
        /// A variable that controls if the filetime check is disabled
        /// </summary>
        private bool m_disableFiletimeCheck = false;

        /// <summary>
        /// A variable that controls the maximum size of a file
        /// </summary>
        private long m_maxFileSize = long.MaxValue;

        /// <summary>
        /// This is a list of unprocessed files, used in multipass runs
        /// </summary>
        private PathCollector m_unproccesed;

        /// <summary>
        /// A list of patch files for removal
        /// </summary>
        private List<Library.Interface.ICompression> m_patches;

        /// <summary>
        /// A leftover file that is partially written, used when creating backups
        /// </summary>
        private PartialFileEntry m_lastPartialFile = null;

        /// <summary>
        /// A list of partial delta files, used when restoring
        /// </summary>
        private Dictionary<string, Utility.TempFile> m_partialDeltas;

        /// <summary>
        /// A list of timestamps to be assigned to the folders during restore finalization
        /// </summary>
        private Dictionary<string, DateTime> m_folderTimestamps;

        /// <summary>
        /// A list of folders that should be deleted after the current restore operation has completed
        /// </summary>
        private List<string> m_folders_to_delete;

        /// <summary>
        /// The snapshot control that guards this backup
        /// </summary>
        private Snapshots.ISnapshotService m_snapshot;

        /// <summary>
        /// The open file strategy to use if not using VSS
        /// </summary>
        private Options.OpenFileStrategy m_openfilepolicy;

        /// <summary>
        /// The USN values recorded in this run
        /// </summary>
        private USNRecord m_currentUSN = null;

        /// <summary>
        /// The set of USN values from the last complete run
        /// </summary>
        private USNRecord m_lastUSN = null;

        /// <summary>
        /// A variable that indicates if the file list is sorted, if this variable is false,
        /// files are picked at random from the file list
        /// </summary>
        private bool m_sortedfilelist;

        #endregion

        /// <summary>
        /// Initializes a RSyncDir instance, binds it to the source folder, and reads in the supplied patches
        /// </summary>
        /// <param name="sourcefolder">The folders to create a backup from</param>
        /// <param name="stat">The status report object</param>
        /// <param name="filter">An optional filter that controls what files to include</param>
        /// <param name="patches">A list of signature archives to read, MUST be sorted in the creation order, oldest first</param>
        public RSyncDir(string[] sourcefolder, CommunicationStatistics stat, Utility.FilenameFilter filter, List<KeyValuePair<ManifestEntry, Library.Interface.ICompression>> patches)
            : this(sourcefolder, stat, filter)
        {
            string[] prefixes = new string[] {
                Utility.Utility.AppendDirSeparator(COMBINED_SIGNATURE_ROOT),
                Utility.Utility.AppendDirSeparator(CONTENT_SIGNATURE_ROOT),
                Utility.Utility.AppendDirSeparator(DELTA_SIGNATURE_ROOT)
            };

            m_patches = new List<Duplicati.Library.Interface.ICompression>();
            foreach (KeyValuePair<ManifestEntry, Library.Interface.ICompression> patch in patches)
                m_patches.Add(patch.Value);

            foreach (KeyValuePair<ManifestEntry, Library.Interface.ICompression> patch in patches)
            {
                Library.Interface.ICompression z = patch.Value;

                if (z.FileExists(DELETED_FILES))
                    foreach (string s in FilenamesFromPlatformIndependant(z.ReadAllLines(DELETED_FILES)))
                    {
                        m_oldSignatures.Remove(s);
                        m_lastVerificationTime.Remove(s);
                        m_oldSymlinks.Remove(s);
                    }

                foreach (string prefix in prefixes)
                {
                    ArchiveWrapper aw = new ArchiveWrapper(z, patch.Key.Time.ToUniversalTime(), prefix);
                    foreach (string f in FilenamesFromPlatformIndependant(z.ListFiles(prefix)))
                    {
                        string name = f.Substring(prefix.Length);
                        m_oldSignatures[name] = aw;
                        m_lastVerificationTime.Remove(name);
                    }
                }

                string symlinkprefix = Utility.Utility.AppendDirSeparator(SYMLINK_ROOT);
                foreach(string s in FilenamesFromPlatformIndependant(patch.Value.ListFiles(symlinkprefix)))
                {
                    string tmp = FilenamesFromPlatformIndependant( new string[] { Encoding.UTF8.GetString(patch.Value.ReadAllBytes(s)) })[0];
                    m_oldSymlinks[s.Substring(symlinkprefix.Length)] = tmp;
                }

                if (z.FileExists(UNMODIFIED_FILES))
                    foreach (string s in FilenamesFromPlatformIndependant(z.ReadAllLines(UNMODIFIED_FILES)))
                        m_lastVerificationTime[s] = patch.Key.Time.ToUniversalTime();

                if (z.FileExists(DELETED_FOLDERS))
                    foreach (string s in FilenamesFromPlatformIndependant(z.ReadAllLines(DELETED_FOLDERS)))
                        m_oldFolders.Remove(s);

                if (z.FileExists(ADDED_FOLDERS))
                {
                    DateTime t = z.GetLastWriteTime(ADDED_FOLDERS).ToUniversalTime();
                    string[] filenames = FilenamesFromPlatformIndependant(z.ReadAllLines(ADDED_FOLDERS));

                    if (z.FileExists(ADDED_FOLDERS_TIMESTAMPS))
                    {
                        string[] timestamps = z.ReadAllLines(ADDED_FOLDERS_TIMESTAMPS);
                        long l;
                        for(int i = 0; i < Math.Min(filenames.Length, timestamps.Length); i++)
                            if (long.TryParse(timestamps[i], out l))
                                m_oldFolders[filenames[i]] = Library.Utility.Utility.EPOCH.AddSeconds(l);
                            else
                                m_oldFolders[filenames[i]] = t;
                    }
                    else
                    {
                        foreach (string s in filenames)
                            m_oldFolders[s] = t;
                    }
                }

                if (z.FileExists(UPDATED_FOLDERS) && z.FileExists(UPDATED_FOLDERS_TIMESTAMPS))
                {
                    string[] filenames = FilenamesFromPlatformIndependant(z.ReadAllLines(UPDATED_FOLDERS));
                    string[] timestamps = z.ReadAllLines(UPDATED_FOLDERS_TIMESTAMPS);
                    long l;

                    for (int i = 0; i < Math.Min(filenames.Length, timestamps.Length); i++)
                        if (long.TryParse(timestamps[i], out l))
                            m_oldFolders[filenames[i]] = Utility.Utility.EPOCH.AddSeconds(l);
                }

                //The latest file is the most valid
                if (z.FileExists(USN_VALUES))
                    using (System.IO.Stream s = z.OpenRead(USN_VALUES))
                        m_lastUSN = new USNRecord(s);
            }
        }

        /// <summary>
        /// Initializes a RSyncDir instance, and binds it to the source folder
        /// </summary>
        /// <param name="sourcefolder">The folders to create a backup from</param>
        /// <param name="stat">The status report object</param>
        /// <param name="filter">An optional filter that controls what files to include</param>
        public RSyncDir(string[] sourcefolder, CommunicationStatistics stat, Utility.FilenameFilter filter)
        {
            m_filter = filter;
            m_oldSignatures = new Dictionary<string, ArchiveWrapper>(Utility.Utility.ClientFilenameStringComparer);
            m_oldFolders = new Dictionary<string, DateTime>(Utility.Utility.ClientFilenameStringComparer);
            m_oldSymlinks = new Dictionary<string, string>(Utility.Utility.ClientFilenameStringComparer);
            m_lastVerificationTime = new Dictionary<string, DateTime>(Utility.Utility.ClientFilenameStringComparer);

            for (int i = 0; i < sourcefolder.Length; i++)
            {
                if (!System.IO.Path.IsPathRooted(sourcefolder[i]))
                    sourcefolder[i] = System.IO.Path.GetFullPath(sourcefolder[i]); 
                sourcefolder[i] = Utility.Utility.AppendDirSeparator(sourcefolder[i]);
            }
            m_stat = stat;
            m_sourcefolder = sourcefolder;

            if (m_filter == null)
                m_filter = new Duplicati.Library.Utility.FilenameFilter(new List<KeyValuePair<bool, string>>());
        }

        /// <summary>
        /// Creates a new patch content/signature pair.
        /// Does not create multiple volumes, not used by Duplicati
        /// </summary>
        /// <param name="signatures">An archive where signatures can be put into</param>
        /// <param name="content">An archive where content can be put into</param>
        public void CreatePatch(Library.Interface.ICompression signatures, Library.Interface.ICompression content)
        {
            InitiateMultiPassDiff(false);
            MakeMultiPassDiff(signatures, content, long.MaxValue);
            FinalizeMultiPass(signatures, content, long.MaxValue);
        }

        /// <summary>
        /// Initiates creating a content/signature pair.
        /// </summary>
        /// <param name="full">True if the set is a full backup, false if it is incremental</param>
        public void InitiateMultiPassDiff(bool full)
        {
            InitiateMultiPassDiff(full, new Options(new Dictionary<string, string>()));
        }

        /// <summary>
        /// Initiates creating a content/signature pair.
        /// </summary>
        /// <param name="full">True if the set is a full backup, false if it is incremental</param>
        /// <param name="options">Any setup options to use</param>
        public void InitiateMultiPassDiff(bool full, Options options)
        {
            if (full)
            {
                m_oldFolders = new Dictionary<string, DateTime>();
                m_oldSignatures = new Dictionary<string, ArchiveWrapper>();
                m_oldSymlinks = new Dictionary<string, string>();
            }

            m_newfiles = new Dictionary<string, string>();
            m_modifiedFiles = new Dictionary<string, string>();
            m_deletedfiles = new List<string>();
            m_newfolders = new List<KeyValuePair<string, DateTime>>();
            m_updatedfolders = new List<KeyValuePair<string, DateTime>>();
            m_deletedfolders = new List<string>();
            m_checkedUnchangedFiles = new List<string>();
            m_lastPartialFile = null;
            m_openfilepolicy = Options.OpenFileStrategy.Ignore;

            try
            {
                if (options.SnapShotStrategy != Options.OptimizationStrategy.Off)
                    m_snapshot = Duplicati.Library.Snapshots.SnapshotUtility.CreateSnapshot(m_sourcefolder, options.RawOptions);
            }
            catch (Exception ex)
            {
                if (options.SnapShotStrategy == Options.OptimizationStrategy.Required)
                    throw;
                else if (options.SnapShotStrategy == Options.OptimizationStrategy.On)
                {
                    if (m_stat != null)
                        m_stat.LogWarning(string.Format(Strings.RSyncDir.SnapshotFailedError, ex.ToString()), ex);
                }
            }

            //Failsafe, just use a plain implementation
            if (m_snapshot == null)
            {
                m_snapshot = Utility.Utility.IsClientLinux ? 
                    (Library.Snapshots.ISnapshotService)new Duplicati.Library.Snapshots.NoSnapshotLinux(m_sourcefolder, options.RawOptions)
                        :
                    (Library.Snapshots.ISnapshotService)new Duplicati.Library.Snapshots.NoSnapshotWindows(m_sourcefolder, options.RawOptions);
                m_openfilepolicy = options.OpenFilePolicy;
            }
            
            Dictionary<string, Snapshots.USNHelper> usnHelpers = null;
            List<string> unchanged = new List<string>();
            m_unproccesed = new PathCollector(m_snapshot, options.SymlinkPolicy, options.FileAttributeFilter, m_filter, m_stat);

            try
            {
                if (options.UsnStrategy != Options.OptimizationStrategy.Off)
                {
                    if (Utility.Utility.IsClientLinux && options.UsnStrategy != Options.OptimizationStrategy.Auto)
                        throw new Exception(Strings.RSyncDir.UsnNotSupportedOnLinuxError);

                    /*
                    if (options.DisableUSNDiffCheck)
                        m_lastUSN = null;
                    */

                    usnHelpers = new Dictionary<string, Duplicati.Library.Snapshots.USNHelper>(Utility.Utility.ClientFilenameStringComparer);
                    foreach (string s in m_sourcefolder)
                    {
                        string rootFolder = System.IO.Path.GetPathRoot(s);
                        if (!usnHelpers.ContainsKey(rootFolder))
                            try { usnHelpers[rootFolder] = new Duplicati.Library.Snapshots.USNHelper(rootFolder); }
                            catch (Exception ex)
                            {
                                if (options.UsnStrategy == Options.OptimizationStrategy.Required)
                                    throw;
                                else if (options.UsnStrategy == Options.OptimizationStrategy.On)
                                {
                                    if (m_stat != null)
                                        m_stat.LogWarning(string.Format(Strings.RSyncDir.UsnFailedError, ex.ToString()), ex);
                                }
                            }

                        if (usnHelpers.ContainsKey(rootFolder))
                        {
                            //This code is broken, see issue 332:
                            //http://code.google.com/p/duplicati/issues/detail?id=332

                            /* if (m_lastUSN != null && m_lastUSN.ContainsKey(rootFolder))
                            {
                                if (m_lastUSN[rootFolder].Key != usnHelpers[rootFolder].JournalID)
                                {
                                    if (m_stat != null)
                                        m_stat.LogWarning(string.Format(Strings.RSyncDir.UsnJournalIdChangedWarning, rootFolder, m_lastUSN[rootFolder].Key, usnHelpers[rootFolder].JournalID), null);

                                    //Just take it all again
                                    usnHelpers[rootFolder].EnumerateFilesAndFolders(s, m_filter, m_unproccesed.Callback);
                                }
                                else if (m_lastUSN[rootFolder].Value > usnHelpers[rootFolder].USN)
                                {
                                    if (m_stat != null)
                                        m_stat.LogWarning(string.Format(Strings.RSyncDir.UsnNumberingFaultWarning, rootFolder, m_lastUSN[rootFolder].Value, usnHelpers[rootFolder].USN), null);

                                    //Just take it all again
                                    usnHelpers[rootFolder].EnumerateFilesAndFolders(s, m_filter, m_unproccesed.Callback);
                                }
                                else //All good we rely on USN numbers to find a list of changed files
                                {
                                    //Find all changed files and folders
                                    Dictionary<string, string> tmp = new Dictionary<string, string>(Utility.Utility.ClientFilenameStringComparer);
                                    foreach (string sx in usnHelpers[rootFolder].GetChangedFileSystemEntries(s, m_lastUSN[rootFolder].Value))
                                    {
                                        tmp.Add(sx, null);
                                        
                                        if (!sx.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
                                            m_unproccesed.Files.Add(sx);
                                    }

                                    //Remove the existing unchanged ones
                                    foreach (string x in usnHelpers[rootFolder].GetFileSystemEntries(s))
                                        if (!tmp.ContainsKey(x))
                                            unchanged.Add(x);
                                }
                            }
                            else */
                            {
                                usnHelpers[rootFolder].EnumerateFilesAndFolders(s, m_unproccesed.Callback);
                            }
                        }

                    }

                    if (usnHelpers.Count > 0)
                    {
                        m_currentUSN = new USNRecord();
                        foreach (KeyValuePair<string, Snapshots.USNHelper> kx in usnHelpers)
                            m_currentUSN.Add(kx.Key, kx.Value.JournalID, kx.Value.USN);
                    }
                }
            }
            catch (Exception ex)
            {
                if (options.UsnStrategy == Options.OptimizationStrategy.Required)
                    throw;
                else if (options.UsnStrategy == Options.OptimizationStrategy.On)
                {
                    if (m_stat != null)
                        m_stat.LogWarning(string.Format(Strings.RSyncDir.UsnFailedError, ex.ToString()), ex);
                }

                //If we get here, something went really wrong with USN, so we disable it
                m_currentUSN = null;
                m_unproccesed = new PathCollector(m_snapshot, options.SymlinkPolicy, options.FileAttributeFilter, m_filter, m_stat);
                unchanged = null;
            }
            finally
            {
                if (usnHelpers != null)
                    foreach(Snapshots.USNHelper h in usnHelpers.Values)
                        try { h.Dispose(); }
                        catch (Exception ex) 
                        {
                            if (m_stat != null)
                                m_stat.LogWarning(string.Format(Strings.RSyncDir.UsnDisposeFailedWarning, ex.ToString()), ex);
                        }
            }


            if (m_currentUSN == null)
            {
                m_snapshot.EnumerateFilesAndFolders(m_unproccesed.Callback);
            }
            else
            {
                //Skip all items that we know are unchanged
                foreach (string x in unchanged)
                {
                    string relpath = GetRelativeName(x);
                    m_oldSignatures.Remove(relpath);
                    m_oldFolders.Remove(relpath);
                    m_oldSymlinks.Remove(relpath);
                }

                //If some folders did not support USN, add their files now
                foreach (string s in m_sourcefolder)
                    if (!m_currentUSN.ContainsKey(System.IO.Path.GetPathRoot(s)))
                        m_snapshot.EnumerateFilesAndFolders(s, m_unproccesed.Callback);
            }

            m_totalfiles = m_unproccesed.Files.Count;
            m_isfirstmultipass = true;

            if (options.ExcludeEmptyFolders)
            {
                //We remove the folders that have no files.
                //It would be more optimal to exclude them from the list before this point,
                // but that would require rewriting of the snapshots

                //We can't rely on the order of the folders, so we sort them to get the shortest folder names first
                m_unproccesed.Folders.Sort(Utility.Utility.ClientFilenameStringComparer);

                //We can't rely on the order of the files either, but sorting them allows us to use a O=log(n) search rather than O=n
                m_unproccesed.Files.Sort(Utility.Utility.ClientFilenameStringComparer);

                for (int i = 0; i < m_unproccesed.Folders.Count; i++)
                {
                    string folder = m_unproccesed.Folders[i];
                    int ix = m_unproccesed.Files.BinarySearch(folder, Utility.Utility.ClientFilenameStringComparer);
                    if (ix >= 0)
                        continue; //Should not happen, means that a file has the same name as a folder
                    
                    //Get the next index larger than the foldername
                    ix = ~ix;

                    if (ix >= m_unproccesed.Files.Count)
                    {
                        //No files matched
                        m_unproccesed.Folders.RemoveAt(i);
                        i--;
                    }
                    else
                    {
                        //If the element does not start with the foldername, no files from the folder are included
                        if (!m_unproccesed.Files[ix].StartsWith(folder, Utility.Utility.ClientFilenameStringComparision))
                        {
                            //Speedup, remove all subfolders as well without performing binary searches
                            while (i < m_unproccesed.Folders.Count && m_unproccesed.Folders[i].StartsWith(folder))
                                m_unproccesed.Folders.RemoveAt(i);

                            //We have removed at least one, so adjust the loop counter
                            i--;
                        }
                    }
                }
            }

            //Build folder diffs
            foreach(string s in m_unproccesed.Folders)
            {
                try
                {
                    string relpath = GetRelativeName(s);
                    if (relpath.Trim().Length != 0)
                    {
                        DateTime lastWrite = m_snapshot.GetLastWriteTime(s).ToUniversalTime();

                        //Cut off as we only have seconds stored
                        lastWrite = new DateTime(lastWrite.Year, lastWrite.Month, lastWrite.Day, lastWrite.Hour, lastWrite.Minute, lastWrite.Second, DateTimeKind.Utc);

                        if (!m_oldFolders.ContainsKey(relpath))
                            m_newfolders.Add(new KeyValuePair<string, DateTime>(relpath, lastWrite));
                        else
                        {
                            if (m_oldFolders[relpath] != lastWrite)
                                m_updatedfolders.Add(new KeyValuePair<string, DateTime>(relpath, lastWrite));
                            m_oldFolders.Remove(relpath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    m_unproccesed.Errors.Add(s);
                    m_stat.LogError(string.Format(Strings.RSyncDir.FolderModificationTimeReadError, s, ex.Message), ex);
                }
            }

            m_unproccesed.Folders.Clear();
            foreach(string s in m_oldFolders.Keys)
                if (!m_unproccesed.IsAffectedByError(s))
                    m_deletedfolders.Add(s);

             //Build symlink diffs
            if (m_oldSymlinks.Count > 0)
            {
                for (int i = m_unproccesed.Symlinks.Count - 1; i >= 0; i--)
                {
                    string s = m_unproccesed.Symlinks[i].Key;
                    try
                    {
                        string relpath = GetRelativeName(s);
                        if (relpath.Trim().Length != 0)
                        {
                            string oldLink;
                            if (m_oldSymlinks.TryGetValue(relpath, out oldLink))
                            {
                                m_oldSymlinks.Remove(relpath);
                                if (string.Equals(oldLink, m_unproccesed.Symlinks[i].Value, Utility.Utility.ClientFilenameStringComparision))
                                {
                                    m_unproccesed.Symlinks.RemoveAt(i);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        m_unproccesed.Errors.Add(s);
                        m_stat.LogError(string.Format(Strings.RSyncDir.SymlinkReadError, s, ex.Message), ex);
                    }
                }
            }

            m_deletedfiles.AddRange(m_oldSymlinks.Keys);
            m_oldSymlinks.Clear();

            m_sortedfilelist = options.SortedFilelist;
            if (m_sortedfilelist)
                m_unproccesed.Files.Sort(Utility.Utility.ClientFilenameStringComparer);
        }

        /// <summary>
        /// Gets the source folder that contains the given path
        /// </summary>
        /// <param name="path">The path to find the source folder for</param>
        /// <returns>The source folder path</returns>
        private string GetSourceFolder(string path)
        {
            foreach (string s in m_sourcefolder)
                if (path.StartsWith(s, Utility.Utility.IsFSCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase))
                    return s;

            throw new Exception(string.Format(Strings.RSyncDir.InternalPathMappingError, path, string.Join(System.IO.Path.PathSeparator.ToString(), m_sourcefolder)));
        }

        /// <summary>
        /// Returns the relative name for a file.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private string GetRelativeName(string path)
        {
            if (m_sourcefolder.Length == 1)
                return path.Substring(m_sourcefolder[0].Length);

            for (int i = 0; i < m_sourcefolder.Length; i++)
                if (path.StartsWith(m_sourcefolder[i], Utility.Utility.IsFSCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase))
                    if (path.Length == m_sourcefolder[i].Length)
                        return Utility.Utility.AppendDirSeparator(i.ToString()); //This is a folder, and must be suffix with a slash
                    else
                        return System.IO.Path.Combine(i.ToString(), path.Substring(m_sourcefolder[i].Length)); //This will use whatever suffix the path already has

            throw new Exception(string.Format(Strings.RSyncDir.InternalPathMappingError, path, string.Join(System.IO.Path.PathSeparator.ToString(), m_sourcefolder)));
        }

        private static string GetRelativeName(string[] sourcefolders, string path)
        {
            if (sourcefolders.Length == 1)
            {
                if (!path.StartsWith(Utility.Utility.AppendDirSeparator(sourcefolders[0]), Utility.Utility.ClientFilenameStringComparision))
                    throw new Exception(string.Format(Strings.RSyncDir.InternalPathMappingError, path, sourcefolders[0]));
                return path.Substring(Utility.Utility.AppendDirSeparator(sourcefolders[0]).Length);
            }

            for (int i = 0; i < sourcefolders.Length; i++)
                if (path.StartsWith(Utility.Utility.AppendDirSeparator(sourcefolders[i]), Utility.Utility.ClientFilenameStringComparision))
                    return System.IO.Path.Combine(i.ToString(), path.Substring(Utility.Utility.AppendDirSeparator(sourcefolders[i]).Length));

            throw new Exception(string.Format(Strings.RSyncDir.InternalPathMappingError, path, string.Join(System.IO.Path.PathSeparator.ToString(), sourcefolders)));
        }

        /// <summary>
        /// Gets the system path, given a relative filename
        /// </summary>
        /// <param name="relpath">The relative filename</param>
        /// <returns>The full system path to the file</returns>
        private string GetFullPathFromRelname(string relpath)
        {
            return GetFullPathFromRelname(m_sourcefolder, relpath);
        }

        /// <summary>
        /// Maps a list of relative names to their full path names
        /// </summary>
        /// <param name="sourcefolders">The list of target folders supplied</param>
        /// <param name="relpath">A list of relative filenames</param>
        /// <returns>A list of absolute filenames</returns>
        private static List<string> GetFullPathFromRelname(string[] sourcefolders, List<string> relpath)
        {
            for(int i = 0; i < relpath.Count; i++)
                relpath[i] = GetFullPathFromRelname(sourcefolders, relpath[i]);

            return relpath;
        }

        /// <summary>
        /// Maps a relative name to its target folder
        /// </summary>
        /// <param name="sourcefolders">The list of target folders supplied</param>
        /// <param name="relpath">The relative path to map</param>
        /// <returns>A full path, based on the relative path</returns>
        private static string GetFullPathFromRelname(string[] sourcefolders, string relpath)
        {
            try
            {
                if (sourcefolders.Length == 1)
                    return System.IO.Path.Combine(sourcefolders[0], relpath);
                else
                {
                    int ix = relpath.IndexOf(System.IO.Path.DirectorySeparatorChar);
                    //In some versions, Duplicati incorrectly writes the folder name without a trailing slash
                    int pos = ix < 0 ? int.Parse(relpath) : int.Parse(relpath.Substring(0, ix));
                    return System.IO.Path.Combine(sourcefolders[Math.Min(sourcefolders.Length - 1, pos)], relpath.Substring(ix + 1));
                }
            }
            catch (Exception ex)
            {
                throw new ArgumentException(string.Format(Strings.RSyncDir.InvalidRelFilenameError, relpath, string.Join(Environment.NewLine, sourcefolders), ex.Message) , ex);
            }
        }

        /// <summary>
        /// Ends the sequence of creating a content/signature pair.
        /// Writes the list of deleted files to the archives.
        /// </summary>
        /// <param name="signaturefile">The signature archive file</param>
        /// <param name="contentfile">The content archive file</param>
        /// <param name="volumesize">The max volume size</param>
        /// <returns>True if the volume is completed, false otherwise</returns>
        public bool FinalizeMultiPass(Library.Interface.ICompression signaturefile, Library.Interface.ICompression contentfile, long volumesize)
        {
            if (!m_finalized)
            {
                if (m_unproccesed.Files.Count == 0)
                {
                    long stringsize = 0;
                    foreach (string s in m_oldSignatures.Keys)
                    {
                        string sourcefolder = "<unknown>";
                        try
                        {
                            string fullpath = GetFullPathFromRelname(s);
                            sourcefolder = GetSourceFolder(fullpath);
                            if (!m_unproccesed.IsAffectedByError(fullpath))
                            {
                                m_deletedfiles.Add(s);
                                stringsize += System.Text.Encoding.UTF8.GetByteCount(s + "\r\n");
                            }
                        }
                        catch (Exception ex)
                        {
                            if (m_stat != null)
                                m_stat.LogError(string.Format(Strings.RSyncDir.DeletedFilenameError, s, sourcefolder), ex);
                            Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.DeletedFilenameError, s, sourcefolder), Duplicati.Library.Logging.LogMessageType.Error, ex);
                            m_unproccesed.FilesWithError.Add(s);
                        }
                    }

                    m_oldSignatures.Clear();

                    if (m_deletedfiles.Count > 0)
                    {
                        //The +100 is a safety margin
                        stringsize += System.Text.Encoding.UTF8.GetByteCount(DELETED_FILES) + 100;
                        if (contentfile.Size + contentfile.FlushBufferSize + stringsize > volumesize)
                            return false; //The followup cannot fit in the volume, so we make a full new volume

                        signaturefile.WriteAllLines(DELETED_FILES, m_deletedfiles.ToArray());
                        contentfile.WriteAllLines(DELETED_FILES, m_deletedfiles.ToArray());
                        m_deletedfiles.Clear();
                    }

                    //We only write the USN if all files were processed
                    if (m_currentUSN != null)
                        using (System.IO.Stream s = signaturefile.CreateFile(USN_VALUES))
                            m_currentUSN.Save(s);

                    //Only write this if all files were processed
                    if (m_checkedUnchangedFiles.Count > 0)
                        signaturefile.WriteAllLines(UNMODIFIED_FILES, m_checkedUnchangedFiles.ToArray());

                    if (m_unproccesed.Symlinks.Count > 0)
                    {
                        foreach(KeyValuePair<string, string> kvp in m_unproccesed.Symlinks)
                        {
                            string target = FilenamesToPlatformIndependant(new string[] { kvp.Value })[0];
                            string source = Path.Combine(SYMLINK_ROOT, GetRelativeName(kvp.Key));
                            byte[] targetBytes = Encoding.UTF8.GetBytes(target);

                            contentfile.WriteAllBytes(source, targetBytes);
                            signaturefile.WriteAllBytes(source, targetBytes);
                        }
                        m_unproccesed.Symlinks.Clear();
                    }
                }

                m_finalized = true;
            }

            return m_finalized;
        }

        /// <summary>
        /// Creates a signature/content pair.
        /// Returns true when all files are processed.
        /// Returns false if there are still files to process.
        /// This method will only return false if the volumesize or remainingSize is exceeded.
        /// </summary>
        /// <param name="signaturefile">The signaure archive file</param>
        /// <param name="contentfile">The content archive file</param>
        /// <param name="volumesize">The max size of this volume</param>
        /// <param name="remainingSize">The max remaining size of arhive space</param>
        /// <returns>False if there are still files to process, true if all files are processed</returns>
        public bool MakeMultiPassDiff(Library.Interface.ICompression signaturefile, Library.Interface.ICompression contentfile, long volumesize)
        {
            if (m_unproccesed == null)
                throw new Exception(Strings.RSyncDir.MultipassUsageError);

            Random r = new Random();
            long totalSize = 0;

            //Insert the marker file
            contentfile.CreateFile(UTC_TIME_MARKER).Dispose();
            signaturefile.CreateFile(UTC_TIME_MARKER).Dispose();

            if (m_isfirstmultipass)
            {
                //We write these files to the very first volume
                if (m_deletedfolders.Count > 0)
                {
                    signaturefile.WriteAllLines(DELETED_FOLDERS, m_deletedfolders.ToArray());
                    contentfile.WriteAllLines(DELETED_FOLDERS, m_deletedfolders.ToArray());
                }

                if (m_newfolders.Count > 0)
                {
                    string[] folders = new string[m_newfolders.Count];
                    string[] timestamps = new string[m_newfolders.Count];

                    for (int i = 0; i < m_newfolders.Count; i++)
                    {
                        folders[i] = m_newfolders[i].Key;
                        timestamps[i] = ((long)((m_newfolders[i].Value - Utility.Utility.EPOCH).TotalSeconds)).ToString();
                    }

                    folders = FilenamesToPlatformIndependant(folders);

                    signaturefile.WriteAllLines(ADDED_FOLDERS, folders);
                    signaturefile.WriteAllLines(ADDED_FOLDERS_TIMESTAMPS, timestamps);
                    contentfile.WriteAllLines(ADDED_FOLDERS, folders);
                    contentfile.WriteAllLines(ADDED_FOLDERS_TIMESTAMPS, timestamps);
                }

                if (m_updatedfolders.Count > 0)
                {
                    string[] folders = new string[m_updatedfolders.Count];
                    string[] timestamps = new string[m_updatedfolders.Count];
                    for (int i = 0; i < m_updatedfolders.Count; i++)
                    {
                        folders[i] = m_updatedfolders[i].Key;
                        timestamps[i] = ((long)((m_updatedfolders[i].Value - Utility.Utility.EPOCH).TotalSeconds)).ToString();
                    }

                    folders = FilenamesToPlatformIndependant(folders);

                    signaturefile.WriteAllLines(UPDATED_FOLDERS, folders);
                    signaturefile.WriteAllLines(UPDATED_FOLDERS_TIMESTAMPS, timestamps);
                    contentfile.WriteAllLines(UPDATED_FOLDERS, folders);
                    contentfile.WriteAllLines(UPDATED_FOLDERS_TIMESTAMPS, timestamps);
                }

                m_isfirstmultipass = false;
            }

            //Last update was a looong time ago
            DateTime nextProgressEvent = DateTime.Now.AddYears(-1);

            if (m_lastPartialFile != null)
            {
                if (ProgressEvent != null)
                {
                    int pg = 100 - ((int)((m_unproccesed.Files.Count / (double)m_totalfiles) * 100));
                    nextProgressEvent = DateTime.Now + PROGRESS_TIMESPAN;
                    ProgressEvent(pg, m_lastPartialFile.fullname);
                }

                m_lastPartialFile = WritePossiblePartial(m_lastPartialFile, contentfile, signaturefile, volumesize);
            }


            while (m_unproccesed.Files.Count > 0)
            {
                if (m_lastPartialFile != null)
                    return false;

                if (totalSize >= volumesize)
                    break;

                int next = m_sortedfilelist ? 0 : r.Next(0, m_unproccesed.Files.Count);
                string s = m_unproccesed.Files[next];
                m_unproccesed.Files.RemoveAt(next);

                if (ProgressEvent != null && DateTime.Now > nextProgressEvent)
                {
                    int pg = 100 - ((int)((m_unproccesed.Files.Count / (double)m_totalfiles) * 100));
                    nextProgressEvent = DateTime.Now + PROGRESS_TIMESPAN;
                    ProgressEvent(pg, s);
                }

                try
                {
                    if (!m_disableFiletimeCheck)
                    {
                        //TODO: Make this check faster somehow
                        string relpath = GetRelativeName(s);
                        if (m_oldSignatures.ContainsKey(relpath))
                        {
                            try
                            {
                                //Reports show that the file time can be missing :(
                                DateTime lastFileWrite = m_snapshot.GetLastWriteTime(s).ToUniversalTime();
                                //Cut off as we only preserve precision in seconds after compression
                                lastFileWrite = new DateTime(lastFileWrite.Year, lastFileWrite.Month, lastFileWrite.Day, lastFileWrite.Hour, lastFileWrite.Minute, lastFileWrite.Second, DateTimeKind.Utc);

                                DateTime lastCheck;
                                if (!m_lastVerificationTime.TryGetValue(relpath, out lastCheck))
                                    lastCheck = m_oldSignatures[relpath].CreateTime;

                                //Compare with the modification time of the last known check time
                                if (lastFileWrite <= lastCheck)
                                {
                                    m_oldSignatures.Remove(relpath);
                                    m_examinedfiles++;
                                    continue;
                                }
                            }
                            catch (Exception ex)
                            {
                                Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.InvalidTimeStampError, s, ex.Message), Duplicati.Library.Logging.LogMessageType.Warning, ex);
                            }
                        }
                    }

                    if (m_unproccesed.Errors.Count > 0 && m_unproccesed.IsAffectedByError(s))
                        m_unproccesed.FilesWithError.Add(s);
                    else
                    {
                        System.IO.Stream fs = null;
                        try
                        {
                            bool isLockedStream = false;

                            m_filesopened++;
                            //We cannot have a "using" directive here because the fs may need to survive multiple rounds
                            try { fs = m_snapshot.OpenRead(s); }
                            catch
                            {
                                if (m_snapshot is Snapshots.NoSnapshot && m_openfilepolicy != Options.OpenFileStrategy.Ignore)
                                {
                                    try { fs = ((Snapshots.NoSnapshot)m_snapshot).OpenLockedRead(s); }
                                    catch { }

                                    //Rethrow original error
                                    if (fs == null)
                                        throw;

                                    isLockedStream = true;
                                }
                                else
                                    throw;
                            }


                            DateTime lastWrite = Utility.Utility.EPOCH;
                            try 
                            {
                                //Record the change time after we opened (and thus locked) the file
                                lastWrite = m_snapshot.GetLastWriteTime(s).ToUniversalTime();
                                //Cut off as we only preserve precision in seconds
                                lastWrite = new DateTime(lastWrite.Year, lastWrite.Month, lastWrite.Day, lastWrite.Hour, lastWrite.Minute, lastWrite.Second, DateTimeKind.Utc);
                            }
                            catch (Exception ex)
                            {
                                Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.InvalidTimeStampError, s, ex.Message), Duplicati.Library.Logging.LogMessageType.Warning, ex);
                            }


                            if (fs.Length > m_maxFileSize)
                            {
                                m_unproccesed.FilesTooLarge.Add(s);
                            }
                            else
                            {
                                //If the file is > 10mb, update the display to show the file being processed
                                if (ProgressEvent != null && fs.Length > 1024 * 1024 * 10)
                                {
                                    int pg = 100 - ((int)((m_unproccesed.Files.Count / (double)m_totalfiles) * 100));
                                    nextProgressEvent = DateTime.Now + PROGRESS_TIMESPAN;
                                    ProgressEvent(pg, s);
                                }

                                System.IO.Stream signature = ProccessDiff(fs, s, signaturefile);
                                if (signature == null)
                                {
                                    //If we had to check the file, it's timestamp was modified, so we record that the file is still unchanged
                                    // so we can avoid checking the next time
                                    if (!m_disableFiletimeCheck)
                                        m_checkedUnchangedFiles.Add(GetRelativeName(s));

                                    //TODO: If the file timestamp was changed AFTER the backup started, we will record it in this and the next backup.
                                    //      This can be avoided, but only happens if the file was not modified, so it will happen rarely
                                }
                                else
                                {
                                    System.IO.Stream originalSignature = null;

                                    //If the stream was locked, we hijack it here to ensure that the signature recorded
                                    // matches the file data being read
                                    if (isLockedStream)
                                    {
                                        if (m_openfilepolicy == Options.OpenFileStrategy.Copy)
                                        {
                                            using (MemoryStream newSig = new MemoryStream())
                                            {
                                                fs.Position = 0;
                                                using (SharpRSync.ChecksumGeneratingStream ts = new SharpRSync.ChecksumGeneratingStream(newSig, fs))
                                                {
                                                    newSig.Capacity = Math.Max(ts.BytesGeneratedForSignature((long)(fs.Length * FILESIZE_GROW_MARGIN_MULTIPLIER)), newSig.Capacity);
                                                    fs = new Utility.TempFileStream();
                                                    Utility.Utility.CopyStream(ts, fs, false);
                                                }

                                                fs.Position = 0;
                                                signature.Position = 0;
                                                newSig.Position = 0;

                                                if (!Utility.Utility.CompareStreams(signature, newSig, true))
                                                    throw new Exception(string.Format(Strings.RSyncDir.FileChangedWhileReadError, s));

                                                signature.Position = 0;
                                            }
                                        }
                                        else
                                        {
                                            //Keep a copy of the original signature for change detection
                                            originalSignature = signature;

                                            //Set up for a new round
                                            signature = new System.IO.MemoryStream();
                                            fs.Position = 0;
                                            long filelen = fs.Length;
                                            fs = new SharpRSync.ChecksumGeneratingStream(signature, fs);
                                            ((MemoryStream)signature).Capacity = Math.Max(((SharpRSync.ChecksumGeneratingStream)fs).BytesGeneratedForSignature(filelen), ((MemoryStream)signature).Capacity);
                                        }
                                    }

                                    totalSize = AddFileToCompression(fs, s, signature, contentfile, signaturefile, volumesize, lastWrite);
                                    
                                    //If this turned into a partial full entry, we must keep the file open.
                                    //The file will be closed when m_lastPartialFile is disposed
                                    if (m_lastPartialFile != null)
                                    {
                                        m_lastPartialFile.OriginalSignatureStream = originalSignature;
                                        if (m_lastPartialFile.Stream == fs)
                                            fs = null;
                                    }
                                }
                            }
                        }
                        finally
                        {
                            try
                            {
                                if (fs != null)
                                    fs.Dispose();
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (m_stat != null)
                        m_stat.LogError(string.Format(Strings.RSyncDir.FileProcessError, s, ex.Message), ex);
                    Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.FileProcessError, s, ex.Message), Duplicati.Library.Logging.LogMessageType.Error, ex);
                    m_unproccesed.FilesWithError.Add(s);
                }
            }

            if (m_unproccesed.Files.Count == 0 && m_lastPartialFile == null)
                return FinalizeMultiPass(signaturefile, contentfile, volumesize);
            else
                return false;
        }

        /// <summary>
        /// Generates a signature stream for the given file and compares it with an existing signature stream, if one exists.
        /// </summary>
        /// <param name="fs">The file to examine, the filestream should be at position 0</param>
        /// <param name="s">The full name of the file</param>
        /// <param name="signaturefile">The signature archive file</param>
        /// <returns>The signature stream if the file is new or modified, null if the file has not been modified</returns>
        private System.IO.Stream ProccessDiff(System.IO.Stream fs, string s, Library.Interface.ICompression signaturefile)
        {
            string relpath = GetRelativeName(s);

            m_examinedfilesize += fs.Length;
            m_examinedfiles++;

            System.IO.MemoryStream ms = new MemoryStream();
            SharpRSync.ChecksumFileWriter ws = new Duplicati.Library.SharpRSync.ChecksumFileWriter(ms);

            //Expand the memorystream to contain all bytes, which avoids re-allocation
            ms.Capacity = Math.Max(ws.BytesGeneratedForSignature((long)(fs.Length * FILESIZE_GROW_MARGIN_MULTIPLIER)), ms.Capacity);
            ws.AddStream(fs);
            ms.Position = 0;

            if (!m_oldSignatures.ContainsKey(relpath))
            {
                m_newfiles.Add(s, null);
                return ms;
            }
            else
            {
                bool equals;
                //File exists in archive, check if signature has changed
                using (System.IO.Stream s2 = m_oldSignatures[relpath].OpenRead(relpath))
                    equals = Utility.Utility.CompareStreams(s2, ms, false);

                ms.Position = 0;

                if (!equals)
                {
                    //Yes, changed, return the signature stream
                    m_modifiedFiles.Add(s, null);
                    return ms;
                }
                else
                {
                    //No changes, discard the signature stream
                    ms.Close();
                    ms.Dispose();
                    m_oldSignatures.Remove(relpath);
                    return null;
                }
            }
        }

        /// <summary>
        /// Appends a file to the content and signature archives.
        /// If the file existed, a delta file is added, otherwise the entire file is added.
        /// </summary>
        /// <param name="fs">The file to add</param>
        /// <param name="s">The full name of the file</param>
        /// <param name="signature">The signature archive file</param>
        /// <param name="contentfile">The content archive file</param>
        /// <param name="signaturefile">The signature stream to add</param>
        /// <param name="volumesize">The max size of the volume</param>
        /// <param name="lastWrite">The time the source file was last written</param>
        /// <returns>The current size of the content archive</returns>
        private long AddFileToCompression(System.IO.Stream fs, string s, System.IO.Stream signature, Library.Interface.ICompression contentfile, Library.Interface.ICompression signaturefile, long volumesize, DateTime lastWrite)
        {
            fs.Position = 0;
            string relpath = GetRelativeName(s);

            if (m_modifiedFiles.ContainsKey(s))
            {
                //Existing file, write the delta file
                string target = System.IO.Path.Combine(DELTA_ROOT, relpath);
                string signaturepath = System.IO.Path.Combine(DELTA_SIGNATURE_ROOT, relpath);

                using (System.IO.Stream sigfs = m_oldSignatures[relpath].OpenRead(relpath))
                {
                    long lbefore = contentfile.Size;

                    Utility.TempFileStream deltaTemp = null;
                    try
                    {
                        deltaTemp = new Duplicati.Library.Utility.TempFileStream();
                        SharpRSync.Interface.GenerateDelta(sigfs, fs, deltaTemp);
                        deltaTemp.Position = 0;

                        m_lastPartialFile = WritePossiblePartial(new PartialFileEntry(deltaTemp, target, s, 0, signature, signaturepath, lastWrite), contentfile, signaturefile, volumesize);
                    }
                    catch
                    {
                        if (deltaTemp != null)
                            deltaTemp.Dispose();
                        throw;
                    }

                    m_diffsize += contentfile.Size - lbefore;
                    m_diffedfilessize += fs.Length;
                    m_diffedfiles++;
                }

                m_modifiedFiles.Remove(s);
                m_oldSignatures.Remove(relpath);
            }
            else
            {
                //New file, write as content
                string signaturepath = System.IO.Path.Combine(CONTENT_SIGNATURE_ROOT, relpath);
                string target = System.IO.Path.Combine(CONTENT_ROOT, relpath);

                long size = fs.Length;
                m_lastPartialFile = WritePossiblePartial(new PartialFileEntry(fs, target, s, 0, signature, signaturepath, lastWrite), contentfile, signaturefile, volumesize);
                
                m_addedfiles++;
                m_addedfilessize += size;

                m_newfiles.Remove(s);
            }

            return contentfile.Size;
        }

        /// <summary>
        /// Appends a file to the content and signature archives, watching the content archive file size.
        /// Returns the partial file entry if the volume size was exceeded. 
        /// Returns null if the file was written entirely.
        /// </summary>
        /// <param name="entry">The entry that describes the partial file</param>
        /// <param name="contentfile">The content archive file</param>
        /// <param name="signaturefile">The signature archive file</param>
        /// <param name="volumesize">The max allowed volumesize</param>
        /// <returns>The partial file entry if the volume size was exceeded. Returns null if the file was written entirely.</returns>
        private PartialFileEntry WritePossiblePartial(PartialFileEntry entry, Library.Interface.ICompression contentfile, Library.Interface.ICompression signaturefile, long volumesize)
        {
            long startPos = entry.Stream.Position;

            //Protect against writing this file if there is not enough space to hold the INCOMPLETE_FILE
            if (startPos == 0 && contentfile.Size + contentfile.FlushBufferSize + (entry.ExtraSize * 2) > volumesize)
                return entry;

            PartialFileEntry pe = WritePossiblePartialInternal(entry, contentfile, volumesize);

            if (pe != null)
            {
                //The record is (still) partial
                string[] tmplines = new PartialEntryRecord(entry.relativeName, startPos, entry.Stream.Position - startPos, entry.Stream.Length).Serialize();
                contentfile.WriteAllLines(INCOMPLETE_FILE, tmplines);
                signaturefile.WriteAllLines(INCOMPLETE_FILE, tmplines);

                //If we are debugging, this can be nice to have
                Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.PartialFileAddedLogMessage, entry.relativeName, startPos), Duplicati.Library.Logging.LogMessageType.Information);
            }
            else
            {
                //If the file was partial before, mark the file as completed
                if (startPos != 0)
                {
                    string[] tmplines = new PartialEntryRecord(entry.relativeName, startPos, entry.Stream.Position - startPos, entry.Stream.Length).Serialize();
                    contentfile.WriteAllLines(COMPLETED_FILE, tmplines);
                    signaturefile.WriteAllLines(COMPLETED_FILE, tmplines);
                }

                //Add signature AFTER content is completed.
                //If content is present, it is restoreable, if signature is missing, file will be backed up on next run
                //If signature is present, but not content, the entire differential sequence will be unable to recover the file
                if (!entry.DumpSignature(signaturefile))
                {
                    if (m_stat != null)
                        m_stat.LogWarning(string.Format(Strings.RSyncDir.FileChangedWhileReadWarning, entry.fullname), null);
                }

                entry.Dispose();
            }
            return pe;
        }

        /// <summary>
        /// Appends a file to the content archive, watching the content archive file size.
        /// Does not record anything in either content or signature volumes
        /// Returns the partial file entry if the volume size was exceeded. 
        /// Returns null if the file was written entirely.
        /// </summary>
        /// <param name="entry">The entry that describes the partial file</param>
        /// <param name="contentfile">The content archive file</param>
        /// <param name="volumesize">The max allowed volumesize</param>
        /// <returns>The partial file entry if the volume size was exceeded. Returns null if the file was written entirely.</returns>
        private PartialFileEntry WritePossiblePartialInternal(PartialFileEntry entry, Library.Interface.ICompression contentfile, long volumesize)
        {
            //append chuncks of 1kb, checking on the total size after each write
            byte[] tmp = new byte[1024];
            using (System.IO.Stream s3 = contentfile.CreateFile(entry.relativeName, entry.LastWriteTime))
            {
                int a;
                while ((a = entry.Stream.Read(tmp, 0, tmp.Length)) != 0)
                {
                    s3.Write(tmp, 0, a);
                    if (contentfile.Size + contentfile.FlushBufferSize + entry.ExtraSize + tmp.Length > volumesize)
                        return entry;
                }
            }

            return null;
        }

        public bool HasChanges { get { return m_newfiles.Count > 0 || m_modifiedFiles.Count > 0; } }

        /// <summary>
        /// Restores all files to the destination, given a set of content files.
        /// This function is not used by Duplicati.
        /// </summary>
        /// <param name="destination">The destination to restore to</param>
        /// <param name="patches">The list of patches (content files) to process</param>
        public void Restore(string[] destination, List<Library.Interface.ICompression> patches)
        {
            if (patches != null)
            {
                foreach (Library.Interface.ICompression s in patches)
                    Patch(destination, s);

                FinalizeRestore();
            }
        }

        /// <summary>
        /// A function that makes sure that empty leftover folders are deleted, 
        /// and that there are no leftover partial files from the patches.
        /// Call this function after calling the Patch function with all patches.
        /// </summary>
        public void FinalizeRestore()
        {
            Snapshots.ISystemIO SystemIO = Utility.Utility.IsClientLinux ? (Snapshots.ISystemIO)new Snapshots.SystemIOLinux() : (Snapshots.ISystemIO)new Snapshots.SystemIOWindows();
            if (m_folders_to_delete != null)
            {
                foreach (string s in m_folders_to_delete)
                {
                    if (SystemIO.DirectoryExists(s))
                        try
                        {
                            //TODO: Perhaps read ahead in patches to prevent creation
                            SystemIO.DirectoryDelete(s, false);
                            if (m_stat as RestoreStatistics != null)
                            {
                                (m_stat as RestoreStatistics).FoldersDeleted++;
                                (m_stat as RestoreStatistics).FoldersRestored--;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (m_stat != null)
                                m_stat.LogError(string.Format(Strings.RSyncDir.DeleteFolderError, s, ex.Message), ex);
                            Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.DeleteFolderError, s, ex.Message), Duplicati.Library.Logging.LogMessageType.Warning, ex);
                        }
                    else
                        Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.FolderToDeleteMissingError, s), Duplicati.Library.Logging.LogMessageType.Warning);
                }
                m_folders_to_delete = null;
            }

            if (m_partialDeltas != null)
            {
                foreach (KeyValuePair<string, Utility.TempFile> tf in m_partialDeltas)
                {
                    Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.PartialFileIncompleteWarning, tf.Key), Duplicati.Library.Logging.LogMessageType.Warning);
                    try
                    {
                        tf.Value.Protected = false;
                        tf.Value.Dispose();
                    }
                    catch (Exception ex)
                    {
                        string logmessage = string.Format(Strings.RSyncDir.PartialLeftoverDeleteError, tf.Key, ex.Message);
                        Logging.Log.WriteMessage(logmessage, Duplicati.Library.Logging.LogMessageType.Error, ex);
                        if (m_stat != null)
                            m_stat.LogError(logmessage, ex);
                    }
                }
                m_partialDeltas = null;
            }

            if (m_folderTimestamps != null)
                foreach (KeyValuePair<string, DateTime> t in m_folderTimestamps)
                    if (SystemIO.DirectoryExists(t.Key))
                        try { SystemIO.DirectorySetLastWriteTimeUtc(t.Key, t.Value); }
                        catch (Exception ex)
                        {
                            m_stat.LogWarning(string.Format(Strings.RSyncDir.FailedToSetFolderWriteTime, t.Key, ex.Message), ex);
                        }

            m_folderTimestamps = null;
        }

        /// <summary>
        /// Internal helper class to filter files during a partial restore
        /// </summary>
        private class FilterHelper
        {
            /// <summary>
            /// A copy of the filter to use
            /// </summary>
            private Utility.FilenameFilter m_filter;

            /// <summary>
            /// The list of folders to restore to
            /// </summary>
            private string[] m_folders;

            /// <summary>
            /// The parent folder
            /// </summary>
            private RSyncDir m_parent;

            /// <summary>
            /// Constructs a new filter helper
            /// </summary>
            /// <param name="folders">The restore folder list</param>
            /// <param name="filter">The filter to apply to the files</param>
            public FilterHelper(RSyncDir parent, string[] folders, Utility.FilenameFilter filter)
            {
                m_parent = parent;
                m_folders  = folders;
                m_filter = filter;
            }

            /// <summary>
            /// A filter predicate used to filter unwanted elements from the list
            /// </summary>
            /// <param name="element">The relative string to examine</param>
            /// <returns>True if the element is accepted, false if it is filtered</returns>
            private bool FilterPredicate(string element)
            {
                return m_filter.ShouldInclude(Utility.Utility.DirectorySeparatorString, Utility.Utility.DirectorySeparatorString + element);
            }

            /// <summary>
            /// A filter predicate used to filter unwanted folder elements from the list
            /// </summary>
            /// <param name="element">The relative string to examine</param>
            /// <returns>True if the folder element is accepted, false if it is filtered</returns>
            private bool FilterFoldersPredicate(string element)
            {
                return m_filter.ShouldInclude(Utility.Utility.DirectorySeparatorString, Utility.Utility.DirectorySeparatorString + Utility.Utility.AppendDirSeparator(element));
            }

            /// <summary>
            /// A conversion delegate used to transform relative names into full names
            /// </summary>
            /// <param name="input">The relative filename</param>
            /// <returns>The full path</returns>
            private string GetFullpathFunc(string input)
            {
                return RSyncDir.GetFullPathFromRelname(m_folders, input);
            }

            /// <summary>
            /// Filters a list by hooking into the enumeration system, rather than copying the list
            /// </summary>
            /// <param name="input">The list of relative filenames to filter</param>
            /// <param name="isFolderList">True if all elements should be interpreted as folders, false otherwise</param>
            /// <returns>A filtered list</returns>
            public IEnumerable<string> Filterlist(IEnumerable<string> input, bool isFolderList)
            {
                return new Utility.PlugableEnumerable<string>(isFolderList ? new Predicate<string>(FilterFoldersPredicate) : new Predicate<string>(FilterPredicate), new Utility.Func<string, string>(GetFullpathFunc), input);
            }
        }

        /// <summary>
        /// Applies a patch (content file) to the destination
        /// </summary>
        /// <param name="destination">The destination that contains the previous version of the data</param>
        /// <param name="patch">The content file that the destination is patched with</param>
        public void Patch(string[] destination, Library.Interface.ICompression patch)
        {
            Snapshots.ISystemIO SystemIO = Utility.Utility.IsClientLinux ? (Snapshots.ISystemIO)new Snapshots.SystemIOLinux() : (Snapshots.ISystemIO)new Snapshots.SystemIOWindows();

            if (m_partialDeltas == null)
                m_partialDeltas = new Dictionary<string, Duplicati.Library.Utility.TempFile>();

            if (m_folderTimestamps == null)
                m_folderTimestamps = new Dictionary<string, DateTime>();

            for (int i = 0; i < destination.Length; i++)
                destination[i] = Utility.Utility.AppendDirSeparator(destination[i]);

            bool isUtc = patch.FileExists(UTC_TIME_MARKER);

            //Set up the filter system to avoid dealing with filtered items
            FilterHelper fh = new FilterHelper(this, destination, m_filter);

            //Delete all files that were removed
            if (patch.FileExists(DELETED_FILES))
                foreach (string s in fh.Filterlist(FilenamesFromPlatformIndependant(patch.ReadAllLines(DELETED_FILES)), false))
                {
                    if (SystemIO.FileExists(s))
                    {
                        try
                        {
                            //TODO: Perhaps read ahead in patches to prevent creation
                            long size = SystemIO.FileLength(s);

                            SystemIO.FileDelete(s);
                            if (m_stat as RestoreStatistics != null)
                            {
                                (m_stat as RestoreStatistics).FilesRestored--;
                                (m_stat as RestoreStatistics).SizeOfRestoredFiles -= size;
                                (m_stat as RestoreStatistics).FilesDeleted++;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (m_stat != null)
                                m_stat.LogError(string.Format(Strings.RSyncDir.DeleteFileError, s, ex.Message), ex);
                            Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.DeleteFileError, s, ex.Message), Duplicati.Library.Logging.LogMessageType.Warning, ex);
                        }
                    }
                    else
                    {
                        Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.FileToDeleteMissingError, s), Duplicati.Library.Logging.LogMessageType.Warning);
                    }
                }

            //Delete all folders that were removed
            if (patch.FileExists(DELETED_FOLDERS))
            {
                if (m_folders_to_delete == null)
                    m_folders_to_delete = new List<string>();
                List<string> deletedfolders = new List<string>(fh.Filterlist(FilenamesFromPlatformIndependant(patch.ReadAllLines(DELETED_FOLDERS)), true));
                //Make sure subfolders are deleted first
                deletedfolders.Sort();
                deletedfolders.Reverse();

                //Append to the list of folders to remove.
                //The folders are removed when the patch sequence is finalized,
                //because the deleted file list is not present until
                //the last content file has been applied.
                m_folders_to_delete.AddRange(deletedfolders);
            }

            //Add folders. This mainly applies to empty folders, 
            //as non-empty folders will also be created when files are restored
            if (patch.FileExists(ADDED_FOLDERS))
            {
                List<string> addedfolders = new List<string>(fh.Filterlist(FilenamesFromPlatformIndependant(patch.ReadAllLines(ADDED_FOLDERS)), true));

                //Make sure topfolders are created first
                addedfolders.Sort();

                foreach (string s in addedfolders)
                {
                    if (!SystemIO.DirectoryExists(s))
                        try
                        {
                            SystemIO.DirectoryCreate(s);
                            if (m_stat as RestoreStatistics != null)
                                (m_stat as RestoreStatistics).FoldersRestored++;
                        }
                        catch (Exception ex)
                        {
                            if (m_stat != null)
                                m_stat.LogError(string.Format(Strings.RSyncDir.CreateFolderError, s, ex.Message), ex);
                            Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.CreateFolderError, s, ex.Message), Duplicati.Library.Logging.LogMessageType.Warning, ex);
                        }
                }
            }

            if (patch.FileExists(ADDED_FOLDERS_TIMESTAMPS))
            {
                //These times are always utc
                string[] folders = FilenamesFromPlatformIndependant(patch.ReadAllLines(ADDED_FOLDERS));
                string[] timestamps = patch.ReadAllLines(ADDED_FOLDERS_TIMESTAMPS);

                for (int i = 0; i < folders.Length; i++)
                    m_folderTimestamps[RSyncDir.GetFullPathFromRelname(destination, folders[i])] = Utility.Utility.EPOCH.AddSeconds(long.Parse(timestamps[i]));
            }

            if (patch.FileExists(UPDATED_FOLDERS) && patch.FileExists(UPDATED_FOLDERS_TIMESTAMPS))
            {
                //These times are always utc
                string[] folders = FilenamesFromPlatformIndependant(patch.ReadAllLines(UPDATED_FOLDERS));
                string[] timestamps = patch.ReadAllLines(UPDATED_FOLDERS_TIMESTAMPS);
                long l;

                for (int i = 0; i < folders.Length; i++)
                    if (long.TryParse(timestamps[i], out l))
                        m_folderTimestamps[RSyncDir.GetFullPathFromRelname(destination, folders[i])] = Utility.Utility.EPOCH.AddSeconds(l);
            }

            PartialEntryRecord pe = null;
            if (patch.FileExists(INCOMPLETE_FILE))
                pe = new PartialEntryRecord(patch.ReadAllLines(INCOMPLETE_FILE));
            
            PartialEntryRecord fe = null;
            if (patch.FileExists(COMPLETED_FILE))
                fe = new PartialEntryRecord(patch.ReadAllLines(COMPLETED_FILE));

            int lastPg = -1;

            string contentprefix = Utility.Utility.AppendDirSeparator(CONTENT_ROOT);
            List<string> contentfiles = m_filter.FilterList(contentprefix, patch.ListFiles(contentprefix));

            string deltaprefix = Utility.Utility.AppendDirSeparator(DELTA_ROOT);
            List<string> deltafiles = m_filter.FilterList(deltaprefix, patch.ListFiles(deltaprefix));

            string symlinkprefix = Utility.Utility.AppendDirSeparator(SYMLINK_ROOT);
            List<string> symlinks = m_filter.FilterList(symlinkprefix, patch.ListFiles(symlinkprefix));

            long totalfiles = deltafiles.Count + contentfiles.Count;
            long fileindex = 0;

            //Restore new files
            foreach (string s in contentfiles)
            {
                string target = GetFullPathFromRelname(destination, s.Substring(contentprefix.Length));
                try
                {
                    if (!SystemIO.DirectoryExists(SystemIO.PathGetDirectoryName(target)))
                    {
                        Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.RestoreFolderMissingError, target), Duplicati.Library.Logging.LogMessageType.Warning);
                        SystemIO.DirectoryCreate(SystemIO.PathGetDirectoryName(target));
                    }

                    //Update each 0.5%
                    int pg = (int)((fileindex / (double)totalfiles) * 200);
                    if (pg != lastPg)
                    {
                        ProgressEvent(pg / 2, target);
                        lastPg = pg;
                    }

                    using (System.IO.Stream s1 = patch.OpenRead(s))
                    {
                        PartialEntryRecord pex = null;
                        Utility.TempFile partialFile = null;

                        if (pe != null && string.Equals(pe.PlatformConvertedFilename, s))
                            pex = pe; //The file is incomplete
                        else if (fe != null && string.Equals(fe.PlatformConvertedFilename, s))
                            pex = fe; //The file has the final segment

                        if (pex != null && string.Equals(pex.PlatformConvertedFilename, s))
                        {
                            //Ensure that the partial file list is in the correct state
                            if (pex.StartOffset == 0 && m_partialDeltas.ContainsKey(s))
                                throw new Exception(string.Format(Strings.RSyncDir.InvalidPartialFileEntry, s));
                            else if (pex.StartOffset != 0 && !m_partialDeltas.ContainsKey(s))
                                throw new Exception(string.Format(Strings.RSyncDir.InvalidPartialFileEntry, s));
                            else if (pex.StartOffset == 0) //First entry, so create a temp file
                                m_partialDeltas.Add(s, new Duplicati.Library.Utility.TempFile());

                            partialFile = m_partialDeltas[s];
                        }
                        else if (m_partialDeltas.ContainsKey(s))
                            throw new Exception(string.Format(Strings.RSyncDir.FileShouldBePartialError, s));

                        long startOffset = pex == null ? 0 : pex.StartOffset;
                        using (System.IO.Stream s2 = SystemIO.FileOpenWrite(partialFile == null ? target : (string)partialFile))
                        {
                            if (s2.Length != startOffset)
                                throw new Exception(string.Format(Strings.RSyncDir.InvalidPartialFileEntry, s));

                            s2.Position = startOffset;
                            if (startOffset == 0)
                                s2.SetLength(0);

                            Utility.Utility.CopyStream(s1, s2);
                        }

                        if (pex != null && pex == fe)
                        {
                            if (SystemIO.FileExists(target))
                                SystemIO.FileDelete(target);
                            SystemIO.FileMove(partialFile, target);
                            partialFile.Dispose();
                            m_partialDeltas.Remove(s);
                        }

                        if (m_stat is RestoreStatistics && (partialFile == null || pex == fe))
                        {
                            (m_stat as RestoreStatistics).FilesRestored++;
                            (m_stat as RestoreStatistics).SizeOfRestoredFiles += SystemIO.FileLength(target);
                        }
                    }

                    if (File.Exists(target))
                    {
                        DateTime t = patch.GetLastWriteTime(s);
                        if (!isUtc)
                            t = t.ToUniversalTime();
                        try { SystemIO.FileSetLastWriteTimeUtc(target, t); }
                        catch (Exception ex)
                        {
                            if (m_stat != null)
                                m_stat.LogWarning(string.Format(Strings.RSyncDir.FailedToSetFileWriteTime, target, ex.Message), ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (m_stat != null)
                        m_stat.LogError(string.Format(Strings.RSyncDir.RestoreFileError, s, ex.Message), ex);
                    Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.RestoreFileError, s, ex.Message), Duplicati.Library.Logging.LogMessageType.Error, ex);
                }
                fileindex++;
            }

            //Patch modfied files
            foreach (string s in deltafiles)
            {
                string target = GetFullPathFromRelname(destination, s.Substring(deltaprefix.Length));
                try
                {
                    //Update each 0.5%
                    int pg = (int)((fileindex / (double)totalfiles) * 200);
                    if (pg != lastPg)
                    {
                        ProgressEvent(pg / 2, target);
                        lastPg = pg;
                    }

                    if (!SystemIO.DirectoryExists(SystemIO.PathGetDirectoryName(target)))
                    {
                        Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.RestoreFolderDeltaError, target), Duplicati.Library.Logging.LogMessageType.Warning);
                        SystemIO.DirectoryCreate(SystemIO.PathGetDirectoryName(target));
                    }

                    PartialEntryRecord pex = null;
                    if (pe != null && string.Equals(pe.PlatformConvertedFilename, s))
                        pex = pe; //The file is incomplete
                    else if (fe != null && string.Equals(fe.PlatformConvertedFilename, s))
                        pex = fe; //The file has the final segment

                    Utility.TempFile tempDelta = null;

                    if (pex != null && string.Equals(pex.PlatformConvertedFilename, s))
                    {
                        //Ensure that the partial file list is in the correct state
                        if (pex.StartOffset == 0 && m_partialDeltas.ContainsKey(s))
                            throw new Exception(string.Format(Strings.RSyncDir.InvalidPartialFileEntry, s));
                        else if (pex.StartOffset != 0 && !m_partialDeltas.ContainsKey(s))
                            throw new Exception(string.Format(Strings.RSyncDir.InvalidPartialFileEntry, s));
                        else if (pex.StartOffset == 0) //First entry, so create a temp file
                            m_partialDeltas.Add(s, new Duplicati.Library.Utility.TempFile());

                        //Dump the content in the temp file at the specified offset
                        using (System.IO.Stream st = SystemIO.FileOpenWrite(m_partialDeltas[s]))
                        {
                            if (st.Length != pex.StartOffset)
                                throw new Exception(string.Format(Strings.RSyncDir.InvalidPartialFileEntry, s));
                            st.Position = pex.StartOffset;
                            using (System.IO.Stream s2 = patch.OpenRead(s))
                                Utility.Utility.CopyStream(s2, st);
                        }

                        //We can't process it until it is received completely
                        if (pex != fe)
                            continue;

                        tempDelta = m_partialDeltas[s];
                        m_partialDeltas.Remove(s);
                    }
                    else if (m_partialDeltas.ContainsKey(s))
                        throw new Exception(string.Format(Strings.RSyncDir.FileShouldBePartialError, s));


                    using (Utility.TempFile tempfile = new Utility.TempFile())
                    using (tempDelta) //May be null, but the using directive does not care
                    {
                        //Use either the patch directly, or the partial temp file
                        System.IO.Stream deltaStream = tempDelta == null ? patch.OpenRead(s) : SystemIO.FileOpenRead(tempDelta);
                        using (System.IO.Stream s2 = deltaStream)
                        using (System.IO.Stream s1 = SystemIO.FileOpenRead(target))
                        using (System.IO.Stream s3 = SystemIO.FileCreate(tempfile))
                            SharpRSync.Interface.PatchFile(s1, s2, s3);

                        if (m_stat as RestoreStatistics != null)
                        {
                            (m_stat as RestoreStatistics).SizeOfRestoredFiles -= SystemIO.FileLength(target);
                            (m_stat as RestoreStatistics).SizeOfRestoredFiles += SystemIO.FileLength(tempfile);
                            (m_stat as RestoreStatistics).FilesPatched++;
                        }

                        SystemIO.FileDelete(target);

                        try { SystemIO.FileMove(tempfile, target); }
                        catch
                        {
                            //The OS sometimes reports the file as existing even after a delete
                            // this seems to be related to MS Security Essentials?
                            System.Threading.Thread.Sleep(500);
                            SystemIO.FileMove(tempfile, target);
                        }
                    }

                    if (File.Exists(target))
                    {
                        DateTime t = patch.GetLastWriteTime(s);
                        if (!isUtc)
                            t = t.ToUniversalTime();
                        
                        try { SystemIO.FileSetLastWriteTimeUtc(target, t); }
                        catch (Exception ex)
                        {
                            if (m_stat != null)
                                m_stat.LogWarning(string.Format(Strings.RSyncDir.FailedToSetFileWriteTime, target, ex.Message), ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (m_stat != null)
                        m_stat.LogError(string.Format(Strings.RSyncDir.RestoreFileError, s, ex.Message), ex);
                    Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.RestoreFileError, s, ex.Message), Duplicati.Library.Logging.LogMessageType.Error, ex);

                    try { SystemIO.FileDelete(target); }
                    catch { }
                }
                fileindex++;
            }

            //Re-create symlinks (no progress report here, should be really fast)
            foreach (string s in symlinks)
            {
                string target = GetFullPathFromRelname(destination, s.Substring(symlinkprefix.Length));
                string symlinktarget = "";
                try
                {
                    symlinktarget = FilenamesFromPlatformIndependant(new string[] { Encoding.UTF8.GetString(patch.ReadAllBytes(s)) })[0];
                    bool isDir = symlinktarget[symlinktarget.Length - 1] == Path.DirectorySeparatorChar;
                    if (isDir)
                        symlinktarget = symlinktarget.Substring(0, symlinktarget.Length - 1);

                    try 
                    {
                        //In case another symlink is present, we "update" it
                        if (SystemIO.FileExists(target) && (SystemIO.GetFileAttributes(target) & FileAttributes.ReparsePoint) != 0)
                            SystemIO.FileDelete(target);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.RestoreFileError, s, ex.Message), Duplicati.Library.Logging.LogMessageType.Error, ex);
                    }

                    SystemIO.CreateSymlink(target, symlinktarget, isDir);
                }
                catch (Exception ex)
                {
                    if (m_stat != null)
                        m_stat.LogError(string.Format(Strings.RSyncDir.RestoreFileError, s, ex.Message), ex);
                    Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.RestoreFileError, s, ex.Message), Duplicati.Library.Logging.LogMessageType.Error, ex);

                    try { SystemIO.FileDelete(target); }
                    catch { }

                    try 
                    {
                        if (!string.IsNullOrEmpty(symlinktarget))
                            using (System.IO.StreamWriter sw = new System.IO.StreamWriter(SystemIO.FileOpenWrite(target)))
                                sw.Write(symlinktarget);
                    }
                    catch
                    {
                    }
                }
            }

        }


        public bool AnyChangesFound
        {
            get
            {
                long count = 0;
                if (m_deletedfiles != null)
                    count += m_deletedfiles.Count;
                if (m_deletedfolders != null)
                    count += m_deletedfolders.Count;
                count += m_diffedfiles;
                count += m_addedfiles;
                count += m_diffedfilessize;
                count += m_addedfilessize;
                if (m_newfolders != null)
                    count += m_newfolders.Count;

                return count != 0;
            }
        }

        #region IDisposable Members

        public void Dispose()
        {
            if (m_lastPartialFile != null)
            {
                try { m_lastPartialFile.Dispose(); }
                catch { }
                m_lastPartialFile = null;
            }

            if (m_patches != null)
            {
                foreach (Library.Interface.ICompression arc in m_patches)
                    try { arc.Dispose(); }
                    catch { }
                m_patches = null;
            }

            if (m_partialDeltas != null)
            {
                foreach (Utility.TempFile tf in m_partialDeltas.Values)
                    try 
                    { 
                        if (tf != null)
                            tf.Dispose(); 
                    }
                    catch { }
                m_partialDeltas = null;
            }


            if (m_stat is BackupStatistics)
            {
                BackupStatistics bs = m_stat as BackupStatistics;

                if (m_deletedfiles != null)
                    bs.DeletedFiles = m_deletedfiles.Count;
                if (m_deletedfolders != null)
                    bs.DeletedFolders = m_deletedfolders.Count;
                bs.ModifiedFiles = m_diffedfiles;
                bs.AddedFiles = m_addedfiles;
                bs.ExaminedFiles = m_examinedfiles;
                bs.OpenedFiles = m_filesopened;
                bs.SizeOfModifiedFiles = m_diffedfilessize;
                bs.SizeOfAddedFiles = m_addedfilessize;
                bs.SizeOfExaminedFiles = m_examinedfilesize;
                if (m_unproccesed != null && m_unproccesed.Files != null)
                    bs.UnprocessedFiles = m_unproccesed.Files.Count;
                if (m_newfolders != null)
                    bs.AddedFolders = m_newfolders.Count;
            }

            if (m_snapshot != null)
            {
                m_snapshot.Dispose();
                m_snapshot = null;
            }

        }

        #endregion

        /// <summary>
        /// Converts all filenames to use / as the dir separator
        /// </summary>
        /// <param name="filenames">The list of filenames to modify</param>
        /// <returns>A modified list of filenames</returns>
        public static string[] FilenamesToPlatformIndependant(string[] filenames)
        {
            if (System.IO.Path.DirectorySeparatorChar != '/')
                for (int i = 0; i < filenames.Length; i++)
                    filenames[i] = filenames[i].Replace(System.IO.Path.DirectorySeparatorChar, '/');

            return filenames;
        }

        /// <summary>
        /// Converts a list of filenames with / as the dir separator to use the OS separator.
        /// </summary>
        /// <param name="filenames">The list of filenames to convert</param>
        /// <param name="prefix">An optional prefix that is appended to the filenames</param>
        /// <returns>A list of filenames that use the prefix and the OS dirseparator</returns>
        public static string[] FilenamesFromPlatformIndependant(string[] filenames, string[] prefixes)
        {
            if (prefixes == null)
                prefixes = new string[] { "" };

            for (int i = 0; i < filenames.Length; i++)
                if (System.IO.Path.DirectorySeparatorChar != '/')
                    filenames[i] = GetFullPathFromRelname(prefixes, filenames[i].Replace('/', System.IO.Path.DirectorySeparatorChar));
                else
                    filenames[i] = GetFullPathFromRelname(prefixes, filenames[i]);

            return filenames;
        }


        /// <summary>
        /// Converts a list of filenames with / as the dir separator to use the OS separator.
        /// </summary>
        /// <param name="filenames">The list of filenames to convert</param>
        /// <returns>A list of filenames that use the prefix and the OS dirseparator</returns>
        public static string[] FilenamesFromPlatformIndependant(string[] filenames)
        {
            return FilenamesFromPlatformIndependant(filenames, null);
        }

        /// <summary>
        /// Gets or sets a value that indicates if the file modification time should be used as an indicator for file modification
        /// </summary>
        public bool DisableFiletimeCheck
        {
            get { return m_disableFiletimeCheck; }
            set { m_disableFiletimeCheck = value; }
        }

        /// <summary>
        /// Gets or sets the largest file allowed
        /// </summary>
        public long MaxFileSize
        {
            get { return m_maxFileSize; }
            set { m_maxFileSize = value; }
        }

        /// <summary>
        /// Gets a list of files and folders that are not yet processed
        /// </summary>
        /// <returns></returns>
        public List<string> UnmatchedFiles()
        {
            List<string> lst = new List<string>();
            lst.AddRange(m_oldFolders.Keys);
            lst.AddRange(m_oldSignatures.Keys);
            lst.AddRange(m_oldSymlinks.Keys);
            return lst;
        }

        /// <summary>
        /// Extracts the files found in a signature volume
        /// </summary>
        /// <param name="patch">The signature volume to read</param>
        /// <returns>A list of file or folder names and their types</returns>
        public List<KeyValuePair<PatchFileType, string>> ListPatchFiles(Library.Interface.ICompression patch)
        {
            List<Library.Interface.ICompression> patches = new List<Library.Interface.ICompression>();
            patches.Add(patch);
            return ListPatchFiles(patches);
        }

        /// <summary>
        /// Extracts the files found in a signature volume
        /// </summary>
        /// <param name="patchs">The signature volumes to read</param>
        /// <returns>A list of file or folder names and their types</returns>
        public List<KeyValuePair<PatchFileType, string>> ListPatchFiles(List<Library.Interface.ICompression> patches)
        {
            List<KeyValuePair<PatchFileType, string>> files = new List<KeyValuePair<PatchFileType, string>>();

            KeyValuePair<PatchFileType, string>[] signatures = new KeyValuePair<PatchFileType, string>[] {
                new KeyValuePair<PatchFileType, string>(PatchFileType.AddedOrUpdatedFile, Utility.Utility.AppendDirSeparator(COMBINED_SIGNATURE_ROOT)),
                new KeyValuePair<PatchFileType, string>(PatchFileType.AddedFile, Utility.Utility.AppendDirSeparator(CONTENT_SIGNATURE_ROOT)),
                new KeyValuePair<PatchFileType, string>(PatchFileType.UpdatedFile, Utility.Utility.AppendDirSeparator(DELTA_SIGNATURE_ROOT)),
            };

            string content_prefix = Utility.Utility.AppendDirSeparator(CONTENT_ROOT);
            string delta_prefix = Utility.Utility.AppendDirSeparator(DELTA_ROOT);
            string control_prefix = Utility.Utility.AppendDirSeparator(CONTROL_ROOT);
            Dictionary<string, bool> partials = new Dictionary<string, bool>();

            foreach (Library.Interface.ICompression arch in patches)
            {
                if (arch.FileExists(DELETED_FILES))
                    foreach (string s in FilenamesFromPlatformIndependant(arch.ReadAllLines(DELETED_FILES)))
                        files.Add(new KeyValuePair<PatchFileType, string>(PatchFileType.DeletedFile, s));

                foreach(KeyValuePair<PatchFileType, string> sigentry in signatures)
                    foreach (string f in FilenamesFromPlatformIndependant(arch.ListFiles(sigentry.Value)))
                    {
                        if (partials.ContainsKey(f))
                            partials.Remove(f);
                        files.Add(new KeyValuePair<PatchFileType, string>(sigentry.Key, f.Substring(sigentry.Value.Length)));
                    }

                foreach (string f in FilenamesFromPlatformIndependant(arch.ListFiles(control_prefix)))
                    files.Add(new KeyValuePair<PatchFileType, string>(PatchFileType.ControlFile, f.Substring(control_prefix.Length)));

                if (arch.FileExists(DELETED_FOLDERS))
                    foreach (string s in FilenamesFromPlatformIndependant(arch.ReadAllLines(DELETED_FOLDERS)))
                        files.Add(new KeyValuePair<PatchFileType, string>(PatchFileType.DeletedFolder, s));

                if (arch.FileExists(ADDED_FOLDERS))
                    foreach (string s in FilenamesFromPlatformIndependant(arch.ReadAllLines(ADDED_FOLDERS)))
                        files.Add(new KeyValuePair<PatchFileType, string>(PatchFileType.AddedFolder, s));

                if (arch.FileExists(INCOMPLETE_FILE))
                {
                    PartialEntryRecord pre = new PartialEntryRecord(arch.ReadAllLines(INCOMPLETE_FILE));

                    string filename = FilenamesFromPlatformIndependant(new string[] { pre.Filename })[0];
                    if (filename.StartsWith(content_prefix))
                    {
                        if (!partials.ContainsKey(filename.Substring(content_prefix.Length)))
                            partials.Add(filename.Substring(content_prefix.Length), false);
                    }
                    else if (filename.StartsWith(delta_prefix))
                    {
                        if (!partials.ContainsKey(filename.Substring(delta_prefix.Length)))
                            partials.Add(filename.Substring(delta_prefix.Length), false);
                    }
                }

                if (arch.FileExists(COMPLETED_FILE))
                {
                    PartialEntryRecord pre = new PartialEntryRecord(arch.ReadAllLines(COMPLETED_FILE));

                    string filename = FilenamesFromPlatformIndependant(new string[] { pre.Filename })[0];
                    if (filename.StartsWith(content_prefix))
                        partials[filename.Substring(content_prefix.Length)]= true;
                    else if (filename.StartsWith(delta_prefix))
                        partials[filename.Substring(delta_prefix.Length)] = true;

                }
            }

            foreach (KeyValuePair<string, bool> s in partials)
            {
                //Index of last found file that matches
                int lastIx = -1;

                for (int i = 0; i < files.Count; i++)
                {
                    KeyValuePair<PatchFileType, string> px = files[i];
                    if ((px.Key == PatchFileType.AddedFile || px.Key == PatchFileType.AddedOrUpdatedFile || px.Key == PatchFileType.UpdatedFile) && px.Value == s.Key)
                    {
                        //We have a new file, if one is already found, remove it
                        if (lastIx != -1)
                        {
                            files.RemoveAt(lastIx);
                            i--;
                        }
                        lastIx = i;
                    }
                }

                //The file is incomplete, remove that only file entry, and insert the incomplete file entry
                if (!s.Value)
                {
                    if (lastIx != -1)
                        files.RemoveAt(lastIx);
                    files.Add(new KeyValuePair<PatchFileType, string>(PatchFileType.IncompleteFile, s.Key));
                }
                
                //If the file is completed, there is now only one entry left
            }

            return files;
        }

        /// <summary>
        /// An internal helper class to collect filenames from the enumeration callback
        /// </summary>
        private class PathCollector
        {
            private readonly Options.SymlinkStrategy m_symlinkHandling;
            private readonly FileAttributes m_attributeMask;
            private readonly Utility.FilenameFilter m_filter;
            private readonly Snapshots.ISnapshotService m_snapshot;
            private readonly CommunicationStatistics m_stat;

            private List<string> m_files = new List<string>();
            private List<string> m_folders = new List<string>();
            private List<string> m_errors = new List<string>();
            private List<string> m_filesWithError = new List<string>();
            private List<string> m_filesTooLarge = new List<string>();
            private List<KeyValuePair<string, string>> m_symlinks = new List<KeyValuePair<string, string>>();

            public PathCollector(Snapshots.ISnapshotService snapshot, Options.SymlinkStrategy symlinkHandling, FileAttributes attributeMask, Utility.FilenameFilter filter, CommunicationStatistics stat)
            {
                m_symlinkHandling = symlinkHandling;
                m_filter = filter;
                m_attributeMask = attributeMask;
                m_snapshot = snapshot;
                m_stat = stat;
            }

            public bool Callback(string rootpath, string path, FileAttributes attributes)
            {
                if ((attributes & Utility.Utility.ATTRIBUTE_ERROR) != 0)
                {
                    m_errors.Add(path);
                    return false;
                }

                if ((attributes & m_attributeMask) != 0)
                    return false;

                if (!m_filter.ShouldInclude(rootpath, path))
                    return false;

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if (m_symlinkHandling == Options.SymlinkStrategy.Ignore)
                        return false;
                    else if (m_symlinkHandling == Options.SymlinkStrategy.Store)
                    {
                        try 
                        {
                            //We treat symlinks as files, even if they point to folders
                            if (path[path.Length - 1] == Path.DirectorySeparatorChar)
                                path = path.Substring(0, path.Length - 1);

                            string s = m_snapshot.GetSymlinkTarget(path);
                            if (s != null)
                            {
                                if ((attributes & FileAttributes.Directory) != 0)
                                    s = Utility.Utility.AppendDirSeparator(s);
                                m_symlinks.Add(new KeyValuePair<string, string>(path, s));
                            }
                        }
                        catch(Exception ex)
                        {
                            Logging.Log.WriteMessage(string.Format("Failed to obtain symlink target for {0}, message: {1}", path, ex.Message), Duplicati.Library.Logging.LogMessageType.Warning, ex);
                            if (m_stat != null)
                                m_stat.LogWarning(string.Format("Failed to obtain symlink target for {0}, message: {1}", path, ex.Message), ex);
                            m_errors.Add(path);
                        }
                        return false;
                    }
                }

                if ((attributes & FileAttributes.Directory) != 0)
                    m_folders.Add(path);
                else 
                    m_files.Add(path);

                return true;
            }

            public List<string> Files { get { return m_files; } }
            public List<string> Folders { get { return m_folders; } }
            public List<string> Errors { get { return m_errors; } }
            public List<string> FilesWithError { get { return m_filesWithError; } }
            public List<string> FilesTooLarge { get { return m_filesTooLarge; } }
            public List<KeyValuePair<string, string>> Symlinks { get { return m_symlinks; } }

            public bool IsAffectedByError(string path)
            {
                foreach (string s in m_errors)
                    if (path.StartsWith(s))
                        return true;

                return m_filesWithError.Contains(path);
            }
        }


        /// <summary>
        /// Helper method to search a signature file for existence of a specified file
        /// </summary>
        /// <param name="mfi">The manifest that the signature file derives from</param>
        /// <param name="fileToFind">The files to find</param>
        /// <param name="signature">The signature file</param>
        internal static void ContainsFile(Manifestfile mfi, string[] filesToFind, Duplicati.Library.Interface.ICompression signature)
        {
            string[] prefixes = new string[] {
                Utility.Utility.AppendDirSeparator(COMBINED_SIGNATURE_ROOT),
                Utility.Utility.AppendDirSeparator(CONTENT_SIGNATURE_ROOT),
                Utility.Utility.AppendDirSeparator(DELTA_SIGNATURE_ROOT)
            };

            foreach (string prefix in prefixes)
                foreach (string f in FilenamesFromPlatformIndependant(signature.ListFiles(prefix)))
                {
                    string fname = f.Substring(prefix.Length);
                    for (int i = 0; i < filesToFind.Length; i++)
                    {
                        if (string.IsNullOrEmpty(filesToFind[i]))
                            continue;
                        string fileToFind = filesToFind[i];
                        string name = System.IO.Path.IsPathRooted(fileToFind) ? GetRelativeName(mfi.SourceDirs, fileToFind) : fileToFind;
                        if (fname.Equals(name, Utility.Utility.ClientFilenameStringComparision))
                            filesToFind[i] = null;
                    }
                }
        }
    }
}
