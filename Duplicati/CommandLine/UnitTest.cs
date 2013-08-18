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
using System.Linq;

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
			
			public static long WarningCount = 0;
			public static long ErrorCount = 0;
			
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
            	if (type == LogMessageType.Error)
            		System.Threading.Interlocked.Increment(ref ErrorCount);
            	else if (type == LogMessageType.Warning)
            		System.Threading.Interlocked.Increment(ref WarningCount);
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

            string auth_password = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "unittest_authpassword.txt");
            if (System.IO.File.Exists(auth_password))
                options["auth-password"] = System.IO.File.ReadAllText(auth_password).Trim();

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

            //Filter empty entries, commonly occuring with copy/paste and newlines
            folders = (from x in folders 
                      where !string.IsNullOrWhiteSpace(x)
                      select Environment.ExpandEnvironmentVariables(x)).ToArray();

            //Expand the tilde to home folder on Linux/OSX
            if (Utility.IsClientLinux)
                folders = (from x in folders
                            select x.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.Personal))).ToArray();

            foreach(var f in folders)
                foreach(var n in f.Split(new char[] {System.IO.Path.PathSeparator}, StringSplitOptions.RemoveEmptyEntries))
                    if (!System.IO.Directory.Exists(n))
                        throw new Exception(string.Format("Missing source folder: {0}", n));

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

            if (!options.ContainsKey("prefix"))
                options["prefix"] = "duplicati_unittest";

            //This would break the test, because the data is not modified the normal way
            options["disable-filetime-check"] = "true";
            //We want all messages in the log
            options["log-level"] = LogMessageType.Profiling.ToString();
            //We cannot rely on USN numbering, but we can use USN enumeration
            //options["disable-usn-diff-check"] = "true";
            
            //We use precise times
            options["disable-time-tolerance"] = "true";

            //We need all sets, even if they are unchanged
            options["upload-unchanged-backups"] = "true";
            
            bool skipfullrestore = false;
            bool skippartialrestore = false;
            bool skipverify = false;
            
            if (Utility.ParseBoolOption(options, "unittest-backuponly"))
            {
                skipfullrestore = true;
                skippartialrestore = true;
                options.Remove("unittest-backuponly");
            }
            
            if (Utility.ParseBoolOption(options, "unittest-skip-partial-restore"))
            {
                skippartialrestore = true;
                options.Remove("unittest-skip-partial-restore");
            }
            
            if (Utility.ParseBoolOption(options, "unittest-skip-full-restore"))
            {
                skipfullrestore = true;
                options.Remove("unittest-skip-full-restore");
            }
            
            if (Utility.ParseBoolOption(options, "unittest-skip-verify"))
            {
                skipverify = true;
                options.Remove("unittest-skip-verify");
            }
            
            using(new Timer("Total unittest"))
            using(TempFolder tf = new TempFolder())
            {
                options["dbpath"] = System.IO.Path.Combine(tempdir, "unittest.sqlite");
                if (System.IO.File.Exists(options["dbpath"]))
                    System.IO.File.Delete(options["dbpath"]);

                if (string.IsNullOrEmpty(target))
                {
                    target = "file://" + tf;
                }
                else
                {
                    Console.WriteLine("Removing old backups");
                    Dictionary<string, string> tmp = new Dictionary<string, string>(options);
                    tmp["keep-versions"] = "0";
                    tmp["force"] = "";
                    tmp["allow-full-removal"] = "";

                    using (new Timer("Cleaning up any existing backups")) 
                        using(var i = new Duplicati.Library.Main.Controller(target, tmp, new ConsoleOutput(options)))
                            i.Delete();
                }

                log.Backupset = "Backup " + folders[0];
                string fhtempsource = null;
                
                bool usingFHWithRestore = (!skipfullrestore || !skippartialrestore);

                using(var fhsourcefolder = usingFHWithRestore ? new Library.Utility.TempFolder() : null)
                {
                    if (usingFHWithRestore)
                    {
                        fhtempsource = fhsourcefolder;
                        CopyDirectoryRecursive(folders[0], fhsourcefolder);
                    }

                    RunBackup(usingFHWithRestore ? (string)fhsourcefolder : folders[0], target, options, folders[0]);
    
                    for (int i = 1; i < folders.Length; i++)
                    {
                        //options["passphrase"] = "bad password";
                        //If the backups are too close, we can't pick the right one :(
                        System.Threading.Thread.Sleep(1000 * 5);
                        log.Backupset = "Backup " + folders[i];
    
                        if (usingFHWithRestore)
                        {
                            System.IO.Directory.Delete(fhsourcefolder, true);
                            CopyDirectoryRecursive(folders[i], fhsourcefolder);
                        }
                        
                        //Call function to simplify profiling
                        RunBackup(usingFHWithRestore ? (string)fhsourcefolder : folders[i], target, options, folders[i]);
                    }
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

                IList<DateTime> entries;
                using(var i = new Duplicati.Library.Main.Controller(target, options, new ConsoleOutput(options)))
                    entries = (from n in i.List().Filesets select n.Time.ToLocalTime()).ToList();

                if (entries.Count != folders.Length)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("Entry count: " + entries.Count.ToString());
                    sb.Append(string.Format("Found {0} filelists but there were {1} source folders", entries.Count, folders.Length));
                    throw new Exception("Filename parsing problem, or corrupt storage: " + sb.ToString());
                }

                if (!skipfullrestore || !skippartialrestore)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        using (TempFolder ttf = new TempFolder())
                        {
                            log.Backupset = "Restore " + folders[i];
                            Console.WriteLine("Restoring the copy: " + folders[i]);
    
                            options["time"] = entries[entries.Count - i - 1].ToString();
    
                            string[] actualfolders = folders[i].Split(System.IO.Path.PathSeparator);    
                            if (!skippartialrestore)
                            {
                                Console.WriteLine("Partial restore of: " + folders[i]);
                                using (TempFolder ptf = new TempFolder())
                                {
                                    List<string> testfiles = new List<string>();
                                    using (new Timer("Extract list of files from" + folders[i]))
                                    {
                                        List<string> sourcefiles;
                                        using(var inst = new Library.Main.Controller(target, options, new ConsoleOutput(options)))
                                            sourcefiles = (from n in inst.List("*").Files select n.Path).ToList();
        
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
        
                                        if (!f.StartsWith(usingFHWithRestore ? fhtempsource : folders[i], Utility.ClientFilenameStringComparision))
                                            throw new Exception(string.Format("Unexpected file found: {0}, path is not a subfolder for {1}", f, folders[i]));
        
                                        f = f.Substring(Utility.AppendDirSeparator(usingFHWithRestore ? fhtempsource : folders[i]).Length);
        
                                        do
                                        {
                                            f = System.IO.Path.GetDirectoryName(f);
                                            partialFolders[Utility.AppendDirSeparator(f)] = null;
                                        } while (f.IndexOf(System.IO.Path.DirectorySeparatorChar) > 0);
                                    }
        
                                    if (partialFolders.ContainsKey(""))
                                        partialFolders.Remove("");
                                    if (partialFolders.ContainsKey(System.IO.Path.DirectorySeparatorChar.ToString()))
                                        partialFolders.Remove(System.IO.Path.DirectorySeparatorChar.ToString());
        
                                    List<string> filterlist;
        
                                    var tfe = Utility.AppendDirSeparator(usingFHWithRestore ? fhtempsource : folders[i]);
                                    
                                    filterlist = (from n in partialFolders.Keys
                                                  where !string.IsNullOrWhiteSpace(n) && n != System.IO.Path.DirectorySeparatorChar.ToString()
                                                  select Utility.AppendDirSeparator(System.IO.Path.Combine(tfe, n)))
                                                  .Union(testfiles) //Add files with full path
                                                  .Union(new string[] { tfe }) //Ensure root folder is included
                                                  .Distinct()
                                                  .ToList();
    
                                    testfiles = (from n in testfiles select n.Substring(tfe.Length)).ToList();
                
                                    //Call function to simplify profiling
                                    RunPartialRestore(folders[i], target, ptf, options, filterlist.ToArray());
        
                                    if (!skipverify)
                                    {
                                        //Call function to simplify profiling
                                        Console.WriteLine("Verifying partial restore of: " + folders[i]);
                                        VerifyPartialRestore(folders[i], testfiles, actualfolders, ptf, folders[0]);
                                    }
                                }
                            }
    
                            if (!skipfullrestore)
                            {
                                //Call function to simplify profiling
                                RunRestore(folders[i], target, ttf, options);
        
                                if (!skipverify)
                                {
                                    //Call function to simplify profiling
                                    Console.WriteLine("Verifying the copy: " + folders[i]);
                                    VerifyFullRestore(folders[i], actualfolders, new string[] { ttf });
                                }
                            }
                        }
                    }
                }   
                
                foreach(string s in Utility.EnumerateFiles(tempdir))
                {
                	if (s == options["dbpath"])
                		continue;
                	if (s.StartsWith(Utility.AppendDirSeparator(tf)))
                		continue;
                		
                	Log.WriteMessage(string.Format("Found left-over temp file: {0}", s.Substring(tempdir.Length)), LogMessageType.Warning);
                	Console.WriteLine("Found left-over temp file: {0} -> {1}", s.Substring(tempdir.Length), TempFile.GetStackTraceForTempFile(System.IO.Path.GetFileName(s)));
                }
                
                foreach(string s in Utility.EnumerateFolders(tempdir))
                	if (!s.StartsWith(Utility.AppendDirSeparator(tf)) && Utility.AppendDirSeparator(s) != Utility.AppendDirSeparator(tf) && Utility.AppendDirSeparator(s) != Utility.AppendDirSeparator(tempdir))
                	{
                		Log.WriteMessage(string.Format("Found left-over temp folder: {0}", s.Substring(tempdir.Length)), LogMessageType.Warning);
                		Console.WriteLine("Found left-over temp folder: {0}", s.Substring(tempdir.Length));
                	}
            }

            (Log.CurrentLog as StreamLog).Dispose();
            Log.CurrentLog = null;
            
            if (LogHelper.ErrorCount > 0)
        		Console.WriteLine("Unittest completed, but with {0} errors, see logfile for details", LogHelper.ErrorCount);
            else if (LogHelper.WarningCount > 0)
        		Console.WriteLine("Unittest completed, but with {0} warnings, see logfile for details", LogHelper.WarningCount);
        	else
        		Console.WriteLine("Unittest completed successfully - Have some cake!");            	
        }

        private static void VerifyPartialRestore(string source, IEnumerable<string> testfiles, string[] actualfolders, string tempfolder, string rootfolder)
        {
            using (new Timer("Verification of partial restore from " + source))
                foreach (string s in testfiles)
                {
                    string restoredname;
                    string sourcename;

                    if (actualfolders.Length == 1)
                    {
                        sourcename = System.IO.Path.Combine(actualfolders[0], s);
                        restoredname = System.IO.Path.Combine(tempfolder, s);
                    }
                    else
                    {
                        int six = s.IndexOf(System.IO.Path.DirectorySeparatorChar);
                        sourcename = System.IO.Path.Combine(actualfolders[int.Parse(s.Substring(0, six))], s.Substring(six + 1));
                        restoredname = System.IO.Path.Combine(System.IO.Path.Combine(tempfolder, System.IO.Path.GetFileName(rootfolder.Split(System.IO.Path.PathSeparator)[int.Parse(s.Substring(0, six))])), s.Substring(six + 1));
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

        private static void VerifyFullRestore(string source, string[] actualfolders, string[] restorefoldernames)
        {
            using (new Timer("Verification of " + source))
            {
                for (int j = 0; j < actualfolders.Length; j++)
                    VerifyDir(actualfolders[j], restorefoldernames[j]);
            }
        }

        private static void RunBackup(string source, string target, Dictionary<string, string> options, string sourcename)
        {
            Console.WriteLine("Backing up the copy: " + sourcename);
            using (new Timer("Backup of " + sourcename))
                using(var i = new Duplicati.Library.Main.Controller(target, options, new ConsoleOutput(options)))
                    Log.WriteMessage(i.Backup(source.Split(System.IO.Path.PathSeparator)).ToString(), LogMessageType.Information);
        }

        private static void RunRestore(string source, string target, string tempfolder, Dictionary<string, string> options)
        {
        	var tops = new Dictionary<string, string>(options);
        	tops["restore-path"] = tempfolder;
            using (new Timer("Restore of " + source))
                using(var i = new Duplicati.Library.Main.Controller(target, tops, new ConsoleOutput(options)))
                    Log.WriteMessage(i.Restore(null).ToString(), LogMessageType.Information);
        }

        private static void RunPartialRestore(string source, string target, string tempfolder, Dictionary<string, string> options, string[] files)
        {
        	var tops = new Dictionary<string, string>(options);
        	tops["restore-path"] = tempfolder;
            using (new Timer("Partial restore of " + source))
                using(var i = new Duplicati.Library.Main.Controller(target, tops, new ConsoleOutput(options)))
                    Log.WriteMessage(i.Restore(files).ToString(), LogMessageType.Information);
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

            var folders1 = Utility.EnumerateFolders(f1);
            var folders2 = Utility.EnumerateFolders(f2).ToList();

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

            var files1 = Utility.EnumerateFiles(f1);
            var files2 = Utility.EnumerateFiles(f2).ToList();
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
        
        /// <summary>
        /// Recursively copy a directory to another location.
        /// </summary>
        /// <param name="source">Source directory path</param>
        /// <param name="dest">Destination directory path</param>
        private static void CopyDirectoryRecursive(string source, string dest)
        {
            if (!System.IO.Directory.Exists(dest))
                System.IO.Directory.CreateDirectory(dest);
            
            foreach (var file in System.IO.Directory.GetFiles(source))
                System.IO.File.Copy(file, System.IO.Path.Combine(dest, System.IO.Path.GetFileName(file)), false);
            
            foreach (var folder in System.IO.Directory.GetDirectories(source))
                CopyDirectoryRecursive(folder, System.IO.Path.Combine(dest, System.IO.Path.GetFileName(folder)));
        }        
    }
}

#endif