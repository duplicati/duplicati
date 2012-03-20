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

#if DEBUG

using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;

namespace Duplicati.CommandLine
{
    /// <summary>
    /// This class encapsulates a simple method for testing the correctness of duplicati.
    /// </summary>
    public class UnitTest
    {
        /// <summary>
        /// A helper class to write debug messages to the log file
        /// </summary>
        private class LogHelper : StreamLog
        {
			private string m_backupset;
			
            public string Backupset 
			{ 
				get { return m_backupset; }
				set { m_backupset = value; }
			}
			
            public LogHelper(string file)
                : base(file)
            {
                this.Backupset = "none";
            }

            public override void WriteMessage(string message, LogMessageType type, Exception exception)
            {
                base.WriteMessage(this.Backupset + ", " + message, type, exception);
            }
        }

        /// <summary>
        /// Running the unit test confirms the correctness of duplicati
        /// </summary>
        /// <param name="folders">The folders to backup. Folder at index 0 is the base, all others are incrementals</param>
        public static void RunTest(string[] folders, Dictionary<string, string> options)
        {
            //Place a file called "unittest_target.txt" in the bin folder, and enter a connection string like "ftp://username:password@example.com"

            string ftp_password = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "unittest_ftppassword.txt");
            if (System.IO.File.Exists(ftp_password))
                options["ftp-password"] = System.IO.File.ReadAllText(ftp_password).Trim();

            string alttarget = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "unittest_target.txt");
            if (System.IO.File.Exists(alttarget))
                RunTest(folders, options, System.IO.File.ReadAllText(alttarget).Trim());
            else
                RunTest(folders, options, null);
        }

        /// <summary>
        /// Running the unit test confirms the correctness of duplicati
        /// </summary>
        /// <param name="folders">The folders to backup. Folder at index 0 is the base, all others are incrementals</param>
        /// <param name="target">The target destination for the backups</param>
        public static void RunTest(string[] folders, Dictionary<string, string> options, string target)
        {
            LogHelper log = new LogHelper("unittest.log");
            Log.CurrentLog = log; ;
            Log.LogLevel = Duplicati.Library.Logging.LogMessageType.Profiling;

            string tempdir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "tempdir");

            try
            {
                if (System.IO.Directory.Exists(tempdir))
                    System.IO.Directory.Delete(tempdir, true);

                System.IO.Directory.CreateDirectory(tempdir);
            }
            catch(Exception ex)
            {
                Log.WriteMessage("Failed to clean tempdir", LogMessageType.Error, ex);
            }

            Duplicati.Library.Utility.TempFolder.SystemTempPath = tempdir;

            //Set some defaults
            if (!options.ContainsKey("passphrase"))
                options["passphrase"] = "secret password!";

            if (!options.ContainsKey("backup-prefix"))
                options["backup-prefix"] = "duplicati_unittest";

            //This would break the test, because the data is not modified the normal way
            options["disable-filetime-check"] = "true";
            //We do not use the same folder, so we need this option
            options["allow-sourcefolder-change"] = "true";
            //We want all messages in the log
            options["log-level"] = LogMessageType.Profiling.ToString();
            //We cannot rely on USN numbering, but we can use USN enumeration
            //options["disable-usn-diff-check"] = "true";
            
            //We use precise times
            options["disable-time-tolerance"] = "true";

            options["verification-level"] = "full";

            //We need all sets, even if they are unchanged
            options["upload-unchanged-backups"] = "true";

            using(new Timer("Total unittest"))
            using(TempFolder tf = new TempFolder())
            {
                //The code below tests for a race condition in the ssh backend.
                /*string[] list = null;
                string[] prevList = null;

                for (int i = 0; i < 1000; i++)
                {
                    Console.WriteLine(string.Format("Listing, test {0}", i));
                    list = Duplicati.Library.Main.Interface.List(target, options);
                    if (i != 0 && list.Length != prevList.Length)
                        Console.WriteLine(string.Format("Count mismatch {0} vs {1}", list.Length, prevList.Length));

                    prevList = list;
                }*/

                if (string.IsNullOrEmpty(target))
                {
                    target = "file://" + tf;
                }
                else
                {
                    Console.WriteLine("Removing old backups");
                    Dictionary<string, string> tmp = new Dictionary<string, string>(options);
                    tmp["delete-all-but-n-full"] = "0";
                    tmp["force"] = "";
                    tmp["allow-full-removal"] = "";

                    using (new Timer("Cleaning up any existing backups")) 
                        Console.WriteLine(Duplicati.Library.Main.Interface.DeleteAllButNFull(target, tmp));
                }

                log.Backupset = "Backup " + folders[0];
                Console.WriteLine("Backing up the full copy: " + folders[0]);
                using (new Timer("Full backup of " + folders[0]))
                {
                    options["full"] = "";
                    Log.WriteMessage(Duplicati.Library.Main.Interface.Backup(folders[0].Split(System.IO.Path.PathSeparator), target, options), LogMessageType.Information);
                    options.Remove("full");
                }

                for (int i = 1; i < folders.Length; i++)
                {
                    //options["passphrase"] = "bad password";
                    //If the backups are too close, we can't pick the right one :(
                    System.Threading.Thread.Sleep(1000 * 5);
                    log.Backupset = "Backup " + folders[i];
                    Console.WriteLine("Backing up the incremental copy: " + folders[i]);
                    using (new Timer("Incremental backup of " + folders[i]))
                        Log.WriteMessage(Duplicati.Library.Main.Interface.Backup(folders[i].Split(System.IO.Path.PathSeparator), target, options), LogMessageType.Information);
                }

                Duplicati.Library.Main.Options opts = new Duplicati.Library.Main.Options(options);
                using (Duplicati.Library.Interface.IBackend bk = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(target, options))
                    foreach (Duplicati.Library.Interface.IFileEntry fe in bk.List())
                        if (fe.Size > opts.VolumeSize)
                        {
                            string msg = string.Format("The file {0} is {1} bytes larger than allowed", fe.Name, fe.Size - opts.VolumeSize);
                            Console.WriteLine(msg);
                            Log.WriteMessage(msg, LogMessageType.Error);
                        }

                List<Duplicati.Library.Main.ManifestEntry> entries = Duplicati.Library.Main.Interface.ParseFileList(target, options);

                if (entries.Count != 1 || entries[0].Incrementals.Count != folders.Length - 1)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("Entry count: " + entries.Count.ToString());
                    if (entries.Count == 1)
                        sb.Append(string.Format("Found {0} incrementals but there were {1} source folders", entries[0].Incrementals.Count, folders.Length));
                    throw new Exception("Filename parsing problem, or corrupt storage: " + sb.ToString());
                }

                Console.WriteLine("Verifying the backup chain");
                using (new Timer("Verify backup"))
                {
                    List<KeyValuePair<Duplicati.Library.Main.BackupEntryBase, Exception>> results = Duplicati.Library.Main.Interface.VerifyBackup(target, options);
                    foreach (KeyValuePair<Duplicati.Library.Main.BackupEntryBase, Exception> x in results)
                        if (x.Value != null)
                            Console.WriteLine(string.Format("Error: {0}: {1}", x.Key.Filename, x.Value.ToString()));
                }

                List<Duplicati.Library.Main.ManifestEntry> t = new List<Duplicati.Library.Main.ManifestEntry>();
                t.Add(entries[0]);
                t.AddRange(entries[0].Incrementals);
                entries = t;

                for (int i = 0; i < entries.Count; i++)
                {
                    using (TempFolder ttf = new TempFolder())
                    {
                        log.Backupset = "Restore " + folders[i];
                        Console.WriteLine("Restoring the copy: " + folders[i]);

                        options["restore-time"] = entries[i].Time.ToString();

                        string[] actualfolders = folders[i].Split(System.IO.Path.PathSeparator);
                        string[] restorefoldernames;
                        if (actualfolders.Length == 1)
                            restorefoldernames = new string[] { ttf };
                        else
                        {
                            restorefoldernames = new string[actualfolders.Length];
                            for (int j = 0; j < actualfolders.Length; j++)
                                restorefoldernames[j] = System.IO.Path.Combine(ttf, System.IO.Path.GetFileName(actualfolders[j]));
                        }

                        Console.WriteLine("Partial restore of: " + folders[i]);
                        using (TempFolder ptf = new TempFolder())
                        {
                            List<string> testfiles = new List<string>();
                            using (new Timer("Extract list of files from" + folders[i]))
                            {
                                IList<string> sourcefiles = Duplicati.Library.Main.Interface.ListCurrentFiles(target, options);

                                //Remove all folders from list
                                for (int j = 0; j < sourcefiles.Count; j++)
                                    if (sourcefiles[j].EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
                                    {
                                        sourcefiles.RemoveAt(j);
                                        j--;
                                    }


                                int testfilecount = 15;
                                Random r = new Random();
                                while (testfilecount-- > 0 && sourcefiles.Count > 0)
                                {
                                    int rn = r.Next(0, sourcefiles.Count);
                                    testfiles.Add(sourcefiles[rn]);
                                    sourcefiles.RemoveAt(rn);
                                }
                            }

                            //Add all folders to avoid warnings in restore log
                            int c = testfiles.Count;
                            Dictionary<string, string> partialFolders = new Dictionary<string, string>(Utility.ClientFilenameStringComparer);
                            for (int j = 0; j < c; j++)
                            {
                                string f = testfiles[j];
                                do
                                {
                                    f = System.IO.Path.GetDirectoryName(f);
                                    partialFolders[Utility.AppendDirSeparator(f)] = null;
                                } while (f.IndexOf(System.IO.Path.DirectorySeparatorChar) > 0);
                            }

                            if (partialFolders.ContainsKey(""))
                                partialFolders.Remove("");

                            Dictionary<string, string> tops = new Dictionary<string,string>(options);
                            List<string> filterlist = new List<string>();
                            filterlist.AddRange(partialFolders.Keys);
                            filterlist.AddRange(testfiles);
                            tops["file-to-restore"] = String.Join(System.IO.Path.PathSeparator.ToString(), filterlist.ToArray());
                            
                            using (new Timer("Partial restore of " + folders[i]))
                                Log.WriteMessage(Duplicati.Library.Main.Interface.Restore(target, new string[] { ptf }, tops), LogMessageType.Information);

                            Console.WriteLine("Verifying partial restore of: " + folders[i]);
                            using (new Timer("Verification of partial restore from " + folders[i]))
                                foreach (string s in testfiles)
                                {
                                    string restoredname; 
                                    string sourcename;

                                    if (actualfolders.Length == 1) 
                                    {
                                        sourcename = System.IO.Path.Combine(actualfolders[0], s);
                                        restoredname = System.IO.Path.Combine(ptf, s);;
                                    }
                                    else
                                    {
                                        int six = s.IndexOf(System.IO.Path.DirectorySeparatorChar);
                                        sourcename = System.IO.Path.Combine(actualfolders[int.Parse(s.Substring(0, six))], s.Substring(six + 1));
                                        restoredname = System.IO.Path.Combine(System.IO.Path.Combine(ptf, System.IO.Path.GetFileName(folders[0].Split(System.IO.Path.PathSeparator)[int.Parse(s.Substring(0, six))])), s.Substring(six + 1));
                                    }

                                    if (!System.IO.File.Exists(restoredname))
                                    {
                                        Log.WriteMessage("Partial restore missing file: " + restoredname, LogMessageType.Error);
                                        Console.WriteLine("Partial restore missing file: " + restoredname);
                                    }
                                    else
                                    {
                                        if (!System.IO.File.Exists(sourcename))
                                        {
                                            Log.WriteMessage("Partial restore missing file: " + sourcename, LogMessageType.Error);
                                            Console.WriteLine("Partial restore missing file: " + sourcename);
                                            throw new Exception("Unittest is broken");
                                        }

                                        if (!CompareFiles(sourcename, restoredname, s))
                                        {
                                            Log.WriteMessage("Partial restore file differs: " + s, LogMessageType.Error);
                                            Console.WriteLine("Partial restore file differs: " + s);
                                        }
                                    }
                                }
                        }

                        using (new Timer("Restore of " + folders[i]))
                            Log.WriteMessage(Duplicati.Library.Main.Interface.Restore(target, restorefoldernames, options), LogMessageType.Information);

                        Console.WriteLine("Verifying the copy: " + folders[i]);

                        using (new Timer("Verification of " + folders[i]))
                        {
                            for (int j = 0; j < actualfolders.Length; j++)
                                VerifyDir(actualfolders[j], restorefoldernames[j]);
                        }
                    }
                }

            }

            (Log.CurrentLog as StreamLog).Dispose();
            Log.CurrentLog = null;
        }

        /// <summary>
        /// Returns the index of a given string, using the file system case sensitivity
        /// </summary>
        /// <returns>The index of the entry or -1 if no entry was found</returns>
        /// <param name='lst'>The list to search</param>
        /// <param name='m'>The string to find</param>
        private static int IndexOf(List<string> lst, string m)
        {
            StringComparison sc = Duplicati.Library.Utility.Utility.ClientFilenameStringComparision;
            for(int i = 0; i < lst.Count; i++)
                if (lst[i].Equals(m, sc))
                    return i;
            
            return -1;
        }
        
        /// <summary>
        /// Verifies the existence of all files and folders, and ensures that all
        /// files are binary equal.
        /// </summary>
        /// <param name="f1">One folder</param>
        /// <param name="f2">Another folder</param>
        private static void VerifyDir(string f1, string f2)
        {
            f1 = Utility.AppendDirSeparator(f1);
            f2 = Utility.AppendDirSeparator(f2);

            List<string> folders1 = Utility.EnumerateFolders(f1);
            List<string> folders2 = Utility.EnumerateFolders(f2);

            foreach (string s in folders1)
            {
                string relpath = s.Substring(f1.Length);
                string target = System.IO.Path.Combine(f2, relpath);
                int ix = IndexOf(folders2, target);
                if (ix < 0)
                {
                    Log.WriteMessage("Missing folder: " + relpath, LogMessageType.Error);
                    Console.WriteLine("Missing folder: " + relpath);
                }
                else
                    folders2.RemoveAt(ix);
            }

            foreach (string s in folders2)
            {
                Log.WriteMessage("Extra folder: " + s.Substring(f2.Length), LogMessageType.Error);
                Console.WriteLine("Extra folder: " + s.Substring(f2.Length));
            }

            List<string> files1 = Utility.EnumerateFiles(f1);
            List<string> files2 = Utility.EnumerateFiles(f2);
            foreach (string s in files1)
            {
                string relpath = s.Substring(f1.Length);
                string target = System.IO.Path.Combine(f2, relpath);
                int ix = IndexOf(files2, target);
                if (ix < 0)
                {
                    Log.WriteMessage("Missing file: " + relpath, LogMessageType.Error);
                    Console.WriteLine("Missing file: " + relpath);
                }
                else
                {
                    files2.RemoveAt(ix);
                    if (!CompareFiles(s, target, relpath))
                    {
                        Log.WriteMessage("File differs: " + relpath, LogMessageType.Error);
                        Console.WriteLine("File differs: " + relpath);
                    }
                }
            }

            foreach (string s in files2)
            {
                Log.WriteMessage("Extra file: " + s.Substring(f2.Length), LogMessageType.Error);
                Console.WriteLine("Extra file: " + s.Substring(f2.Length));
            }
        }

        /// <summary>
        /// Compares two files by reading all bytes, and comparing one by one
        /// </summary>
        /// <param name="f1">One file</param>
        /// <param name="f2">Another file</param>
        /// <param name="display">File display name</param>
        /// <returns>True if they are equal, false otherwise</returns>
        private static bool CompareFiles(string f1, string f2, string display)
        {
            using (System.IO.FileStream fs1 = System.IO.File.OpenRead(f1))
            using (System.IO.FileStream fs2 = System.IO.File.OpenRead(f2))
                if (fs1.Length != fs2.Length)
                {
                    Log.WriteMessage("Lengths differ: " + display + ", " + fs1.Length.ToString() + " vs. " + fs2.Length.ToString(), LogMessageType.Error);
                    Console.WriteLine("Lengths differ: " + display + ", " + fs1.Length.ToString() + " vs. " + fs2.Length.ToString());
                    return false;
                }
                else
                {
                    long len = fs1.Length;
                    for (long l = 0; l < len; l++)
                        if (fs1.ReadByte() != fs2.ReadByte())
                        {
                            Log.WriteMessage("Mismatch in byte " + l.ToString() + " in file " + display, LogMessageType.Error);
                            Console.WriteLine("Mismatch in byte " + l.ToString() + " in file " + display);
                            return false;
                        }
                }

            return true;
        }
    }
}

#endif