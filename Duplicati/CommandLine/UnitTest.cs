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

#if DEBUG

using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Library.Logging;
using Duplicati.Library.Core;

namespace Duplicati.CommandLine
{
    /// <summary>
    /// This class encapsulates a simple method for testing the correctness of duplicati.
    /// </summary>
    public class UnitTest
    {
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
            Log.CurrentLog = new StreamLog("unittest.log");
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

            Duplicati.Library.Core.TempFolder.SystemTempPath = tempdir;

            //Set some defaults
            if (!options.ContainsKey("passphrase"))
                options["passphrase"] = "secret password!";

            if (!options.ContainsKey("backup-prefix"))
                options["backup-prefix"] = "duplicati_unittest";

            //This would break the test, because the data is not modified the normal way
            options["disable-filetime-check"] = null;

            using(new Timer("Total unittest"))
            using(TempFolder tf = new TempFolder())
            {
                //Actually this is supposed to be the remote server's OS, not the client OS
                if (System.Environment.OSVersion.Platform != PlatformID.Unix)
                    options["time-separator"] = "'";

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
                    tmp["remove-all-but-n-full"] = "0";
                    tmp["force"] = "";
                    using (new Timer("Cleaning up any existing backups")) 
                        Console.WriteLine(Duplicati.Library.Main.Interface.RemoveAllButNFull(target, tmp));
                }


                Console.WriteLine("Backing up the full copy: " + folders[0]);
                using (new Timer("Full backup of " + folders[0]))
                {
                    options["full"] = "";
                    Log.WriteMessage(Duplicati.Library.Main.Interface.Backup(folders[0], target, options), LogMessageType.Information);
                    options.Remove("full");
                }

                for (int i = 1; i < folders.Length; i++)
                {
                    //options["passphrase"] = "bad password";
                    //If the backups are too close, we can't pick the right one :(
                    System.Threading.Thread.Sleep(1000 * 5);
                    Console.WriteLine("Backing up the incremental copy: " + folders[i]);
                    using (new Timer("Incremental backup of " + folders[i]))
                        Log.WriteMessage(Duplicati.Library.Main.Interface.Backup(folders[i], target, options), LogMessageType.Information);
                }

                List<Duplicati.Library.Main.BackupEntry> entries = Duplicati.Library.Main.Interface.ParseFileList(target, options);

                if (entries.Count != 1 || entries[0].Incrementals.Count != folders.Length - 1)
                    throw new Exception("Filename parsing problem, or corrupt storage");

                List<Duplicati.Library.Main.BackupEntry> t = new List<Duplicati.Library.Main.BackupEntry>();
                t.Add(entries[0]);
                t.AddRange(entries[0].Incrementals);
                entries = t;

                for (int i = 0; i < entries.Count; i++)
                {
                    using (TempFolder ttf = new TempFolder())
                    {
                        Console.WriteLine("Restoring the copy: " + folders[i]);

                        options["restore-time"] = entries[i].Time.ToString();

                        using (new Timer("Restore of " + folders[i]))
                            Log.WriteMessage(Duplicati.Library.Main.Interface.Restore(target, ttf, options), LogMessageType.Information);

                        Console.WriteLine("Verifying the copy: " + folders[i]);

                        using (new Timer("Verification of " + folders[i]))
                            VerifyDir(System.IO.Path.GetFullPath(folders[i]), ttf);
                    }
                }

            }

            (Log.CurrentLog as StreamLog).Dispose();
            Log.CurrentLog = null;
        }

        /// <summary>
        /// Verifies the existence of all files and folders, and ensures that all
        /// files are binary equal.
        /// </summary>
        /// <param name="f1">One folder</param>
        /// <param name="f2">Another folder</param>
        private static void VerifyDir(string f1, string f2)
        {
            f1 = Utility.AppendDirSeperator(f1);
            f2 = Utility.AppendDirSeperator(f2);

            List<string> folders1 = Utility.EnumerateFolders(f1);
            List<string> folders2 = Utility.EnumerateFolders(f2);

            foreach (string s in folders1)
            {
                string relpath = s.Substring(f1.Length);
                string target = System.IO.Path.Combine(f2, relpath);
                if (!folders2.Contains(target))
                {
                    Log.WriteMessage("Missing folder: " + relpath, LogMessageType.Error);
                    Console.WriteLine("Missing folder: " + relpath);
                }
                else
                    folders2.Remove(target);
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
                if (!files2.Contains(target))
                {
                    Log.WriteMessage("Missing file: " + relpath, LogMessageType.Error);
                    Console.WriteLine("Missing file: " + relpath);
                }
                else
                {
                    files2.Remove(target);
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
                    for (long l = 0; l < fs1.Length; l++)
                        if (fs1.ReadByte() != fs2.ReadByte())
                        {
                            Log.WriteMessage("Mismatch in byte " + l.ToString() + " in file " + display, LogMessageType.Error);
                            Console.WriteLine("Mismatch in byte " + l.ToString() + " in file " + display);
                            return false;
                        }

            return true;
        }
    }
}

#endif