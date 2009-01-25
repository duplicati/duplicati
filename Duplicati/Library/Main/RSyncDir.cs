#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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
        private static readonly string SIGNATURE_ROOT = "signature";
        private static readonly string CONTENT_ROOT = "snapshot";
        private static readonly string DELTA_ROOT = "diff";
        //private static readonly string DACL_ROOT = "dacl";

        private static readonly string DELETED_FILES = "deleted_files.txt";
        private static readonly string DELETED_FOLDERS = "deleted_folders.txt";

        /// <summary>
        /// This is the folder being backed up
        /// </summary>
        public string m_sourcefolder;
        /// <summary>
        /// This is the folder containing signatures for the previous backup
        /// </summary>
        public string m_basefolder;
        /// <summary>
        /// This is the folder that gets filled with the next backup
        /// </summary>
        public Core.TempFolder m_targetfolder;

        /// <summary>
        /// This is a list of existing file signatures.
        /// Key is path to the file. 
        /// value is path to the signature file.
        /// </summary>
        private Dictionary<string, string> m_oldSignatures;
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

        public RSyncDir(string sourcefolder, string basefolder, CommunicationStatistics stat)
        {
            if (!System.IO.Path.IsPathRooted(sourcefolder))
                sourcefolder = System.IO.Path.GetFullPath(sourcefolder);
            if (!System.IO.Path.IsPathRooted(basefolder))
                sourcefolder = System.IO.Path.GetFullPath(basefolder);
            m_sourcefolder = Core.Utility.AppendDirSeperator(sourcefolder);
            m_basefolder = Core.Utility.AppendDirSeperator(basefolder);
            m_targetfolder = new Duplicati.Library.Core.TempFolder();
            m_stat = stat;

            PrepareTargetFolder();
            BuildIndex();
        }

        private void PrepareTargetFolder()
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(m_targetfolder, SIGNATURE_ROOT));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(m_targetfolder, CONTENT_ROOT));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(m_targetfolder, DELTA_ROOT));
        }

        private void BuildIndex()
        {
            if (m_basefolder == null || !System.IO.Directory.Exists(m_basefolder))
            {
                m_oldSignatures = new Dictionary<string, string>();
                m_oldFolders = new Dictionary<string, string>();
                return;
            }
            m_basefolder = Core.Utility.AppendDirSeperator(m_basefolder);

            m_oldSignatures = new Dictionary<string,string>();
            string sigfolder = Core.Utility.AppendDirSeperator(System.IO.Path.Combine(m_basefolder, SIGNATURE_ROOT));

            foreach (string s in Core.Utility.EnumerateFiles(sigfolder))
                m_oldSignatures.Add(s.Substring(sigfolder.Length), s);

            m_oldFolders = new Dictionary<string, string>();
            foreach (string s in Core.Utility.EnumerateFolders(sigfolder))
                m_oldFolders.Add(s.Substring(sigfolder.Length), null);

            
        }

        public void InitiateMultiPassDiff(bool full, Core.FilenameFilter filter)
        {
            if (full)
            {
                m_oldFolders = new Dictionary<string, string>();
                m_oldSignatures = new Dictionary<string, string>();
            }

            m_newfiles = new Dictionary<string, string>();
            m_modifiedFiles = new Dictionary<string, string>();
            m_deletedfiles = new List<string>();
            m_newfolders = new List<string>();
            m_deletedfolders = new List<string>();

            m_unproccesed = Core.Utility.EnumerateFiles(m_sourcefolder, filter);
            m_totalfiles = m_unproccesed.Count;

            foreach (string s in Core.Utility.EnumerateFolders(m_sourcefolder, filter))
            {
                string relpath = s.Substring(m_sourcefolder.Length);
                if (!m_oldFolders.ContainsKey(relpath))
                    m_newfolders.Add(relpath);
                else
                    m_oldFolders.Remove(relpath);

            }

            m_deletedfolders = new List<string>();
            m_deletedfolders.AddRange(m_oldFolders.Keys);
        }

        public void FinalizeMultiPass(Compression.Compression sigfile)
        {
            if (!m_finalized)
            {
                m_deletedfiles.AddRange(m_oldSignatures.Keys);
                if (m_deletedfiles.Count > 0)
                {
                    System.IO.StreamWriter sw = new StreamWriter(sigfile.AddStream(DELETED_FILES));
                    foreach (string s in m_deletedfiles)
                        sw.WriteLine(s);

                    sw.Flush();
                }

                if (m_deletedfolders.Count > 0)
                {
                    System.IO.StreamWriter sw = new StreamWriter(sigfile.AddStream(DELETED_FOLDERS));
                    foreach (string s in m_deletedfolders)
                        sw.WriteLine(s);

                    sw.Flush();
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
                }

                m_finalized = true;
            }
        }

        public bool MakeMultiPassDiff(Compression.Compression sigfile, string zipfile, long volumesize)
        {
            if (m_unproccesed == null)
                throw new Exception("Multi pass is not initialized");

            Random r = new Random();
            long totalSize = 0;

            using (Compression.Compression c = new Duplicati.Library.Compression.Compression(m_targetfolder, zipfile))
            {
                while (m_unproccesed.Count > 0 && totalSize < volumesize)
                {
                    int next = r.Next(0, m_unproccesed.Count);
                    string s = m_unproccesed[next];
                    m_unproccesed.RemoveAt(next);

                    try
                    {
                        using (System.IO.FileStream fs = System.IO.File.Open(s, FileMode.Open, FileAccess.Read, FileShare.Read))
                            if (ProccessDiff(fs, s, sigfile))
                                totalSize = AddFileToCompression(fs, s, c);
                    }
                    catch (Exception ex)
                    {
                        if (m_stat != null)
                            m_stat.LogError("Failed to process file: \"" + s + "\", Error message: " + ex.Message);
                        Logging.Log.WriteMessage("Failed to process file: \"" + s + "\"", Duplicati.Library.Logging.LogMessageType.Error, ex);                        
                    }
                }

                if (m_unproccesed.Count == 0)
                    FinalizeMultiPass(sigfile);
            }

            return m_unproccesed.Count == 0;
        }

        private bool ProccessDiff(System.IO.FileStream fs, string s, Compression.Compression sigfile)
        {
            string relpath = s.Substring(m_sourcefolder.Length);
            string target = System.IO.Path.Combine(System.IO.Path.Combine(m_targetfolder, SIGNATURE_ROOT), relpath);

            using (System.IO.MemoryStream ms = new MemoryStream())
            {
                m_examinedfilesize += fs.Length;
                m_examinedfiles++;
                SharpRSync.Interface.GenerateSignature(fs, ms);

                if (!m_oldSignatures.ContainsKey(relpath))
                {
                    Core.Utility.CopyStream(ms, sigfile.AddStream(target), true);
                    m_newfiles.Add(s, null);
                    return true;
                }
                else
                {
                    ms.Position = 0;
                    bool equals;

                    using (System.IO.FileStream fs2 = System.IO.File.OpenRead(m_oldSignatures[relpath]))
                        equals = Core.Utility.CompareStreams(fs2, ms, true);

                    if (!equals)
                    {
                        Core.Utility.CopyStream(ms, sigfile.AddStream(target), true);
                        m_modifiedFiles.Add(s, null);
                        m_oldSignatures.Remove(relpath);
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


        private long AddFileToCompression(System.IO.FileStream fs, string s, Compression.Compression c)
        {
            fs.Position = 0;

            if (m_modifiedFiles.ContainsKey(s))
            {
                string relpath = s.Substring(m_sourcefolder.Length);
                string target = System.IO.Path.Combine(System.IO.Path.Combine(m_targetfolder, DELTA_ROOT), relpath);
                string signature = System.IO.Path.Combine(System.IO.Path.Combine(m_basefolder, SIGNATURE_ROOT), relpath);

                using (System.IO.FileStream sigfs = System.IO.File.OpenRead(signature))
                {
                    System.IO.Stream s3 = c.AddStream(target);
                    SharpRSync.Interface.GenerateDelta(sigfs, fs, s3);
                    //TODO: s3.Length the size of the archive
                    //m_diffsize += s3.Length;
                    m_diffedfilessize += fs.Length;
                    m_diffedfiles++;
                }

                m_modifiedFiles.Remove(s);
            }
            else
            {
                string relpath = s.Substring(m_sourcefolder.Length);
                string target = System.IO.Path.Combine(System.IO.Path.Combine(m_targetfolder, CONTENT_ROOT), relpath);

                Core.Utility.CopyStream(fs, c.AddStream(target));
                m_addedfiles++;
                m_addedfilessize += fs.Length;

                m_newfiles.Remove(s);
            }
            return c.Size;
        }

        public bool HasChanges { get { return m_newfiles.Count > 0 || m_modifiedFiles.Count > 0; } }

        public List<string> EnumerateSourceFolders()
        {
            List<string> folders = Core.Utility.EnumerateFolders(m_sourcefolder);
            for (int i = 0; i < folders.Count; i++)
                folders[i] = System.IO.Path.Combine(System.IO.Path.Combine(m_targetfolder, SIGNATURE_ROOT), folders[i].Substring(m_sourcefolder.Length));
            
            return folders;
        }

        public void Restore(string destination, List<string> patchfolders)
        {
            Patch(destination, m_basefolder);
            
            if (patchfolders != null)
                foreach (string s in patchfolders)
                    Patch(destination, s);
        }

        public void Patch(string destination, string patch)
        {
            string deletedfiles = System.IO.Path.Combine(patch, DELETED_FILES);
            if (System.IO.File.Exists(deletedfiles))
                foreach (string s in System.IO.File.ReadAllLines(deletedfiles))
                {
                    string target = System.IO.Path.Combine(destination, s.Trim());
                    if (System.IO.File.Exists(target))
                    {
                        try
                        {
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

            string deletedfolders = System.IO.Path.Combine(patch, DELETED_FOLDERS);
            if (System.IO.File.Exists(deletedfolders))
            {
                //Make sure subfolders are deleted first
                string[] folderlist = System.IO.File.ReadAllLines(deletedfolders);
                Array.Sort(folderlist);
                Array.Reverse(folderlist);

                foreach (string s in folderlist)
                {
                    string target = System.IO.Path.Combine(destination, s.Trim());
                    if (System.IO.Directory.Exists(target))
                        try
                        {
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

            string foldersource = Core.Utility.AppendDirSeperator(System.IO.Path.Combine(patch, SIGNATURE_ROOT));
            foreach (string s in Core.Utility.EnumerateFolders(foldersource))
            {
                string target = System.IO.Path.Combine(destination, s.Substring(foldersource.Length));
                if (!System.IO.Directory.Exists(target))
                    System.IO.Directory.CreateDirectory(target);
            }



            string sourcefolder = Core.Utility.AppendDirSeperator(System.IO.Path.Combine(patch, CONTENT_ROOT));

            destination = Core.Utility.AppendDirSeperator(destination);

            //TODO: Handle file access exceptions
            foreach (string s in Core.Utility.EnumerateFiles(sourcefolder))
            {
                string relpath = s.Substring(sourcefolder.Length);
                string target = System.IO.Path.Combine(destination, relpath);
                try
                {
                    System.IO.File.Copy(s, target);
                }
                catch (Exception ex)
                {
                        if (m_stat != null)
                            m_stat.LogError("Failed to restore file: \"" + relpath + "\", Error message: " + ex.Message);
                    Logging.Log.WriteMessage("Failed to restore file " + relpath, Duplicati.Library.Logging.LogMessageType.Error, ex);
                }

            }

            sourcefolder = Core.Utility.AppendDirSeperator(System.IO.Path.Combine(patch, DELTA_ROOT));

            foreach (string s in Core.Utility.EnumerateFiles(sourcefolder))
            {
                string relpath = s.Substring(sourcefolder.Length);
                string target = System.IO.Path.Combine(destination, relpath);
                //string source = System.IO.Path.Combine(sourcefolder, relpath)

                string tempfile = System.IO.Path.GetTempFileName();
                try
                {
                    SharpRSync.Interface.PatchFile(target, s, tempfile);
                    System.IO.File.Delete(target);
                    System.IO.File.Move(tempfile, target);
                }
                catch (Exception ex)
                {
                        if (m_stat != null)
                            m_stat.LogError("Failed to restore file: \"" + relpath + "\", Error message: " + ex.Message);
                    Logging.Log.WriteMessage("Failed to restore file " + relpath, Duplicati.Library.Logging.LogMessageType.Error, ex);
                    System.IO.File.Delete(target);
                    System.IO.File.Delete(tempfile);
                }

            }
        }


        #region IDisposable Members

        public void Dispose()
        {
            if (m_targetfolder != null)
            {
                m_targetfolder.Dispose();
                m_targetfolder = null;
            }
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
                foreach (string s in System.IO.File.ReadAllLines(System.IO.Path.Combine(basefolder, DELETED_FILES)))
                    deletedfiles.Add(s, s);

            if (System.IO.File.Exists(System.IO.Path.Combine(updatefolder, DELETED_FILES)))
                foreach (string s in System.IO.File.ReadAllLines(System.IO.Path.Combine(updatefolder, DELETED_FILES)))
                    System.IO.File.Delete(System.IO.Path.Combine(System.IO.Path.Combine(basefolder, SIGNATURE_ROOT), s));

            if (System.IO.File.Exists(System.IO.Path.Combine(updatefolder, DELETED_FOLDERS)))
                foreach (string s in System.IO.File.ReadAllLines(System.IO.Path.Combine(updatefolder, DELETED_FOLDERS)))
                    if (System.IO.Directory.Exists(System.IO.Path.Combine(System.IO.Path.Combine(basefolder, SIGNATURE_ROOT), s)))
                        System.IO.Directory.Delete(System.IO.Path.Combine(System.IO.Path.Combine(basefolder, SIGNATURE_ROOT), s), true);

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

            List<string> deltedfolders = new List<string>();
            if (System.IO.File.Exists(System.IO.Path.Combine(basefolder, DELETED_FOLDERS)))
                deltedfolders.AddRange(System.IO.File.ReadAllLines(System.IO.Path.Combine(basefolder, DELETED_FOLDERS)));

            if (System.IO.File.Exists(System.IO.Path.Combine(updatefolder, DELETED_FOLDERS)))
                foreach (string s in System.IO.File.ReadAllLines(System.IO.Path.Combine(updatefolder, DELETED_FOLDERS)))
                    if (!deltedfolders.Contains(s))
                        deltedfolders.Add(s);

            List<string> delfiles = new List<string>(deletedfiles.Values);
            if (System.IO.File.Exists(System.IO.Path.Combine(updatefolder, DELETED_FILES)))
                foreach (string s in System.IO.File.ReadAllLines(System.IO.Path.Combine(updatefolder, DELETED_FILES)))
                    if (!delfiles.Contains(s))
                        delfiles.Add(s);

            if (!System.IO.Directory.Exists(basefolder))
                System.IO.Directory.CreateDirectory(basefolder);

            System.IO.File.WriteAllLines(System.IO.Path.Combine(basefolder, DELETED_FOLDERS), deltedfolders.ToArray());
            System.IO.File.WriteAllLines(System.IO.Path.Combine(basefolder, DELETED_FILES), delfiles.ToArray());

        }

    }
}
