// Copyright (C) 2026, The Duplicati Team
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
using Duplicati.Library.Logging;
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

        /// <summary>
        /// Parses the log level from an option, defaulting to the given level
        /// </summary>
        private static LogMessageType ParseLogLevel(Dictionary<string, string> options, string option, LogMessageType defaultLevel)
        {
            if (options.TryGetValue(option, out var value) && !string.IsNullOrWhiteSpace(value))
                if (Enum.TryParse<LogMessageType>(value, true, out var level))
                    return level;

            return defaultLevel;
        }

        public static int Main(string[] _args)
        {
            StreamLogDestination consoleDestination = null;
            StreamLogDestination fileDestination = null;
            try
            {
                var args = new List<string>(_args);
                var options = ExtractOptions(args);

                if (args.Count == 0)
                    args = new List<string> { System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) };

                if (args.Count < 1 || HelpOptionExtensions.IsArgumentAnyHelpString(args) || options.ContainsKey("help"))
                {
                    Console.WriteLine(@$"Usage:
{PackageHelper.GetExecutableName(PackageHelper.NamedExecutable.Snapshots)} [test-folder1] [test-folder2] ...
    --ignorelocking=<true|false>       : If true, the test will continue even if the file cannot be locked
    --snapshot-provider=<wmi|vanara|native|alphavss> : The VSS provider to use (Windows only, leave empty on other platforms)
    --log-file=<path>                  : Write log output to the given file
    --log-file-log-level=<level>       : Log level for the log file (e.g. Verbose, Information, Warning); default Warning
    --console-log-level=<level>        : Log level for console output (e.g. Verbose, Information, Warning); default Warning
    --help                             : Show this help

Where <test-folderN> is one or more folders where files will be locked/created etc.
Multiple folders on different disks can be used to test multi-volume snapshots.");
                    return 1;
                }

                // Always log to the console at the requested level
                consoleDestination = new StreamLogDestination(Console.OpenStandardOutput());

                // Set up file logging if requested
                if (options.TryGetValue("log-file", out var logfile) && !string.IsNullOrWhiteSpace(logfile))
                {
                    var logDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(logfile));
                    if (!string.IsNullOrEmpty(logDir) && !System.IO.Directory.Exists(logDir))
                        System.IO.Directory.CreateDirectory(logDir);

                    fileDestination = new StreamLogDestination(logfile);
                }

                using (Log.StartScope(consoleDestination, ParseLogLevel(options, "console-log-level", LogMessageType.Warning)))
                using (fileDestination == null
                    ? Log.StartScope()
                    : Log.StartScope(fileDestination, ParseLogLevel(options, "log-file-log-level", LogMessageType.Warning)))
                {
                    return Run(args, options);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("The snapshot tester failed: {0}", ex);
                Console.WriteLine("* Test failed");
                return 3;
            }
            finally
            {
                fileDestination?.Dispose();
                consoleDestination?.Dispose();
            }
        }

        private static int Run(List<string> args, Dictionary<string, string> options)
        {
            var ignoreLockingFailure = Library.Utility.Utility.ParseBoolOption(options, "ignorelocking");
            var anyLockingVerified = false;

            // Create and lock a test file in each folder
            var locks = new List<(string Folder, string File, System.IO.FileStream Stream)>();
            try
            {
                foreach (var folder in args)
                {
                    if (!System.IO.Directory.Exists(folder))
                        System.IO.Directory.CreateDirectory(folder);

                    var filename = System.IO.Path.Combine(folder, "testfile.bin");
                    Console.WriteLine("Creating file {0}", filename);
                    var fs = new System.IO.FileStream(filename, System.IO.FileMode.Create, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);
                    locks.Add((folder, filename, fs));
                }

                // Verify each file is actually locked
                foreach (var (folder, filename, fs) in locks)
                {
                    Console.WriteLine("Attempting to read locked file {0}", filename);
                    try
                    {
                        using (new System.IO.FileStream(filename, System.IO.FileMode.Create, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
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
                        anyLockingVerified = true;
                        Console.WriteLine("The file {0} was correctly locked, message: {1}", filename, ex.Message);
                    }
                }

                Console.WriteLine("Creating snapshot for folder(s): {0}", string.Join(", ", args));
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

                using (var snapshot = SnapshotUtility.CreateSnapshot(args, options, false))
                {
                    foreach (var (folder, filename, fs) in locks)
                    {
                        Console.WriteLine("Attempting to read locked file via snapshot: {0}", filename);
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
            }
            finally
            {
                foreach (var l in locks)
                    l.Stream?.Dispose();
            }

            if (!anyLockingVerified)
            {
                Console.WriteLine("* Test passed (but file locking could not be verified)");
            }
            else
            {
                Console.WriteLine("* Test passed");
            }
            return 0;
        }
    }
}
