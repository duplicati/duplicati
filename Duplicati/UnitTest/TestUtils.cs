//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.IO;
using System.Collections.Generic;
using Duplicati.Library.Utility;
using System.Linq;
using Duplicati.Library.Logging;
using System.Reflection;

namespace Duplicati.UnitTest
{
    public static class TestUtils
    {
        public static Dictionary<string, string> DefaultOptions
        {
            get
            {
                var opts = new Dictionary<string, string>();

                string auth_password = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "unittest_authpassword.txt");
                if (System.IO.File.Exists(auth_password))
                    opts["auth-password"] = File.ReadAllText(auth_password).Trim();
                
                return opts;
            }
        }

        public static string GetDefaultTarget(string other = null)
        {
            string alttarget = System.IO.Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "unittest_target.txt");

            if (File.Exists(alttarget))
                return File.ReadAllText(alttarget).Trim();
            else if (other != null)
                return other;
            else
                using(var tf = new Library.Utility.TempFolder())
                {
                    tf.Protected = true;
                    return "file://" + tf;
                }
        }

        /// <summary>
        /// Recursively copy a directory to another location.
        /// </summary>
        /// <param name="sourcefolder">Source directory path</param>
        /// <param name="targetfolder">Destination directory path</param>
        public static void CopyDirectoryRecursive(string sourcefolder, string targetfolder)
        {
            sourcefolder = Library.Utility.Utility.AppendDirSeparator(sourcefolder);

            var work = new Queue<string>();
            work.Enqueue(sourcefolder);

            var timestampfailures = 0;

            while (work.Count > 0)
            {
                var c = work.Dequeue();

                var t = Path.Combine(targetfolder, c.Substring(sourcefolder.Length));

                if (!Directory.Exists(t))
                    Directory.CreateDirectory(t);
                
                try { Directory.SetCreationTimeUtc(t, Directory.GetCreationTimeUtc(c)); }
                catch(Exception ex) 
                { 
                    if (timestampfailures++ < 20)
                        Console.WriteLine("Failed to set creation time on dir {0}: {1}", t, ex.Message); 
                }

                try { Directory.SetLastWriteTimeUtc(t, Directory.GetLastWriteTimeUtc(c)); }
                catch(Exception ex) 
                { 
                    if (timestampfailures++ < 20)
                        Console.WriteLine("Failed to set write time on dir {0}: {1}", t, ex.Message); 
                }

                
                foreach(var n in Directory.EnumerateFiles(c))
                {
                    var tf = Path.Combine(t, Path.GetFileName(n));
                    File.Copy(n, tf, true);
                    try { File.SetCreationTimeUtc(tf, System.IO.File.GetCreationTimeUtc(n)); }
                    catch(Exception ex)
                    {
                        if (timestampfailures++ < 20)
                            Console.WriteLine("Failed to set creation time on file {0}: {1}", n, ex.Message); 
                    }
                    try { File.SetLastWriteTimeUtc(tf, System.IO.File.GetLastWriteTimeUtc(n)); }
                    catch(Exception ex)
                    {
                        if (timestampfailures++ < 20)
                            Console.WriteLine("Failed to set write time on file {0}: {1}", n, ex.Message); 
                    }
                }

                foreach(var n in Directory.EnumerateDirectories(c))
                    work.Enqueue(n);
            }

            if (timestampfailures > 20)
                Console.WriteLine("Encountered additional {0} timestamp errors!", timestampfailures);
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
        public static void VerifyDir(string f1, string f2, bool verifymetadata)
        {
            var anymissing = false;
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
                    anymissing = true;
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
                    anymissing = true;
                }
                else
                {
                    files2.RemoveAt(ix);
                    if (!CompareFiles(s, target, relpath, verifymetadata))
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

            if (anymissing)
                throw new Exception("Verify failed, some items are missing");
        }

        /// <summary>
        /// Compares two files by reading all bytes, and comparing one by one
        /// </summary>
        /// <param name="f1">One file</param>
        /// <param name="f2">Another file</param>
        /// <param name="display">File display name</param>
        /// <returns>True if they are equal, false otherwise</returns>
        public static bool CompareFiles(string f1, string f2, string display, bool verifymetadata)
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
                    // The byte-by-byte compare is dog-slow, so we use a fast(-er) check, and then report the first byte diff if required
                    if (!Library.Utility.Utility.CompareStreams(fs1, fs2, true))
                    {
                        fs1.Position = 0;
                        fs2.Position = 0;
                        long len = fs1.Length;
                        for(long l = 0; l < len; l++)
                            if (fs1.ReadByte() != fs2.ReadByte())
                            {
                                Log.WriteMessage("Mismatch in byte " + l.ToString() + " in file " + display, LogMessageType.Error);
                                Console.WriteLine("Mismatch in byte " + l.ToString() + " in file " + display);
                                return false;
                            }
                    }
                }

            if (verifymetadata)
            {
                if (System.IO.File.GetLastWriteTime(f1) != System.IO.File.GetLastWriteTime(f2))
                {
                    Log.WriteMessage("Mismatch in lastmodified for " + f2 + ", " + System.IO.File.GetLastWriteTimeUtc(f1) + " vs. " + System.IO.File.GetLastWriteTimeUtc(f2), LogMessageType.Warning);
                    Console.WriteLine("Mismatch in lastmodified for " + f2 + ", " + System.IO.File.GetLastWriteTimeUtc(f1) + " vs. " + System.IO.File.GetLastWriteTimeUtc(f2));
                }

                if (System.IO.File.GetCreationTimeUtc(f1) != System.IO.File.GetCreationTimeUtc(f2))
                {
                    Log.WriteMessage("Mismatch in create-time for " + f2 + ", " + System.IO.File.GetCreationTimeUtc(f1) + " vs. " + System.IO.File.GetCreationTimeUtc(f2), LogMessageType.Warning);
                    Console.WriteLine("Mismatch in create-time for " + f2 + ", " + System.IO.File.GetCreationTimeUtc(f1) + " vs. " + System.IO.File.GetCreationTimeUtc(f2));
                }
            }


            return true;
        }
            
        public static Dictionary<string, string> Expand(this Dictionary<string, string> self, object extra)
        {
            var res = new Dictionary<string, string>(self);
            foreach(var n in extra.GetType().GetFields())
            {
                var name = n.Name.Replace('_', '-');
                var value = n.GetValue(extra);
                res[name] = value == null ? "" : value.ToString();
            }

            foreach(var n in extra.GetType().GetProperties())
            {
                var name = n.Name.Replace('_', '-');
                var value = n.GetValue(extra);
                res[name] = value == null ? "" : value.ToString();
            }

            return res;
        }

    }
}

