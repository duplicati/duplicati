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
using System.IO;

namespace Duplicati.Library.Main.RSync
{
    /// <summary>
    /// This class wraps the process of creating a diff for an entire folder
    /// </summary>
    public class RSyncDir : IDisposable
    {
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

        /// <summary>
        /// An internal helper class that aids in supporting three different prefixes for signatures
        /// </summary>
        private class ArchiveWrapper
        {
            /// <summary>
            /// The archive to wrap
            /// </summary>
            private Core.IFileArchive m_archive;

            /// <summary>
            /// The prefix to append to request
            /// </summary>
            private string m_prefix;

            /// <summary>
            /// Constructs a new ArchiveWrapper with a given prefix
            /// </summary>
            /// <param name="arch">The archive to wrap</param>
            /// <param name="prefix">The prefix to use</param>
            public ArchiveWrapper(Core.IFileArchive arch, string prefix)
            {
                m_archive = arch;
                m_prefix = prefix;
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
            /// Gets the last time the file was modified
            /// </summary>
            /// <param name="relpath"The path to the file, excluding the prefix></param>
            /// <returns>The last time the file was modified</returns>
            internal DateTime GetLastWriteTime(string relpath)
            {
                return m_archive.GetLastWriteTime(System.IO.Path.Combine(m_prefix, relpath));
            }
        }

        /// <summary>
        /// An internal helper class that allows a file to span multiple volumes
        /// </summary>
        private class PartialFileEntry : IDisposable
        {
            public readonly string relativeName;
            private Core.TempFile tempfile;
            private System.IO.FileStream m_fs;
            private System.IO.Stream m_signatureStream;
            private string m_signaturePath;

            public PartialFileEntry(string filename, string relname, long position, System.IO.Stream signatureFile, string signaturePath)
            {
                m_fs = System.IO.File.OpenRead(filename);
                m_fs.Position = position;
                this.relativeName = relname;
                this.tempfile = null;
                this.m_signatureStream = signatureFile;
                this.m_signaturePath = signaturePath;
            }

            public PartialFileEntry(Core.TempFile tempfile, string relname, long position, System.IO.Stream signatureFile, string signaturePath)
            {
                m_fs = System.IO.File.OpenRead(tempfile);
                m_fs.Position = position;
                this.relativeName = relname;
                this.tempfile = tempfile;
                this.m_signatureStream = signatureFile;
                this.m_signaturePath = signaturePath;
            }

            public System.IO.FileStream Stream { get { return m_fs; } }

            public void DumpSignature(Core.IFileArchive signatureArchive)
            {
                //Add signature AFTER content.
                //If content is present, it is restoreable, if signature is missing, file will be backed up on next run
                //If signature is present, but not content, the entire differential sequence will be unable to recover the file

                using (m_signatureStream)
                using (System.IO.Stream s3 = signatureArchive.CreateFile(this.m_signaturePath))
                    Core.Utility.CopyStream(m_signatureStream, s3, true);
                
                m_signatureStream = null;
            }

            /// <summary>
            /// Gets a value representing a rough estimate of how many bytes this partial entry will use when written to the datastream.s
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

                if (this.tempfile != null)
                {
                    this.tempfile.Dispose();
                    this.tempfile = null;
                }

                if (m_signatureStream != null)
                {
                    m_signatureStream.Dispose();
                    m_signatureStream = null;
                }
            }

            #endregion
        }

        /// <summary>
        /// Class that represents a partial record in the INCOMPLETE_FILE or COMPLETED_FILE
        /// </summary>
        public class PartialEntryRecord
        {
            public string Filename { get; set; }
            public long StartOffset { get; set; }
            public long Length { get; set; }
            public long TotalFileSize { get; set; }

            public PartialEntryRecord(string[] items)
            {
                if (items.Length != 4)
                    throw new Exception(Strings.RSyncDir.InvalidPartialRecordError);
                this.Filename = items[0];
                this.StartOffset = long.Parse(items[1]);
                this.Length = long.Parse(items[2]);
                this.TotalFileSize = long.Parse(items[3]);
            }

            public PartialEntryRecord(string filename, long start, long size, long totalSize)
            {
                this.Filename = filename;
                this.StartOffset = start;
                this.Length = size;
                this.TotalFileSize = totalSize;
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


        internal static readonly string COMBINED_SIGNATURE_ROOT = "signature";
        internal static readonly string CONTENT_SIGNATURE_ROOT = "content_signature";
        internal static readonly string DELTA_SIGNATURE_ROOT = "delta_signature";

        internal static readonly string CONTENT_ROOT = "snapshot";
        internal static readonly string DELTA_ROOT = "diff";
        //private static readonly string DACL_ROOT = "dacl";
        internal static readonly string CONTROL_ROOT = "controlfiles";

        internal static readonly string DELETED_FILES = "deleted_files.txt";
        internal static readonly string DELETED_FOLDERS = "deleted_folders.txt";

        internal static readonly string ADDED_FOLDERS = "added_folders.txt";
        internal static readonly string INCOMPLETE_FILE = "incomplete_file.txt";
        internal static readonly string COMPLETED_FILE = "completed_file.txt";


        public delegate void ProgressEventDelegate(int progress, string filename);
        public event ProgressEventDelegate ProgressEvent;


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
        private Dictionary<string, string> m_oldFolders;

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
        /// This is the list of added folders
        /// </summary>
        private List<string> m_newfolders;
        /// <summary>
        /// This is the list of deleted folders
        /// </summary>
        private List<string> m_deletedfolders;
        
        /// <summary>
        /// The total number of files found
        /// </summary>
        private long m_totalfiles;
        /// <summary>
        /// The number of files examined
        /// </summary>
        private long m_examinedfiles;
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
        private Core.FilenameFilter m_filter;

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
        private List<Core.IFileArchive> m_patches;

        /// <summary>
        /// A leftover file that is partially written, used when creating backups
        /// </summary>
        private PartialFileEntry m_lastPartialFile = null;

        /// <summary>
        /// A list of partial delta files, used when restoring
        /// </summary>
        private Dictionary<string, Core.TempFile> m_partialDeltas;

        /// <summary>
        /// A list of folders that should be deleted after the current restore operation has completed
        /// </summary>
        private List<string> m_folders_to_delete;

        /// <summary>
        /// Initializes a RSyncDir instance, binds it to the source folder, and reads in the supplied patches
        /// </summary>
        /// <param name="sourcefolder">The folders to create a backup from</param>
        /// <param name="stat">The status report object</param>
        /// <param name="filter">An optional filter that controls what files to include</param>
        /// <param name="patches">A list of signature archives to read</param>
        public RSyncDir(string[] sourcefolder, CommunicationStatistics stat, Core.FilenameFilter filter, List<Core.IFileArchive> patches)
            : this(sourcefolder, stat, filter)
        {
            string[] prefixes = new string[] {
                Core.Utility.AppendDirSeperator(COMBINED_SIGNATURE_ROOT),
                Core.Utility.AppendDirSeperator(CONTENT_SIGNATURE_ROOT),
                Core.Utility.AppendDirSeperator(DELTA_SIGNATURE_ROOT)
            };

            m_patches = patches;

            foreach (Core.IFileArchive z in patches)
            {
                if (z.FileExists(DELETED_FILES))
                    foreach (string s in FilenamesFromPlatformIndependant(z.ReadAllLines(DELETED_FILES)))
                        if (m_oldSignatures.ContainsKey(s))
                            m_oldSignatures.Remove(s);

                foreach(string prefix in prefixes)
                    foreach (string f in FilenamesFromPlatformIndependant(z.ListFiles(prefix)))
                        m_oldSignatures[f.Substring(prefix.Length)] = new ArchiveWrapper(z, prefix);

                if (z.FileExists(DELETED_FOLDERS))
                    foreach (string s in FilenamesFromPlatformIndependant(z.ReadAllLines(DELETED_FOLDERS)))
                        if (m_oldFolders.ContainsKey(s))
                            m_oldFolders.Remove(s);

                if (z.FileExists(ADDED_FOLDERS))
                    foreach (string s in FilenamesFromPlatformIndependant(z.ReadAllLines(ADDED_FOLDERS)))
                        m_oldFolders[s] = s;
            }
        }

        /// <summary>
        /// Initializes a RSyncDir instance, and binds it to the source folder
        /// </summary>
        /// <param name="sourcefolder">The folders to create a backup from</param>
        /// <param name="stat">The status report object</param>
        /// <param name="filter">An optional filter that controls what files to include</param>
        public RSyncDir(string[] sourcefolder, CommunicationStatistics stat, Core.FilenameFilter filter)
        {
            m_filter = filter;
            m_oldSignatures = new Dictionary<string, ArchiveWrapper>();
            m_oldFolders = new Dictionary<string, string>();
            for (int i = 0; i < sourcefolder.Length; i++)
            {
                if (!System.IO.Path.IsPathRooted(sourcefolder[i]))
                    sourcefolder[i] = System.IO.Path.GetFullPath(sourcefolder[i]); 
                sourcefolder[i] = Core.Utility.AppendDirSeperator(sourcefolder[i]);
            }
            m_stat = stat;
            m_sourcefolder = sourcefolder;

            if (m_filter == null)
                m_filter = new Duplicati.Library.Core.FilenameFilter(new List<KeyValuePair<bool, string>>());
        }

        /// <summary>
        /// Creates a new patch content/signature pair.
        /// Does not create multiple volumes, not used by Duplicati
        /// </summary>
        /// <param name="signatures">An archive where signatures can be put into</param>
        /// <param name="content">An archive where content can be put into</param>
        public void CreatePatch(Core.IFileArchive signatures, Core.IFileArchive content)
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
            if (full)
            {
                m_oldFolders = new Dictionary<string, string>();
                m_oldSignatures = new Dictionary<string, ArchiveWrapper>();
            }

            m_newfiles = new Dictionary<string, string>();
            m_modifiedFiles = new Dictionary<string, string>();
            m_deletedfiles = new List<string>();
            m_newfolders = new List<string>();
            m_deletedfolders = new List<string>();
            m_lastPartialFile = null;

            m_unproccesed = new PathCollector();
            //TODO: Figure out how to make this faster, but still random
            //Perhaps use itterative callbacks, with random recurse or itterate on each folder
            //... we need to know the total length to provide a progress bar... :(

            foreach(string s in m_sourcefolder)
                Core.Utility.EnumerateFileSystemEntries(s, m_filter, new Duplicati.Library.Core.Utility.EnumerationCallbackDelegate(m_unproccesed.Callback));

            m_totalfiles = m_unproccesed.Files.Count;
            m_isfirstmultipass = true;
            
            //Build folder diffs
            foreach(string s in m_unproccesed.Folders)
            {
                string relpath = GetRelativeName(s);
                if (relpath.Trim().Length != 0)
                {
                    if (!m_oldFolders.ContainsKey(relpath))
                        m_newfolders.Add(relpath);
                    else
                        m_oldFolders.Remove(relpath);
                }
            }

            m_unproccesed.Folders.Clear();
            foreach(string s in m_oldFolders.Keys)
                if (!m_unproccesed.IsAffectedByError(s))
                    m_deletedfolders.Add(s);
        }

        /// <summary>
        /// Gets the source folder that contains the given path
        /// </summary>
        /// <param name="path">The path to find the source folder for</param>
        /// <returns>The source folder path</returns>
        private string GetSourceFolder(string path)
        {
            foreach (string s in m_sourcefolder)
                if (path.StartsWith(s, Core.Utility.IsFSCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase))
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

            for(int i = 0; i < m_sourcefolder.Length; i++)
                if (path.StartsWith(m_sourcefolder[i], Core.Utility.IsFSCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase))
                    return System.IO.Path.Combine(i.ToString(), path.Substring(m_sourcefolder[i].Length));

            throw new Exception(string.Format(Strings.RSyncDir.InternalPathMappingError, path, string.Join(System.IO.Path.PathSeparator.ToString(), m_sourcefolder)));
        }

        /// <summary>
        /// Gets the system path, given a relative filename
        /// </summary>
        /// <param name="relpath">The relative filename</param>
        /// <returns>The full system path to the file</returns>
        private string GetFullPathFromRelname(string relpath)
        {
            if (m_sourcefolder.Length == 1)
                return System.IO.Path.Combine(m_sourcefolder[0], relpath);
            else
            {
                int ix = relpath.IndexOf(System.IO.Path.DirectorySeparatorChar);
                int pos = int.Parse(relpath.Substring(0, ix));
                return System.IO.Path.Combine(m_sourcefolder[pos], relpath.Substring(ix + 1));
            }
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
            if (sourcefolders.Length == 1)
                return System.IO.Path.Combine(sourcefolders[0], relpath);
            else
            {
                int ix = relpath.IndexOf(System.IO.Path.DirectorySeparatorChar);
                int pos = int.Parse(relpath.Substring(0, ix));
                if (ix >= sourcefolders.Length)
                    return System.IO.Path.Combine(sourcefolders[sourcefolders.Length - 1], relpath);
                else
                    return System.IO.Path.Combine(sourcefolders[pos], relpath.Substring(ix + 1));
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
        public bool FinalizeMultiPass(Core.IFileArchive signaturefile, Core.IFileArchive contentfile, long volumesize)
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
                                stringsize += System.Text.Encoding.UTF8.GetByteCount(s + Environment.NewLine);
                            }
                        }
                        catch (Exception ex)
                        {
                            if (m_stat != null)
                                m_stat.LogError(string.Format(Strings.RSyncDir.DeletedFilenameError, s, sourcefolder));
                            Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.DeletedFilenameError, s, sourcefolder), Duplicati.Library.Logging.LogMessageType.Error, ex);
                            m_unproccesed.FilesWithError.Add(s);
                        }
                    }

                    if (m_deletedfiles.Count > 0)
                    {
                        //The +100 is a safety margin
                        stringsize += System.Text.Encoding.UTF8.GetByteCount(DELETED_FILES) + 100;
                        if (contentfile.Size + contentfile.FlushBufferSize + stringsize > volumesize)
                        {
                            m_deletedfiles.Clear();
                            return false; //The followup cannot fit in the volume, so we make a full new volume
                        }

                        signaturefile.WriteAllLines(DELETED_FILES, m_deletedfiles.ToArray());
                        contentfile.WriteAllLines(DELETED_FILES, m_deletedfiles.ToArray());
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
        public bool MakeMultiPassDiff(Core.IFileArchive signaturefile, Core.IFileArchive contentfile, long volumesize)
        {
            if (m_unproccesed == null)
                throw new Exception(Strings.RSyncDir.MultipassUsageError);

            Random r = new Random();
            long totalSize = 0;

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
                    signaturefile.WriteAllLines(ADDED_FOLDERS, m_newfolders.ToArray());
                    contentfile.WriteAllLines(ADDED_FOLDERS, m_newfolders.ToArray());
                }

                m_isfirstmultipass = false;
            }

            if (m_lastPartialFile != null)
                m_lastPartialFile = WritePossiblePartial(m_lastPartialFile, contentfile, signaturefile, volumesize);

            int lastPg = -1;

            while (m_unproccesed.Files.Count > 0 && totalSize < volumesize && m_lastPartialFile == null)
            {

                int next = r.Next(0, m_unproccesed.Files.Count);
                string s = m_unproccesed.Files[next];
                m_unproccesed.Files.RemoveAt(next);

                if (ProgressEvent != null)
                {
                    //Update each 0.5% change, so it is visible that files are being examined
                    int pg = 200 - ((int)((m_unproccesed.Files.Count / (double)m_totalfiles) * 200));
                    if (lastPg != pg)
                    {
                        ProgressEvent(pg / 2, s);
                        lastPg = pg;
                    }
                }

                try
                {
                    if (!m_disableFiletimeCheck)
                    {
                        //TODO: Make this check faster somehow
                        string relpath = GetRelativeName(s);
                        if (m_oldSignatures.ContainsKey(relpath))
                        {
                            if (System.IO.File.GetLastWriteTime(s) < m_oldSignatures[relpath].GetLastWriteTime(relpath))
                            {
                                m_oldSignatures.Remove(relpath);
                                m_examinedfiles++;
                                continue;
                            }
                        }
                    }

                    if (m_unproccesed.Errors.Count > 0 && m_unproccesed.IsAffectedByError(s))
                        m_unproccesed.FilesWithError.Add(s);
                    else
                    {
                        using (System.IO.FileStream fs = System.IO.File.Open(s, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            if (fs.Length > m_maxFileSize)
                                m_unproccesed.FilesTooLarge.Add(s);
                            else
                            {
                                //If the file is > 10mb, update the display to show the file being processed
                                if (ProgressEvent != null && fs.Length > 1024 * 1024 * 10)
                                    ProgressEvent(lastPg / 2, s);

                                System.IO.Stream signature = ProccessDiff(fs, s, signaturefile);
                                if (signature != null)
                                    totalSize = AddFileToCompression(fs, s, signature, contentfile, signaturefile, volumesize);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (m_stat != null)
                        m_stat.LogError(string.Format(Strings.RSyncDir.FileProcessError, s, ex.Message));
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
        private System.IO.Stream ProccessDiff(System.IO.FileStream fs, string s, Core.IFileArchive signaturefile)
        {
            string relpath = GetRelativeName(s);

            System.IO.MemoryStream ms = new MemoryStream();
            m_examinedfilesize += fs.Length;
            m_examinedfiles++;
            SharpRSync.Interface.GenerateSignature(fs, ms);
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
                    equals = Core.Utility.CompareStreams(s2, ms, false);

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
        /// <returns>The current size of the content archive</returns>
        private long AddFileToCompression(System.IO.FileStream fs, string s, System.IO.Stream signature, Core.IFileArchive contentfile, Core.IFileArchive signaturefile, long volumesize)
        {
            fs.Position = 0;
            signature.Position = 0;
            string relpath = GetRelativeName(s);

            if (m_modifiedFiles.ContainsKey(s))
            {
                //Existing file, write the delta file
                string target = System.IO.Path.Combine(DELTA_ROOT, relpath);
                string signaturepath = System.IO.Path.Combine(DELTA_SIGNATURE_ROOT, relpath);

                using (System.IO.Stream sigfs = m_oldSignatures[relpath].OpenRead(relpath))
                {
                    long lbefore = contentfile.Size;

                    Core.TempFile deltaTemp = null;
                    try
                    {
                        deltaTemp = new Core.TempFile();
                        using (System.IO.FileStream s3 = System.IO.File.Create(deltaTemp))
                            SharpRSync.Interface.GenerateDelta(sigfs, fs, s3);

                        m_lastPartialFile = WritePossiblePartial(new PartialFileEntry(deltaTemp, target, 0, signature, signaturepath), contentfile, signaturefile, volumesize);
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

                m_lastPartialFile = WritePossiblePartial(new PartialFileEntry(s, target, 0, signature, signaturepath), contentfile, signaturefile, volumesize);
                
                m_addedfiles++;
                m_addedfilessize += fs.Length;

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
        private PartialFileEntry WritePossiblePartial(PartialFileEntry entry, Core.IFileArchive contentfile, Core.IFileArchive signaturefile, long volumesize)
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
#if DEBUG
                //If we are debugging, this can be nice to have
                Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.PartialFileAddedLogMessage, entry.relativeName, startPos), Duplicati.Library.Logging.LogMessageType.Information);
#endif
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
                entry.DumpSignature(signaturefile);
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
        private PartialFileEntry WritePossiblePartialInternal(PartialFileEntry entry, Core.IFileArchive contentfile, long volumesize)
        {
            //append chuncks of 1kb, checking on the total size after each write
            byte[] tmp = new byte[1024];
            using (System.IO.Stream s3 = contentfile.CreateFile(entry.relativeName))
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
        public void Restore(string[] destination, List<Core.IFileArchive> patches)
        {
            if (patches != null)
            {
                foreach (Core.IFileArchive s in patches)
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
            if (m_folders_to_delete != null)
            {
                foreach (string s in m_folders_to_delete)
                {
                    if (System.IO.Directory.Exists(s))
                        try
                        {
                            //TODO: Perhaps read ahead in patches to prevent creation
                            System.IO.Directory.Delete(s, false);
                        }
                        catch (Exception ex)
                        {
                            if (m_stat != null)
                                m_stat.LogError(string.Format(Strings.RSyncDir.DeleteFolderError, s, ex.Message));
                            Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.DeleteFolderError, s, ex.Message), Duplicati.Library.Logging.LogMessageType.Warning, ex);
                        }
                    else
                        Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.FolderToDeleteMissingError, s), Duplicati.Library.Logging.LogMessageType.Warning);
                }
                m_folders_to_delete = null;
            }

            if (m_partialDeltas != null)
            {
                foreach (KeyValuePair<string, Core.TempFile> tf in m_partialDeltas)
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
                            m_stat.LogError(logmessage);
                    }
                }
                m_partialDeltas = null;
            }
        }

        /// <summary>
        /// Internal helper class to filter files during a partial restore
        /// </summary>
        private class FilterHelper
        {
            /// <summary>
            /// A copy of the filter to use
            /// </summary>
            private Core.FilenameFilter m_filter;

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
            public FilterHelper(RSyncDir parent, string[] folders, Core.FilenameFilter filter)
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
                return m_filter.ShouldInclude(System.IO.Path.DirectorySeparatorChar.ToString(), System.IO.Path.DirectorySeparatorChar.ToString() + Core.Utility.AppendDirSeperator(element));
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
            /// <returns>A filtered list</returns>
            public IEnumerable<string> Filterlist(IEnumerable<string> input)
            {
                return new Core.PlugableEnumerable<string>(new Predicate<string>(FilterPredicate), new Core.Func<string,string>(GetFullpathFunc), input);
            }
        }

        /// <summary>
        /// Applies a patch (content file) to the destination
        /// </summary>
        /// <param name="destination">The destination that contains the previous version of the data</param>
        /// <param name="patch">The content file that the destination is patched with</param>
        public void Patch(string[] destination, Core.IFileArchive patch)
        {
            if (m_partialDeltas == null)
                m_partialDeltas = new Dictionary<string, Duplicati.Library.Core.TempFile>();

            for (int i = 0; i < destination.Length; i++)
                destination[i] = Core.Utility.AppendDirSeperator(destination[i]);

            //Set up the filter system to avoid dealing with filtered items
            FilterHelper fh = new FilterHelper(this, destination, m_filter);

            //Delete all files that were removed
            if (patch.FileExists(DELETED_FILES))
                foreach (string s in fh.Filterlist(FilenamesFromPlatformIndependant(patch.ReadAllLines(DELETED_FILES))))
                {
                    if (System.IO.File.Exists(s))
                    {
                        try
                        {
                            //TODO: Perhaps read ahead in patches to prevent creation
                            System.IO.File.Delete(s);
                        }
                        catch (Exception ex)
                        {
                            if (m_stat != null)
                                m_stat.LogError(string.Format(Strings.RSyncDir.DeleteFileError, s, ex.Message));
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
                List<string> deletedfolders = new List<string>(fh.Filterlist(FilenamesFromPlatformIndependant(patch.ReadAllLines(DELETED_FOLDERS))));
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
                List<string> addedfolders = new List<string>(fh.Filterlist(FilenamesFromPlatformIndependant(patch.ReadAllLines(ADDED_FOLDERS))));

                //Make sure topfolders are created first
                addedfolders.Sort();

                foreach (string s in addedfolders)
                {
                    if (!System.IO.Directory.Exists(s))
                        try
                        {
                            System.IO.Directory.CreateDirectory(s);
                        }
                        catch (Exception ex)
                        {
                            if (m_stat != null)
                                m_stat.LogError(string.Format(Strings.RSyncDir.CreateFolderError, s, ex.Message));
                            Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.CreateFolderError, s, ex.Message), Duplicati.Library.Logging.LogMessageType.Warning, ex);
                        }
                }
            }

            PartialEntryRecord pe = null;
            if (patch.FileExists(INCOMPLETE_FILE))
                pe = new PartialEntryRecord(patch.ReadAllLines(INCOMPLETE_FILE));
            
            PartialEntryRecord fe = null;
            if (patch.FileExists(COMPLETED_FILE))
                fe = new PartialEntryRecord(patch.ReadAllLines(COMPLETED_FILE));

            //Restore new files
            string prefix = Core.Utility.AppendDirSeperator(CONTENT_ROOT);

            foreach (string s in m_filter.FilterList(prefix, patch.ListFiles(prefix)))
            {
                string target = GetFullPathFromRelname(destination, s.Substring(prefix.Length));
                try
                {
                    if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(target)))
                    {
                        Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.RestoreFolderMissingError, target), Duplicati.Library.Logging.LogMessageType.Warning);
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                    }

                    using (System.IO.Stream s1 = patch.OpenRead(s))
                    {
                        PartialEntryRecord pex = null;
                        Core.TempFile partialFile = null;
                        if (pe != null && string.Equals(pe.Filename, s))
                        {
                            pex = pe; //The file is incomplete
                            if (!m_partialDeltas.ContainsKey(s))
                                m_partialDeltas.Add(s, new Core.TempFile());
                            partialFile = m_partialDeltas[s];
                        }
                        else if (fe != null && string.Equals(fe.Filename, s))
                        {
                            pex = fe; //The file has the final segment
                            if (!m_partialDeltas.ContainsKey(s))
                                throw new Exception(string.Format(Strings.RSyncDir.InvalidPartialFileEntry, s));
                            partialFile = m_partialDeltas[s];
                        }

                        long startOffset = pex == null ? 0 : pex.StartOffset;
                        using (System.IO.FileStream s2 = System.IO.File.OpenWrite(partialFile == null ? target : (string)partialFile))
                        {
                            s2.Position = startOffset;
                            if (startOffset == 0)
                                s2.SetLength(0);

                            Core.Utility.CopyStream(s1, s2);
                        }

                        if (pex != null && pex == fe)
                        {
                            if (System.IO.File.Exists(target))
                                System.IO.File.Delete(target);
                            System.IO.File.Move(partialFile, target);
                            partialFile.Dispose();
                            m_partialDeltas.Remove(s);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (m_stat != null)
                        m_stat.LogError(string.Format(Strings.RSyncDir.RestoreFileError, s, ex.Message));
                    Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.RestoreFileError, s, ex.Message), Duplicati.Library.Logging.LogMessageType.Error, ex);
                }

            }

            //Patch modfied files
            prefix = Core.Utility.AppendDirSeperator(DELTA_ROOT);
            foreach (string s in m_filter.FilterList(prefix, patch.ListFiles(prefix)))
            {
                string target = GetFullPathFromRelname(destination, s.Substring(prefix.Length));
                try
                {
                    if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(target)))
                    {
                        Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.RestoreFolderDeltaError, target), Duplicati.Library.Logging.LogMessageType.Warning);
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                    }

                    PartialEntryRecord pex = null;
                    if (pe != null && string.Equals(pe.Filename, s))
                        pex = pe; //The file is incomplete
                    else if (fe != null && string.Equals(fe.Filename, s))
                        pex = fe; //The file has the final segment

                    Core.TempFile tempDelta = null;

                    if (pex != null && string.Equals(pex.Filename, s))
                    {
                        //Ensure that the partial file list is in the correct state
                        if (pex.StartOffset == 0 && m_partialDeltas.ContainsKey(s))
                            throw new Exception(string.Format(Strings.RSyncDir.InvalidPartialFileEntry, s));
                        else if (pex.StartOffset != 0 && !m_partialDeltas.ContainsKey(s))
                            throw new Exception(string.Format(Strings.RSyncDir.InvalidPartialFileEntry, s));
                        else if (pex.StartOffset == 0) //First entry, so create a temp file
                            m_partialDeltas.Add(s, new Duplicati.Library.Core.TempFile());

                        //Dump the content in the temp file at the specified offset
                        using (System.IO.Stream st = System.IO.File.OpenWrite(m_partialDeltas[s]))
                        {
                            if (st.Length != pex.StartOffset)
                                throw new Exception(string.Format(Strings.RSyncDir.InvalidPartialFileEntry, s));
                            st.Position = pex.StartOffset;
                            using (System.IO.Stream s2 = patch.OpenRead(s))
                                Core.Utility.CopyStream(s2, st);
                        }

                        //We can't process it until it is recieved completely
                        if (pex != fe)
                            continue;

                        tempDelta = m_partialDeltas[s];
                        m_partialDeltas.Remove(s);
                    }
                    else if (m_partialDeltas.ContainsKey(s))
                        throw new Exception(string.Format(Strings.RSyncDir.FileShouldBePartialError, s));


                    using (Core.TempFile tempfile = new Core.TempFile())
                    using (tempDelta) //May be null, but the using directive does not care
                    {
                        //Use either the patch directly, or the partial temp file
                        System.IO.Stream deltaStream = tempDelta == null ? patch.OpenRead(s) : System.IO.File.OpenRead(tempDelta);
                        using (System.IO.Stream s2 = deltaStream)
                        using (System.IO.FileStream s1 = System.IO.File.OpenRead(target))
                        using (System.IO.FileStream s3 = System.IO.File.Create(tempfile))
                            SharpRSync.Interface.PatchFile(s1, s2, s3);

                        System.IO.File.Delete(target);
                        System.IO.File.Move(tempfile, target);
                    }
                }
                catch (Exception ex)
                {
                    if (m_stat != null)
                        m_stat.LogError(string.Format(Strings.RSyncDir.RestoreFileError, s, ex.Message));
                    Logging.Log.WriteMessage(string.Format(Strings.RSyncDir.RestoreFileError, s, ex.Message), Duplicati.Library.Logging.LogMessageType.Error, ex);

                    try { System.IO.File.Delete(target); }
                    catch { }
                }

            }
        }


        #region IDisposable Members

        public void Dispose()
        {
            if (m_patches != null)
            {
                foreach (Core.IFileArchive arc in m_patches)
                    try { arc.Dispose(); }
                    catch { }
                m_patches = null;
            }

            if (m_partialDeltas != null)
            {
                foreach (Core.TempFile tf in m_partialDeltas.Values)
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

                bs.DeletedFiles = m_deletedfiles.Count;
                bs.DeletedFolders = m_deletedfolders.Count;
                bs.ModifiedFiles = m_diffedfiles;
                bs.AddedFiles = m_addedfiles;
                bs.ExaminedFiles = m_examinedfiles;
                bs.SizeOfModifiedFiles = m_diffedfilessize;
                bs.SizeOfAddedFiles = m_addedfilessize;
                bs.SizeOfExaminedFiles = m_examinedfilesize;
                bs.UnprocessedFiles = m_unproccesed.Files.Count;
                bs.AddedFolders = m_newfolders.Count;
            }

        }

        #endregion


        /*
        /// <summary>
        /// Will update all files in basefolder with the changes recorded in updatefolder.
        /// Only handles signature folders
        /// </summary>
        /// <param name="basefolder">The folder to update</param>
        /// <param name="updatefolder">The updates to merge into the basefolder</param>
        public static void MergeSignatures(string basefolder, string updatefolder)
        {
            basefolder = Core.Utility.AppendDirSeperator(basefolder);
            updatefolder = Core.Utility.AppendDirSeperator(updatefolder);

            Dictionary<string, string> deletedfiles = new Dictionary<string, string>();
            if (System.IO.File.Exists(System.IO.Path.Combine(basefolder, DELETED_FILES)))
                foreach (string s in FilenamesFromPlatformIndependant(System.IO.File.ReadAllLines(System.IO.Path.Combine(basefolder, DELETED_FILES))))
                    deletedfiles.Add(s, s);

            if (System.IO.File.Exists(System.IO.Path.Combine(updatefolder, DELETED_FILES)))
                foreach (string s in FilenamesFromPlatformIndependant(System.IO.File.ReadAllLines(System.IO.Path.Combine(updatefolder, DELETED_FILES))))
                    System.IO.File.Delete(System.IO.Path.Combine(System.IO.Path.Combine(basefolder, SIGNATURE_ROOT), s));

            Dictionary<string, string> addedfolders = new Dictionary<string, string>();
            if (System.IO.File.Exists(System.IO.Path.Combine(basefolder, ADDED_FOLDERS)))
                foreach (string s in FilenamesFromPlatformIndependant(System.IO.File.ReadAllLines(System.IO.Path.Combine(basefolder, ADDED_FOLDERS))))
                    addedfolders.Add(s, s);

            Dictionary<string, string> deletedfolders = new Dictionary<string, string>();
            if (System.IO.File.Exists(System.IO.Path.Combine(basefolder, DELETED_FOLDERS)))
                foreach (string s in FilenamesFromPlatformIndependant(System.IO.File.ReadAllLines(System.IO.Path.Combine(basefolder, DELETED_FOLDERS))))
                    deletedfolders.Add(s,s );

            if (System.IO.File.Exists(System.IO.Path.Combine(updatefolder, DELETED_FOLDERS)))
                foreach (string s in FilenamesFromPlatformIndependant(System.IO.File.ReadAllLines(System.IO.Path.Combine(updatefolder, DELETED_FOLDERS))))
                {
                    if (addedfolders.ContainsKey(s))
                        addedfolders.Remove(s);
                    deletedfolders[s] = s;
                }

            if (System.IO.File.Exists(System.IO.Path.Combine(updatefolder, ADDED_FOLDERS)))
                foreach (string s in FilenamesFromPlatformIndependant(System.IO.File.ReadAllLines(System.IO.Path.Combine(updatefolder, ADDED_FOLDERS))))
                    addedfolders[s] = s;

            List<string> updates = Core.Utility.EnumerateFiles(System.IO.Path.Combine(updatefolder, SIGNATURE_ROOT));
            foreach(string s in updates)
            {
                string relpath = s.Substring(updatefolder.Length);
                string target = System.IO.Path.Combine(basefolder, relpath);

                if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(target)))
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));

                System.IO.File.Copy(s, target, true);
                //If they are created again
                relpath = relpath.Substring(SIGNATURE_ROOT.Length + 1);
                if (deletedfiles.ContainsKey(relpath))
                    deletedfiles.Remove(relpath);
            }

            List<string> delfiles = new List<string>(deletedfiles.Values);
            if (System.IO.File.Exists(System.IO.Path.Combine(updatefolder, DELETED_FILES)))
                foreach (string s in FilenamesFromPlatformIndependant(System.IO.File.ReadAllLines(System.IO.Path.Combine(updatefolder, DELETED_FILES))))
                    if (!delfiles.Contains(s))
                        delfiles.Add(s);

            if (!System.IO.Directory.Exists(basefolder))
                System.IO.Directory.CreateDirectory(basefolder);

            List<string> dfo = new List<string>(deletedfolders.Values);
            List<string> dfi = new List<string>(deletedfiles.Values);
            List<string> afo = new List<string>(addedfolders.Values);

            System.IO.File.WriteAllLines(System.IO.Path.Combine(basefolder, DELETED_FOLDERS), FilenamesToPlatformIndependant(dfo.ToArray()));
            System.IO.File.WriteAllLines(System.IO.Path.Combine(basefolder, DELETED_FILES), FilenamesToPlatformIndependant(dfi.ToArray()));
            System.IO.File.WriteAllLines(System.IO.Path.Combine(basefolder, ADDED_FOLDERS), FilenamesToPlatformIndependant(afo.ToArray()));
        }*/

        /// <summary>
        /// Converts all filenames to use / as the dir seperator
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
        /// Converts a list of filenames with / as the dir seperator to use the OS seperator.
        /// </summary>
        /// <param name="filenames">The list of filenames to convert</param>
        /// <param name="prefix">An optional prefix that is appended to the filenames</param>
        /// <returns>A list of filenames that use the prefix and the OS direseperator</returns>
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
        /// Converts a list of filenames with / as the dir seperator to use the OS seperator.
        /// </summary>
        /// <param name="filenames">The list of filenames to convert</param>
        /// <returns>A list of filenames that use the prefix and the OS direseperator</returns>
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
            return lst;
        }

        /// <summary>
        /// Extracts the files found in a signature volume
        /// </summary>
        /// <param name="patch">The signature volume to read</param>
        /// <returns>A list of file or folder names and their types</returns>
        public List<KeyValuePair<PatchFileType, string>> ListPatchFiles(Core.IFileArchive patch)
        {
            List<Core.IFileArchive> patches = new List<Duplicati.Library.Core.IFileArchive>();
            patches.Add(patch);
            return ListPatchFiles(patches);
        }

        /// <summary>
        /// Extracts the files found in a signature volume
        /// </summary>
        /// <param name="patchs">The signature volumes to read</param>
        /// <returns>A list of file or folder names and their types</returns>
        public List<KeyValuePair<PatchFileType, string>> ListPatchFiles(List<Core.IFileArchive> patches)
        {
            List<KeyValuePair<PatchFileType, string>> files = new List<KeyValuePair<PatchFileType, string>>();

            KeyValuePair<PatchFileType, string>[] signatures = new KeyValuePair<PatchFileType, string>[] {
                new KeyValuePair<PatchFileType, string>(PatchFileType.AddedOrUpdatedFile, Core.Utility.AppendDirSeperator(COMBINED_SIGNATURE_ROOT)),
                new KeyValuePair<PatchFileType, string>(PatchFileType.AddedFile, Core.Utility.AppendDirSeperator(CONTENT_SIGNATURE_ROOT)),
                new KeyValuePair<PatchFileType, string>(PatchFileType.UpdatedFile, Core.Utility.AppendDirSeperator(DELTA_SIGNATURE_ROOT)),
            };

            string content_prefix = Core.Utility.AppendDirSeperator(CONTENT_ROOT);
            string delta_prefix = Core.Utility.AppendDirSeperator(DELTA_ROOT);
            string control_prefix = Core.Utility.AppendDirSeperator(CONTROL_ROOT);
            Dictionary<string, bool> partials = new Dictionary<string, bool>();

            foreach (Core.IFileArchive arch in patches)
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
            private List<string> m_files = new List<string>();
            private List<string> m_folders = new List<string>();
            private List<string> m_errors = new List<string>();
            private List<string> m_filesWithError = new List<string>();
            private List<string> m_filesTooLarge = new List<string>();

            public void Callback(string rootpath, string path, Core.Utility.EnumeratedFileStatus status)
            {
                if (status == Core.Utility.EnumeratedFileStatus.Folder)
                    m_folders.Add(path);
                else if (status == Core.Utility.EnumeratedFileStatus.File)
                    m_files.Add(path);
                else if (status == Core.Utility.EnumeratedFileStatus.Error)
                    m_errors.Add(path);
            }

            public List<string> Files { get { return m_files; } }
            public List<string> Folders { get { return m_folders; } }
            public List<string> Errors { get { return m_errors; } }
            public List<string> FilesWithError { get { return m_filesWithError; } }
            public List<string> FilesTooLarge { get { return m_filesTooLarge; } }

            public bool IsAffectedByError(string path)
            {
                foreach (string s in m_errors)
                    if (path.StartsWith(s))
                        return true;

                return m_filesWithError.Contains(path);
            }
        }

    }
}
