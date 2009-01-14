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

#if DEBUG

using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Library.Logging;
using Duplicati.Library.Core;

namespace Duplicati.CommandLine
{
    /// <summary>
    /// This class encapsulates a simple method for testing the correctness of 
    /// duplicati.
    /// </summary>
    public class UnitTest
    {
        /// <summary>
        /// Running the unit test confirms the correctness of duplicati
        /// </summary>
        /// <param name="folders">The folders to backup. Folder at index 0 is the base, all others are incrementals</param>
        public static void RunTest(string[] folders)
        {
            Log.CurrentLog = new StreamLog("unittest.log");
            Log.LogLevel = Duplicati.Library.Logging.LogMessageType.Profiling;
            
            using(new Timer("Total unittest"))
            using (TempFolder tf = new TempFolder())
            {
                Dictionary<string, string> options = new Dictionary<string, string>();
                options["time-separator"] = "'";
                options["passphrase"] = "secret password!";

                Console.WriteLine("Backing up the full copy: " + folders[0]);
                using (new Timer("Full backup of " + folders[0]))
                    Duplicati.Library.Main.Interface.Backup(folders[0], "file://" + tf, options);

                for (int i = 1; i < folders.Length; i++)
                {
                    //If the backups are too close, we can't pick the right one :(
                    System.Threading.Thread.Sleep(1000 * 5);
                    Console.WriteLine("Backing up the incremental copy: " + folders[i]);
                    using (new Timer("Incremental backup of " + folders[i]))
                        Duplicati.Library.Main.Interface.Backup(folders[i], "file://" + tf, options);
                }

                List<Duplicati.Library.Main.BackupEntry> entries = Duplicati.Library.Main.Interface.ParseFileList("file://" + tf, options);

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

                        Dictionary<string, string> opts = new Dictionary<string, string>();
                        opts["restore-time"] = entries[i].Time.ToString();
                        opts["time-separator"] = "'";
                        using (new Timer("Restore of " + folders[i]))
                            Duplicati.Library.Main.Interface.Restore("file://" + tf, ttf, opts);

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
                    Console.WriteLine("Missing folder: " + relpath);
                else
                    folders2.Remove(target);
            }

            foreach(string s in folders2)
                Console.WriteLine("Extra folder: " + s.Substring(f2.Length));

            List<string> files1 = Utility.EnumerateFiles(f1);
            List<string> files2 = Utility.EnumerateFiles(f2);
            foreach (string s in files1)
            {
                string relpath = s.Substring(f1.Length);
                string target = System.IO.Path.Combine(f2, relpath);
                if (!files2.Contains(target))
                    Console.WriteLine("Missing file: " + relpath);
                else
                {
                    files2.Remove(target);
                    if (!CompareFiles(s, target, relpath))
                        Console.WriteLine("File differs: " + relpath);
                }
            }

            foreach (string s in files2)
                Console.WriteLine("Extra file: " + s.Substring(f2.Length));
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
                    Console.WriteLine("Lengths differ: " + display + ", " + fs1.Length.ToString() + " vs. " + fs2.Length.ToString());
                    return false;
                }
                else
                    for (long l = 0; l < fs1.Length; l++)
                        if (fs1.ReadByte() != fs2.ReadByte())
                        {
                            Console.WriteLine("Mismatch in byte " + l.ToString() + " in file " + display);
                            return false;
                        }

            return true;
        }
    }
}

#endif