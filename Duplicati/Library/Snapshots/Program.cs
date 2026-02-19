// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Snapshots
{
    public static class Program
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

        public static int Main(string[] _args)
        {
            try
            {
                var args = new List<string>(_args);
                var options = ExtractOptions(args);

                if (args.Count == 0)
                    args = new List<string> { System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) };

                if (args.Count != 1 || HelpOptionExtensions.IsArgumentAnyHelpString(args) || options.ContainsKey("help"))
                {
                    Console.WriteLine(@$"Usage:
{PackageHelper.GetExecutableName(PackageHelper.NamedExecutable.Snapshots)} [test-folder]
    --ignorelocking=<true|false>       : If true, the test will continue even if the file cannot be locked
    --help                             : Show this help

Where <test-folder> is the folder where files will be locked/created etc");
                    return 1;
                }

                if (!System.IO.Directory.Exists(args[0]))
                    System.IO.Directory.CreateDirectory(args[0]);

                var filename = System.IO.Path.Combine(args[0], "testfile.bin");

                Console.WriteLine("Creating file {0}", filename);
                var ignoreLockingFailure = Library.Utility.Utility.ParseBoolOption(options, "ignorelocking");
                var failedToReadLockedFile = false;

                using (var fs = new System.IO.FileStream(filename, System.IO.FileMode.Create, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                {
                    Console.WriteLine("Attempting to read locked file {0}", filename);

                    try
                    {
                        using (System.IO.FileStream fs2 = new System.IO.FileStream(filename, System.IO.FileMode.Create, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                        { }

                        if (!ignoreLockingFailure)
                        {
                            Console.WriteLine("Could open locked file {0}, cannot test", filename);
                            Console.WriteLine("* Test failed");
                            return 2;
                        }
                    }
                    catch (Exception ex)
                    {
                        failedToReadLockedFile = true;
                        Console.WriteLine("The file {0} was correctly locked, message: {1}", filename, ex.Message);
                    }

                    Console.WriteLine("Creating snapshot for folder: {0}", args[0]);
                    if (OperatingSystem.IsMacOS())
                    {
                        Console.WriteLine("Using APFS snapshots on MacOS with tmutil");
                        Console.WriteLine("If this fails, make sure the process has Full Disk Access permission (sudo is not required)");
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        Console.WriteLine("Using LVM snapshots on Linux");
                        Console.WriteLine("If this fails, try to run as root");
                    }
                    else //if (OperatingSystem.IsWindows())
                    {
                        Console.WriteLine("Using Volume Shadow Copies on Windows");
                        Console.WriteLine("If this fails, try to run as Administrator");
                    }

                    using (var snapshot = SnapshotUtility.CreateSnapshot(new[] { args[0] }, options, false))
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
                            return 2;
                        }
                    }
                }

                if (!failedToReadLockedFile)
                {
                    Console.WriteLine("* Test passed (but file locking could not be verified)");
                }
                else
                {
                    Console.WriteLine("* Test passed");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("The snapshot tester failed: {0}", ex);
                Console.WriteLine("* Test failed");
                return 3;
            }

        }
    }
}
