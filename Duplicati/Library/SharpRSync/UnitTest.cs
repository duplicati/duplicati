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

namespace Duplicati.Library.SharpRSync
{
    /// <summary>
    /// Performs unittests by comparing the output of SharpRsync with the output of rdiff
    /// </summary>
    public class UnitTest
    {
        /// <summary>
        /// Runs the test
        /// </summary>
        /// <param name="items">A list of items to test, Key is the current version, Value is the updated version</param>
        /// <param name="useRdiff">True if rdiff should also be tested, false otherwise (requires that &quot;rdiff&quot; can be executed)</param>
        public static void DoTest(List<KeyValuePair<string, string>> items, bool useRdiff)
        {
            string basedir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string testfolder = System.IO.Path.Combine(basedir, "UNITTEST");

            try
            {
                foreach (KeyValuePair<string, string> item in items)
                {
                    System.IO.Directory.CreateDirectory(testfolder);

                    //Step 1 generate signatures
                    string sharpSigfile = System.IO.Path.Combine(testfolder, "sharp.signature");
                    string rdiffSigfile = System.IO.Path.Combine(testfolder, "rdiff.signature");

                    string sharpDeltafile = System.IO.Path.Combine(testfolder, "sharp.delta");
                    string rdiffDeltafile = System.IO.Path.Combine(testfolder, "rdiff.delta");

                    string sharpPatchedfile = System.IO.Path.Combine(testfolder, "sharp.patched");
                    string rdiffPatchedfile = System.IO.Path.Combine(testfolder, "rdiff.patched");

                    DateTime start = DateTime.Now;
                    SharpRSync.Interface.GenerateSignature(item.Key, sharpSigfile);
                    Console.WriteLine("Signature generation for {0} with SharpRSync took {1}", item.Key, DateTime.Now - start);

                    if (useRdiff)
                    {
                        start = DateTime.Now;
                        System.Diagnostics.Process.Start("rdiff", string.Format("signature \"{0}\" \"{1}\"", item.Key, rdiffSigfile)).WaitForExit();
                        Console.WriteLine("Signature generation for {0} with rdiff took {1}", item.Key, DateTime.Now - start);

                        CompareFiles(sharpSigfile, rdiffSigfile, sharpSigfile);
                    }


                    start = DateTime.Now;
                    SharpRSync.Interface.GenerateDelta(sharpSigfile, item.Value, sharpDeltafile);
                    Console.WriteLine("Delta generation for {0} -> {1} with SharpRSync took {2}", item.Key, item.Value, DateTime.Now - start);

                    if (useRdiff)
                    {
                        start = DateTime.Now;
                        System.Diagnostics.Process.Start("rdiff", string.Format("delta \"{0}\" \"{1}\" \"{2}\"", rdiffSigfile, item.Value, rdiffDeltafile)).WaitForExit();
                        Console.WriteLine("Delta generation for {0} -> {1} with rdiff took {2}", item.Key, item.Value, DateTime.Now - start);
                    }

                    start = DateTime.Now;
                    SharpRSync.Interface.PatchFile(item.Key, sharpDeltafile, sharpPatchedfile);
                    Console.WriteLine("Patch generation for {0} with SharpRSync took {1}", item.Key, DateTime.Now - start);

                    if (useRdiff)
                    {
                        start = DateTime.Now;
                        System.Diagnostics.Process.Start("rdiff", string.Format("patch \"{0}\" \"{1}\" \"{2}\"", item.Key, rdiffDeltafile, rdiffPatchedfile)).WaitForExit();
                        Console.WriteLine("Patch generation for {0} with rdiff took {1}", item.Key, DateTime.Now - start);
                        CompareFiles(sharpPatchedfile, rdiffPatchedfile, sharpDeltafile);
                    }

                    CompareFiles(sharpPatchedfile, item.Value, sharpPatchedfile);

                    System.IO.Directory.Delete(testfolder, true);
                }
            }
            finally
            {
                try
                {
                    if (System.IO.Directory.Exists(testfolder))
                        System.IO.Directory.Delete(testfolder, true);
                }
                catch { }
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
                    Console.WriteLine("Lengths differ: " + display + ", " + fs1.Length.ToString() + " vs. " + fs2.Length.ToString());
                    return false;
                }
                else
                {
                    long len = fs1.Length;
                    for (long l = 0; l < len; l++)
                        if (fs1.ReadByte() != fs2.ReadByte())
                        {
                            Console.WriteLine("Mismatch in byte " + l.ToString() + " in file " + display);
                            return false;
                        }
                }

            return true;
        }
    }
}

#endif
