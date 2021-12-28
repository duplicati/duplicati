#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
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
using Duplicati.Library.Common;

namespace Duplicati.Library.Snapshots
{
    static class Program
    {
        private static Dictionary<string, string> ExtractOptions(List<string> args)
        {
            Dictionary<string, string> options = new Dictionary<string, string>();

            for (int i = 0; i < args.Count; i++)
            {
                if (args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    string key = null;
                    string value = null;
                    if (args[i].IndexOf("=", StringComparison.Ordinal) > 0)
                    {
                        key = args[i].Substring(0, args[i].IndexOf("=", StringComparison.Ordinal));
                        value = args[i].Substring(args[i].IndexOf("=", StringComparison.Ordinal) + 1);
                    }
                    else
                        key = args[i];

                    //Skip the leading --
                    key = key.Substring(2).ToLower(System.Globalization.CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(value) && value.Length > 1 && value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
                        value = value.Substring(1, value.Length - 2);

                    //Last argument overwrites the current
                    options[key] = value;

                    args.RemoveAt(i);
                    i--;
                }
            }

            return options;
        }

        public static void Main(string[] _args)
        {
            try
            {
                List<string> args = new List<string>(_args);
                Dictionary<string, string> options = ExtractOptions(args);
                
                if (args.Count == 0)
                    args = new List<string> { System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) };

                if (args.Count != 1)
                {
                    Console.WriteLine(@"Usage:
Duplicati.Library.Snapshots.exe [test-folder]

Where <test-folder> is the folder where files will be locked/created etc");
                    return;
                }

                if (!System.IO.Directory.Exists(args[0]))
                    System.IO.Directory.CreateDirectory(args[0]);

                string filename = System.IO.Path.Combine(args[0], "testfile.bin");

                Console.WriteLine("Creating file {0}", filename);

                using (System.IO.FileStream fs = new System.IO.FileStream(filename, System.IO.FileMode.Create, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                {
                    Console.WriteLine("Attempting to read locked file {0}", filename);

                    try
                    {
                        using (System.IO.FileStream fs2 = new System.IO.FileStream(filename, System.IO.FileMode.Create, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                        { }

                        Console.WriteLine("Could open locked file {0}, cannot test", filename);
                        Console.WriteLine("* Test failed");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("The file {0} was correctly locked, message: {1}", filename, ex.Message);
                    }

                    Console.WriteLine("Creating snapshot for folder: {0}", args[0]);
                    Console.WriteLine("If this fails, try to run as " + (Platform.IsClientPosix ? "root" : "Administrator"));
                    using (ISnapshotService snapshot = SnapshotUtility.CreateSnapshot(new[] { args[0] }, options))
                    {
                        Console.WriteLine("Attempting to read locked file via snapshot");
                        try
                        {
                            using (System.IO.Stream s = snapshot.OpenRead(filename))
                            { }

                            Console.WriteLine("Could open locked file {0}, through snapshot", filename);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("The file {0} was locked even through snapshot, message: {1}", filename, ex);
                            Console.WriteLine("* Test failed");
                            return;
                        }
                    }
                }

                Console.WriteLine("* Test passed");
            }
            catch (Exception ex)
            {
                Console.WriteLine("The snapshot tester failed: {0}", ex);
                Console.WriteLine("* Test failed");
            }

        }
    }
}
