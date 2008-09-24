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

namespace Duplicati.Main.RSync
{
    /// <summary>
    /// This class wraps the process of creating a diff for an entire folder
    /// </summary>
    public class RSyncDir : IDisposable
    {
        private static readonly string SIGNATURE_ROOT = "signature";
        private static readonly string CONTENT_ROOT = "content";

        private static readonly string SIGNATURE_FOLDER = SIGNATURE_ROOT + Path.DirectorySeparatorChar + "signatures";

        private static readonly string NEW_FOLDERS_FILE = SIGNATURE_ROOT + Path.DirectorySeparatorChar + "newfolders";
        private static readonly string DELETED_FOLDERS_FILE = SIGNATURE_ROOT + Path.DirectorySeparatorChar + "deletedfolders";
        private static readonly string DELETED_FILES_FILE = SIGNATURE_ROOT + Path.DirectorySeparatorChar + "deletedfiles";
        private static readonly string DELTA_FOLDER = CONTENT_ROOT + Path.DirectorySeparatorChar + "delta";
        private static readonly string BASE_FOLDER = CONTENT_ROOT + Path.DirectorySeparatorChar + "base";

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

        public RSyncDir(string sourcefolder, string basefolder)
        {
            m_sourcefolder = Core.Utility.AppendDirSeperator(sourcefolder);
            m_basefolder = Core.Utility.AppendDirSeperator(basefolder);
            m_targetfolder = new Duplicati.Core.TempFolder();

            PrepareTargetFolder();
            BuildIndex();
        }

        private void PrepareTargetFolder()
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(m_targetfolder, BASE_FOLDER));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(m_targetfolder, DELTA_FOLDER));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(m_targetfolder, SIGNATURE_FOLDER));
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
            string sigfolder = Core.Utility.AppendDirSeperator(System.IO.Path.Combine(m_basefolder, SIGNATURE_FOLDER));

            foreach (string s in Core.Utility.EnumerateFiles(sigfolder))
                m_oldSignatures.Add(s.Substring(sigfolder.Length), s);

            m_oldFolders = new Dictionary<string, string>();
            if (System.IO.File.Exists(System.IO.Path.Combine(m_basefolder, NEW_FOLDERS_FILE)))
                foreach (string s in System.IO.File.ReadAllLines(System.IO.Path.Combine(m_basefolder, NEW_FOLDERS_FILE)))
                    if (s.Trim().Length > 0)
                        m_oldFolders.Add(s, null);
        }

        public void CalculateDiffFilelist(bool full, Core.FilenameFilter filter)
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

            foreach (string s in Core.Utility.EnumerateFiles(m_sourcefolder, filter))
            {
                string relpath = s.Substring(m_sourcefolder.Length);
                string target = System.IO.Path.Combine(System.IO.Path.Combine(m_targetfolder, SIGNATURE_FOLDER), relpath);
                if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(target)))
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));

                RSync.RDiffWrapper.GenerateSignature(s, target);

                if (!m_oldSignatures.ContainsKey(relpath))
                    m_newfiles.Add(s, target);
                else
                {
                    if (!CompareFiles(m_oldSignatures[relpath], target))
                        m_modifiedFiles.Add(s, target);
                    else
                        System.IO.File.Delete(target);
                    m_oldSignatures.Remove(relpath);
                }
            }

            m_deletedfiles.AddRange(m_oldSignatures.Keys);

            foreach (string s in Core.Utility.EnumerateFolders(m_sourcefolder, filter))
            {
                string relpath = s.Substring(m_sourcefolder.Length);
                if (!m_oldFolders.ContainsKey(s))
                    m_newfolders.Add(relpath);
                else
                    m_oldFolders.Remove(relpath);
            }

            m_deletedfolders = new List<string>();
            m_deletedfolders.AddRange(m_oldFolders.Keys);

        }

        public void CreateDeltas(string zipfile, long volumesize)
        {
            long totalSize = 0;
            Random r = new Random();
            string[] tmp = new string[m_modifiedFiles.Keys.Count];
            m_modifiedFiles.Keys.CopyTo(tmp, 0);
            List<string> keys = new List<string>(tmp);
            tmp = new string[m_newfiles.Keys.Count];
            m_newfiles.Keys.CopyTo(tmp, 0);
            keys.AddRange(tmp);

            using (Compression.Compression c = new Duplicati.Compression.Compression(m_targetfolder, zipfile))
            {

                while (keys.Count > 0 && totalSize < volumesize)
                {
                    int index = r.Next(0, keys.Count);
                    string s = keys[index];
                    keys.RemoveAt(index);

                    if (m_modifiedFiles.ContainsKey(s))
                    {

                        string relpath = s.Substring(m_sourcefolder.Length);
                        string target = System.IO.Path.Combine(System.IO.Path.Combine(m_targetfolder, DELTA_FOLDER), relpath);
                        string signature = System.IO.Path.Combine(System.IO.Path.Combine(m_targetfolder, SIGNATURE_FOLDER), relpath);
                        if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(target)))
                            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));

                        RSync.RDiffWrapper.GenerateDelta(signature, s, target);

                        totalSize = c.AddFile(target);
                        System.IO.File.Delete(target);
                        m_modifiedFiles.Remove(s);
                    }
                    else
                    {
                        string relpath = s.Substring(m_sourcefolder.Length);
                        string target = System.IO.Path.Combine(System.IO.Path.Combine(m_targetfolder, BASE_FOLDER), relpath);

                        System.IO.File.Copy(s, target);
                        totalSize = c.AddFile(target);
                        System.IO.File.Delete(target);
                        m_newfiles.Remove(s);
                    }
                }

                if (m_newfolders.Count > 0)
                {
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(m_targetfolder, NEW_FOLDERS_FILE), m_newfolders.ToArray());
                    c.AddFile(System.IO.Path.Combine(m_targetfolder, NEW_FOLDERS_FILE));
                }
                if (m_deletedfolders.Count > 0)
                {
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(m_targetfolder, DELETED_FOLDERS_FILE), m_deletedfolders.ToArray());
                    c.AddFile(System.IO.Path.Combine(m_targetfolder, DELETED_FOLDERS_FILE));
                }
                if (m_modifiedFiles.Count > 0)
                {
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(m_targetfolder, DELETED_FILES_FILE), m_deletedfiles.ToArray());
                    c.AddFile(System.IO.Path.Combine(m_targetfolder, DELETED_FILES_FILE));
                }
            }

            m_newfolders = new List<string>();
            m_deletedfiles = new List<string>();
            m_deletedfolders = new List<string>();
        }

        public bool HasChanges { get { return m_newfiles.Count > 0 || m_modifiedFiles.Count > 0; } }

        public void CreateDeltas()
        {
            foreach (string s in m_modifiedFiles.Keys)
            {
                string relpath = s.Substring(m_sourcefolder.Length);
                string target = System.IO.Path.Combine(System.IO.Path.Combine(m_targetfolder, DELTA_FOLDER), relpath);
                string signature = System.IO.Path.Combine(System.IO.Path.Combine(m_targetfolder, SIGNATURE_FOLDER), relpath);
                if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(target)))
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));

                RSync.RDiffWrapper.GenerateDelta(signature, s, target);
            }

            foreach (string s in m_newfiles.Keys)
            {
                string relpath = s.Substring(m_sourcefolder.Length);
                string target = System.IO.Path.Combine(System.IO.Path.Combine(m_targetfolder, BASE_FOLDER), relpath);

                System.IO.File.Copy(s, target);
            }

            System.IO.File.WriteAllLines(System.IO.Path.Combine(m_targetfolder, NEW_FOLDERS_FILE), m_newfolders.ToArray());
            System.IO.File.WriteAllLines(System.IO.Path.Combine(m_targetfolder, DELETED_FOLDERS_FILE), m_deletedfolders.ToArray());
            System.IO.File.WriteAllLines(System.IO.Path.Combine(m_targetfolder, DELETED_FILES_FILE), m_deletedfiles.ToArray());

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
            string sourcefolder = Core.Utility.AppendDirSeperator(System.IO.Path.Combine(patch, BASE_FOLDER));

            destination = Core.Utility.AppendDirSeperator(destination);

            foreach (string s in Core.Utility.EnumerateFiles(sourcefolder))
            {
                string relpath = s.Substring(sourcefolder.Length);
                string target = System.IO.Path.Combine(destination, relpath);
                if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(target)))
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                System.IO.File.Copy(s, target);
            }

            sourcefolder = Core.Utility.AppendDirSeperator(System.IO.Path.Combine(patch, DELTA_FOLDER));

            foreach (string s in Core.Utility.EnumerateFiles(sourcefolder))
            {
                string relpath = s.Substring(sourcefolder.Length);
                string target = System.IO.Path.Combine(destination, relpath);
                if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(target)))
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));

                string tempfile = System.IO.Path.GetTempFileName();
                RSync.RDiffWrapper.PatchFile(target, s, tempfile);
                System.IO.File.Delete(target);
                System.IO.File.Move(tempfile, target);
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
            System.Security.Cryptography.SHA1 sha = System.Security.Cryptography.SHA1.Create();

            using (System.IO.FileStream fs1 = new System.IO.FileStream(file1, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
            using (System.IO.FileStream fs2 = new System.IO.FileStream(file2, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                if (fs1.Length != fs2.Length)
                    return false;
                else
                    return Convert.ToBase64String(sha.ComputeHash(fs1)) == Convert.ToBase64String(sha.ComputeHash(fs2));
        }

        public static void MergeSignatures(string basefolder, string updatefolder)
        {
            basefolder = Core.Utility.AppendDirSeperator(basefolder);
            updatefolder = Core.Utility.AppendDirSeperator(updatefolder);

            Dictionary<string, string> deletedfiles = new Dictionary<string, string>();
            if (System.IO.File.Exists(System.IO.Path.Combine(basefolder, DELETED_FILES_FILE)))
                foreach (string s in System.IO.File.ReadAllLines(System.IO.Path.Combine(basefolder, DELETED_FILES_FILE)))
                    deletedfiles.Add(s, s);

            if (System.IO.File.Exists(System.IO.Path.Combine(updatefolder, DELETED_FILES_FILE)))
                foreach (string s in System.IO.File.ReadAllLines(System.IO.Path.Combine(updatefolder, DELETED_FILES_FILE)))
                    System.IO.File.Delete(System.IO.Path.Combine(System.IO.Path.Combine(basefolder, SIGNATURE_ROOT), s));

            List<string> updates = Core.Utility.EnumerateFiles(System.IO.Path.Combine(updatefolder, SIGNATURE_FOLDER));
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

            List<string> newfolders = new List<string>();
            if (System.IO.File.Exists(System.IO.Path.Combine(basefolder, NEW_FOLDERS_FILE)))
                newfolders.AddRange(System.IO.File.ReadAllLines(System.IO.Path.Combine(basefolder, NEW_FOLDERS_FILE)));

            List<string> deltedfolders = new List<string>();
            if (System.IO.File.Exists(System.IO.Path.Combine(basefolder, DELETED_FOLDERS_FILE)))
                newfolders.AddRange(System.IO.File.ReadAllLines(System.IO.Path.Combine(basefolder, DELETED_FOLDERS_FILE)));

            if (System.IO.File.Exists(System.IO.Path.Combine(updatefolder, NEW_FOLDERS_FILE)))
                foreach (string s in System.IO.File.ReadAllLines(System.IO.Path.Combine(updatefolder, NEW_FOLDERS_FILE)))
                {
                    if (!newfolders.Contains(s))
                        newfolders.Add(s);
                    if (deltedfolders.Contains(s))
                        deltedfolders.Remove(s);
                }

            if (System.IO.File.Exists(System.IO.Path.Combine(updatefolder, DELETED_FOLDERS_FILE)))
                foreach (string s in System.IO.File.ReadAllLines(System.IO.Path.Combine(updatefolder, DELETED_FOLDERS_FILE)))
                {
                    if (newfolders.Contains(s))
                        newfolders.Remove(s);
                    if (!deltedfolders.Contains(s))
                        deltedfolders.Add(s);
                }

            List<string> delfiles = new List<string>(deletedfiles.Values);
            if (System.IO.File.Exists(System.IO.Path.Combine(updatefolder, DELETED_FILES_FILE)))
                foreach (string s in System.IO.File.ReadAllLines(System.IO.Path.Combine(updatefolder, DELETED_FILES_FILE)))
                    if (!delfiles.Contains(s))
                        delfiles.Add(s);

            if (!System.IO.Directory.Exists(basefolder))
                System.IO.Directory.CreateDirectory(basefolder);

            System.IO.File.WriteAllLines(System.IO.Path.Combine(basefolder, NEW_FOLDERS_FILE), newfolders.ToArray());
            System.IO.File.WriteAllLines(System.IO.Path.Combine(basefolder, DELETED_FOLDERS_FILE), deltedfolders.ToArray());
            System.IO.File.WriteAllLines(System.IO.Path.Combine(basefolder, DELETED_FILES_FILE), delfiles.ToArray());

        }

        public string NewSignatures { get { return System.IO.Path.Combine(m_targetfolder, SIGNATURE_ROOT); } }
        public string NewDeltas { get { return System.IO.Path.Combine(m_targetfolder, CONTENT_ROOT); } }

    }
}
