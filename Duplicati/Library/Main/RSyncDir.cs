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
using System.IO;

namespace Duplicati.Library.Main.RSync
{
    /// <summary>
    /// This class wraps the process of creating a diff for an entire folder
    /// </summary>
    public class RSyncDir : IDisposable
    {
        internal static readonly string SIGNATURE_ROOT = "signature";
        internal static readonly string CONTENT_ROOT = "snapshot";
        internal static readonly string DELTA_ROOT = "diff";
        //private static readonly string DACL_ROOT = "dacl";

        internal static readonly string DELETED_FILES = "deleted_files.txt";
        internal static readonly string DELETED_FOLDERS = "deleted_folders.txt";

        internal static readonly string ADDED_FOLDERS = "added_folders.txt";

        /// <summary>
        /// This is the folder being backed up
        /// </summary>
        public string m_sourcefolder;

        /// <summary>
        /// This is a list of existing file signatures.
        /// Key is path to the file. 
        /// value is path to the signature file.
        /// </summary>
        private Dictionary<string, Core.IFileArchive> m_oldSignatures;
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
        /// This is a list of unprocessed files, used in multipass runs
        /// </summary>
        private List<string> m_unproccesed;

        public RSyncDir(string sourcefolder, CommunicationStatistics stat, Core.FilenameFilter filter, List<Core.IFileArchive> patches)
            : this(sourcefolder, stat, filter)
        {
            string prefix = Core.Utility.AppendDirSeperator(SIGNATURE_ROOT);

            foreach (Core.IFileArchive z in patches)
            {
                if (z.FileExists(DELETED_FILES))
                    foreach (string s in FilenamesFromPlatformIndependant(z.ReadAllLines(DELETED_FILES)))
                        if (m_oldSignatures.ContainsKey(s))
                            m_oldSignatures.Remove(s);

                foreach (string f in FilenamesFromPlatformIndependant(z.ListFiles(prefix)))
                    m_oldSignatures[f.Substring(prefix.Length)] = z;

                if (z.FileExists(DELETED_FOLDERS))
                    foreach (string s in FilenamesFromPlatformIndependant(z.ReadAllLines(DELETED_FOLDERS)))
                        if (m_oldFolders.ContainsKey(s))
                            m_oldFolders.Remove(s);

                if (z.FileExists(ADDED_FOLDERS))
                    foreach (string s in FilenamesFromPlatformIndependant(z.ReadAllLines(ADDED_FOLDERS)))
                        m_oldFolders.Add(s, s);
            }
        }

        public RSyncDir(string sourcefolder, CommunicationStatistics stat, Core.FilenameFilter filter)
        {
            if (!System.IO.Path.IsPathRooted(sourcefolder))
                sourcefolder = System.IO.Path.GetFullPath(sourcefolder);
            m_filter = filter;
            m_oldSignatures = new Dictionary<string, Duplicati.Library.Core.IFileArchive>();
            m_oldFolders = new Dictionary<string, string>();
            m_sourcefolder = Core.Utility.AppendDirSeperator(sourcefolder);
            m_stat = stat;

            if (m_filter == null)
                m_filter = new Duplicati.Library.Core.FilenameFilter(new List<KeyValuePair<bool, string>>());
        }

        public void CreatePatch(Core.IFileArchive signatures, Core.IFileArchive content)
        {
            InitiateMultiPassDiff(false);
            MakeMultiPassDiff(signatures, content, long.MaxValue);
            FinalizeMultiPass(signatures, content);
        }

        public void InitiateMultiPassDiff(bool full)
        {
            if (full)
            {
                m_oldFolders = new Dictionary<string, string>();
                m_oldSignatures = new Dictionary<string, Core.IFileArchive>();
            }

            m_newfiles = new Dictionary<string, string>();
            m_modifiedFiles = new Dictionary<string, string>();
            m_deletedfiles = new List<string>();
            m_newfolders = new List<string>();
            m_deletedfolders = new List<string>();

            //TODO: Figure out how to make this faster, but still random
            //Perhaps use itterative callbacks, with random recurse or itterate on each folder
            m_unproccesed = Core.Utility.EnumerateFileSystemEntries(m_sourcefolder, m_filter);
            m_totalfiles = m_unproccesed.Count;

            
            //Build folder diffs
            string dirmarker = System.IO.Path.DirectorySeparatorChar.ToString();
            for(int i = 0; i < m_unproccesed.Count; i++)
            {
                string s = m_unproccesed[i];
                if (s.EndsWith(dirmarker))
                {
                    string relpath = s.Substring(m_sourcefolder.Length);
                    if (!m_oldFolders.ContainsKey(relpath))
                        m_newfolders.Add(relpath);
                    else
                        m_oldFolders.Remove(relpath);

                    m_unproccesed.RemoveAt(i);
                    i--;
                }
            }

            m_deletedfolders = new List<string>();
            m_deletedfolders.AddRange(m_oldFolders.Keys);
        }

        public void FinalizeMultiPass(Core.IFileArchive signaturefile, Core.IFileArchive contentfile)
        {
            if (!m_finalized)
            {
                if (m_unproccesed.Count == 0)
                    m_deletedfiles.AddRange(m_oldSignatures.Keys);

                if (m_deletedfiles.Count > 0)
                {
                    signaturefile.WriteAllLines(DELETED_FILES, m_deletedfiles.ToArray());
                    contentfile.WriteAllLines(DELETED_FILES, m_deletedfiles.ToArray());
                }

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
                    bs.UnprocessedFiles = m_unproccesed.Count;
                    bs.AddedFolders = m_newfolders.Count;
                }

                m_finalized = true;
            }
        }

        public bool MakeMultiPassDiff(Core.IFileArchive signaturefile, Core.IFileArchive contentfile, long volumesize)
        {
            if (m_unproccesed == null)
                throw new Exception("Multi pass is not initialized");

            Random r = new Random();
            long totalSize = 0;

            while (m_unproccesed.Count > 0 && totalSize < volumesize)
            {
                int next = r.Next(0, m_unproccesed.Count);
                string s = m_unproccesed[next];
                m_unproccesed.RemoveAt(next);

                try
                {
                    using (System.IO.FileStream fs = System.IO.File.Open(s, FileMode.Open, FileAccess.Read, FileShare.Read))
                        if (ProccessDiff(fs, s, signaturefile))
                            totalSize = AddFileToCompression(fs, s, contentfile);
                }
                catch (Exception ex)
                {
                    if (m_stat != null)
                        m_stat.LogError("Failed to process file: \"" + s + "\", Error message: " + ex.Message);
                    Logging.Log.WriteMessage("Failed to process file: \"" + s + "\"", Duplicati.Library.Logging.LogMessageType.Error, ex);
                }
            }

            if (m_unproccesed.Count == 0)
                FinalizeMultiPass(signaturefile, contentfile);


            return m_unproccesed.Count == 0;
        }

        private bool ProccessDiff(System.IO.FileStream fs, string s, Core.IFileArchive signaturefile)
        {
            string relpath = s.Substring(m_sourcefolder.Length);
            string target = System.IO.Path.Combine(SIGNATURE_ROOT, relpath);

            using (System.IO.MemoryStream ms = new MemoryStream())
            {
                m_examinedfilesize += fs.Length;
                m_examinedfiles++;
                SharpRSync.Interface.GenerateSignature(fs, ms);

                if (!m_oldSignatures.ContainsKey(relpath))
                {
                    using(System.IO.Stream s2 = signaturefile.CreateFile(target))
                        Core.Utility.CopyStream(ms, s2, true);
                    m_newfiles.Add(s, null);
                    return true;
                }
                else
                {
                    ms.Position = 0;
                    bool equals;

                    
                    //File in archive
                    using (System.IO.Stream s2 = m_oldSignatures[relpath].OpenRead(target))
                        equals = Core.Utility.CompareStreams(s2, ms, false);

                    if (!equals)
                    {
                        //TODO: It's really bad if the signature is present, but not the content
                        using(System.IO.Stream s3 = signaturefile.CreateFile(target))
                            Core.Utility.CopyStream(ms, s3, true);
                        m_modifiedFiles.Add(s, null);
                        return true;
                    }
                    else
                    {
                        m_oldSignatures.Remove(relpath);
                        return false;
                    }
                }
            }
        }


        private long AddFileToCompression(System.IO.FileStream fs, string s, Core.IFileArchive contentfile)
        {
            fs.Position = 0;

            if (m_modifiedFiles.ContainsKey(s))
            {
                string relpath = s.Substring(m_sourcefolder.Length);
                string target = System.IO.Path.Combine(DELTA_ROOT, relpath);
                string signature = System.IO.Path.Combine(SIGNATURE_ROOT, relpath);

                using (System.IO.Stream sigfs = m_oldSignatures[relpath].OpenRead(signature))
                {
                    using(System.IO.Stream s3 = contentfile.CreateFile(target))
                        SharpRSync.Interface.GenerateDelta(sigfs, fs, s3);

                    //TODO: s3.Length is the size of the archive
                    //m_diffsize += s3.Length;
                    m_diffedfilessize += fs.Length;
                    m_diffedfiles++;
                }

                m_modifiedFiles.Remove(s);
                m_oldSignatures.Remove(relpath);
            }
            else
            {
                string relpath = s.Substring(m_sourcefolder.Length);
                string target = System.IO.Path.Combine(CONTENT_ROOT, relpath);

                using(System.IO.Stream s3 = contentfile.CreateFile(target))
                    Core.Utility.CopyStream(fs, s3);
                
                m_addedfiles++;
                m_addedfilessize += fs.Length;

                m_newfiles.Remove(s);
            }

            return contentfile.Size;
        }

        public bool HasChanges { get { return m_newfiles.Count > 0 || m_modifiedFiles.Count > 0; } }

        /*public List<string> EnumerateSourceFolders()
        {
            List<string> folders = Core.Utility.EnumerateFolders(m_sourcefolder);
            for (int i = 0; i < folders.Count; i++)
                folders[i] = System.IO.Path.Combine(System.IO.Path.Combine(m_targetfolder, SIGNATURE_ROOT), folders[i].Substring(m_sourcefolder.Length));
            
            return folders;
        }*/

        public void Restore(string destination, List<Core.IFileArchive> patches)
        {
            if (patches != null)
                foreach (Core.IFileArchive s in patches)
                    Patch(destination, s);
        }

        public void Patch(string destination, Core.IFileArchive patch)
        {
            destination = Core.Utility.AppendDirSeperator(destination);

            if (patch.FileExists(DELETED_FILES))
                foreach (string s in m_filter.FilterList("", FilenamesFromPlatformIndependant(patch.ReadAllLines(DELETED_FILES))))
                {
                    string target = System.IO.Path.Combine(destination, s.Trim());
                    if (System.IO.File.Exists(target))
                    {
                        try
                        {
                            //TODO: Perhaps read ahead in patches to prevent creation
                            System.IO.File.Delete(target);
                        }
                        catch (Exception ex)
                        {
                            if (m_stat != null)
                                m_stat.LogError("Failed to delete file: \"" + target + "\", Error message: " + ex.Message);
                            Logging.Log.WriteMessage("Failed to delete file: " + target, Duplicati.Library.Logging.LogMessageType.Warning, ex);
                        }
                    }
                    else
                    {
                        Logging.Log.WriteMessage("Filed marked for deletion did not exist: " + target, Duplicati.Library.Logging.LogMessageType.Warning);
                    }
                }

            if (patch.FileExists(DELETED_FOLDERS))
            {
                List<string> deletedfolders = m_filter.FilterList("", FilenamesFromPlatformIndependant(patch.ReadAllLines(DELETED_FOLDERS)));
                //Make sure subfolders are deleted first
                deletedfolders.Sort();
                deletedfolders.Reverse();

                foreach (string s in deletedfolders)
                {
                    string target = System.IO.Path.Combine(destination, s.Trim());
                    if (System.IO.Directory.Exists(target))
                        try
                        {
                            //TODO: Perhaps read ahead in patches to prevent creation
                            System.IO.Directory.Delete(target, false);
                        }
                        catch (Exception ex)
                        {
                            if (m_stat != null)
                                m_stat.LogError("Failed to remove folder: \"" + target + "\", Error message: " + ex.Message);
                            Logging.Log.WriteMessage("Failed to remove folder: " + target, Duplicati.Library.Logging.LogMessageType.Warning, ex);
                        }
                }
            }


            if (patch.FileExists(ADDED_FOLDERS))
            {
                List<string> addedfolders = m_filter.FilterList("", FilenamesFromPlatformIndependant(patch.ReadAllLines(ADDED_FOLDERS)));

                //Make sure topfolders are created first
                addedfolders.Sort();

                foreach (string s in addedfolders)
                {
                    string target = System.IO.Path.Combine(destination, s.Trim());
                    if (!System.IO.Directory.Exists(target))
                        try
                        {
                            System.IO.Directory.CreateDirectory(target);
                        }
                        catch (Exception ex)
                        {
                            if (m_stat != null)
                                m_stat.LogError("Failed to create folder: \"" + target + "\", Error message: " + ex.Message);
                            Logging.Log.WriteMessage("Failed to create folder: " + target, Duplicati.Library.Logging.LogMessageType.Warning, ex);
                        }
                }
            }

            string prefix = Core.Utility.AppendDirSeperator(CONTENT_ROOT);

            foreach (string s in m_filter.FilterList(prefix, patch.ListFiles(prefix)))
            {
                string target = System.IO.Path.Combine(destination, s.Substring(prefix.Length));
                try
                {
                    if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(target)))
                    {
                        Logging.Log.WriteMessage("Folder did not exist on restore " + target, Duplicati.Library.Logging.LogMessageType.Warning);
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                    }

                    using (System.IO.Stream s1 = patch.OpenRead(s))
                    using (System.IO.FileStream s2 = System.IO.File.Create(target))
                        Core.Utility.CopyStream(s1, s2);
                }
                catch (Exception ex)
                {
                    if (m_stat != null)
                        m_stat.LogError("Failed to restore file: \"" + s + "\", Error message: " + ex.Message);
                    Logging.Log.WriteMessage("Failed to restore file " + s, Duplicati.Library.Logging.LogMessageType.Error, ex);
                }

            }

            prefix = Core.Utility.AppendDirSeperator(DELTA_ROOT);
            foreach (string s in m_filter.FilterList(prefix, patch.ListFiles(prefix)))
            {
                string target = System.IO.Path.Combine(destination, s.Substring(prefix.Length));
                try
                {
                    if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(target)))
                    {
                        Logging.Log.WriteMessage("Folder did not exist on restore by delta " + target, Duplicati.Library.Logging.LogMessageType.Warning);
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                    }

                    using (Core.TempFile tempfile = new Core.TempFile())
                    {
                        using (System.IO.FileStream s1 = System.IO.File.OpenRead(target))
                        using (System.IO.Stream s2 = patch.OpenRead(s))
                        using (System.IO.FileStream s3 = System.IO.File.Create(tempfile))
                            SharpRSync.Interface.PatchFile(s1, s2, s3);

                        System.IO.File.Delete(target);
                        System.IO.File.Move(tempfile, target);
                    }
                }
                catch (Exception ex)
                {
                    if (m_stat != null)
                        m_stat.LogError("Failed to restore file: \"" + s + "\", Error message: " + ex.Message);
                    Logging.Log.WriteMessage("Failed to restore file " + s, Duplicati.Library.Logging.LogMessageType.Error, ex);

                    try { System.IO.File.Delete(target); }
                    catch { }
                }

            }
        }


        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion

        /// <summary>
        /// Compares two files to see if they are identical
        /// </summary>
        /// <param name="file1">One file</param>
        /// <param name="file2">Another file</param>
        /// <returns>True if they are binary equals, false otherwise</returns>
        private static bool CompareFiles(string file1, string file2)
        {
            using (System.IO.FileStream fs1 = new System.IO.FileStream(file1, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
            using (System.IO.FileStream fs2 = new System.IO.FileStream(file2, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                return Core.Utility.CompareStreams(fs1, fs2, true);
        }

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
        }

        public static string[] FilenamesToPlatformIndependant(string[] filenames)
        {
            if (System.IO.Path.DirectorySeparatorChar != '/')
                for (int i = 0; i < filenames.Length; i++)
                    filenames[i] = filenames[i].Replace(System.IO.Path.DirectorySeparatorChar, '/');

            return filenames;
        }

        public static string[] FilenamesFromPlatformIndependant(string[] filenames)
        {
            if (System.IO.Path.DirectorySeparatorChar != '/')
                for (int i = 0; i < filenames.Length; i++)
                    filenames[i] = filenames[i].Replace('/', System.IO.Path.DirectorySeparatorChar);

            return filenames;
        }
    }
}
