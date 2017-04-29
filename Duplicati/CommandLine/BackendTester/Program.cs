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
using Duplicati.Library.Interface;
using System.Linq;

namespace Duplicati.CommandLine.BackendTester
{
    class Program
    {
        class TempFile
        {
            public readonly string remotefilename;
            public readonly string localfilename;
            public readonly byte[] hash;
            public readonly long length;
            public bool found = false;

            public TempFile(string remotefilename, string localfilename, byte[] hash, long length)
            {
                this.remotefilename = remotefilename;
                this.localfilename = localfilename;
                this.hash = hash;
                this.length = length;
            }
        }

        private const string ValidFilenameChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ123456789";
        private const string ExtendedChars = "-_',=)(&%$#@! +";

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            Duplicati.Library.AutoUpdater.UpdaterManager.IgnoreWebrootFolder = true;
            return Duplicati.Library.AutoUpdater.UpdaterManager.RunFromMostRecent(typeof(Program).GetMethod("RealMain"), args);
        }

        public static void RealMain(string[] _args)
        {
            try
            {
                if (_args.Length == 1)
                {
                    try
                    {
                        var p = Library.Utility.Utility.ExpandEnvironmentVariables(_args[0]);
                        if (System.IO.File.Exists(p))
                            _args = (from x in System.IO.File.ReadLines(p)
                                where !string.IsNullOrWhiteSpace(x) && !x.Trim().StartsWith("#")
                                select x.Trim()
                            ).ToArray();
                    }
                    catch
                    {
                    }
                }

                List<string> args = new List<string>(_args);
                Dictionary<string, string> options = Library.Utility.CommandLineParser.ExtractOptions(args);

                if (!options.ContainsKey("auth_password") && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_PASSWORD")))
                    options["auth_password"] = System.Environment.GetEnvironmentVariable("AUTH_PASSWORD");

                if (!options.ContainsKey("auth_username") && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_USERNAME")))
                    options["auth_username"] = System.Environment.GetEnvironmentVariable("AUTH_USERNAME");

                if (options.ContainsKey("tempdir") && !string.IsNullOrEmpty(options["tempdir"]))
                    Library.Utility.TempFolder.SetSystemTempPath(options["tempdir"]);

                if (args.Count != 1 || args[0].ToLower() == "help" || args[0] == "?")
                {
                    Console.WriteLine("Usage: <protocol>://<username>:<password>@<path>");
                    Console.WriteLine("Example: ftp://user:pass@server/folder");
                    Console.WriteLine();
                    Console.WriteLine("Supported backends: " + string.Join(",", Duplicati.Library.DynamicLoader.BackendLoader.Keys));

                    Console.WriteLine();
                    List<string> lines = new List<string>();
                    foreach (Library.Interface.ICommandLineArgument arg in SupportedCommands)
                        Library.Interface.CommandLineArgument.PrintArgument(lines, arg);

                    foreach (string s in lines)
                        Console.WriteLine(s);

                    return;
                }

                int reruns = 5;
                if (options.ContainsKey("reruns"))
                    reruns = int.Parse(options["reruns"]);

                for (int i = 0; i < reruns; i++)
                {
                    Console.WriteLine("Starting run no {0}", i);
                    if (!Run(args, options, i == 0))
                        return;
                }
                Console.WriteLine("Unittest complete!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unittest failed: " + ex.ToString());
            }
        }

        static bool Run(List<string> args, Dictionary<string, string> options, bool first)
        {
            string allowedChars = ValidFilenameChars;
            if (options.ContainsKey("extended-chars"))
                allowedChars += options["extended-chars"];
            else
                allowedChars += ExtendedChars;

            bool autoCreateFolders = Library.Utility.Utility.ParseBoolOption(options, "auto-create-folder");

            Library.Interface.IBackend backend = Library.DynamicLoader.BackendLoader.GetBackend(args[0], options);
            if (backend == null)
            {
                Console.WriteLine("Unsupported backend");
                Console.WriteLine();
                Console.WriteLine("Supported backends: " + string.Join(",", Duplicati.Library.DynamicLoader.BackendLoader.Keys));
                return false;
            }

            string disabledModulesValue;
            string enabledModulesValue;
            options.TryGetValue("enable-module", out enabledModulesValue);
            options.TryGetValue("disable-module", out disabledModulesValue);
            string[] enabledModules = enabledModulesValue == null ? new string[0] : enabledModulesValue.Trim().ToLower().Split(',');
            string[] disabledModules = disabledModulesValue == null ? new string[0] : disabledModulesValue.Trim().ToLower().Split(',');

            List<Library.Interface.IGenericModule> loadedModules = new List<IGenericModule>();
            foreach (Library.Interface.IGenericModule m in Library.DynamicLoader.GenericLoader.Modules)
                if (Array.IndexOf<string>(disabledModules, m.Key.ToLower()) < 0 && (m.LoadAsDefault || Array.IndexOf<string>(enabledModules, m.Key.ToLower()) >= 0))
                {
                    m.Configure(options);
                    loadedModules.Add(m);
                }

            try
            {
                List<Library.Interface.IFileEntry> curlist = null;
                try
                {
                    curlist = backend.List();
                }
                catch (FolderMissingException fex)
                {
                    if (autoCreateFolders)
                    {
                        try
                        {
                            backend.CreateFolder();
                            curlist = backend.List();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Autocreate folder failed with message: " + ex.Message);
                        }
                    }

                    if (curlist == null)
                        throw fex;
                }

                foreach (Library.Interface.IFileEntry fe in curlist)
                    if (!fe.IsFolder)
                    {
                        if (Library.Utility.Utility.ParseBoolOption(options, "auto-clean") && first)
                            if (Library.Utility.Utility.ParseBoolOption(options, "force"))
                            {
                                Console.WriteLine("Auto clean, removing file: {0}", fe.Name);
                                backend.Delete(fe.Name);
                                continue;
                            }
                            else
                                Console.WriteLine("Specify the --force flag to actually delete files");

                        Console.WriteLine("*** Remote folder is not empty, aborting");
                        return false;
                    }


                int number_of_files = 10;
                int min_file_size = 1024;
                int max_file_size = 1024 * 1024 * 50;
                int min_filename_size = 5;
                int max_filename_size = 80;
                bool disableStreaming = Library.Utility.Utility.ParseBoolOption(options, "disable-streaming-transfers");
                bool skipOverwriteTest = Library.Utility.Utility.ParseBoolOption(options, "skip-overwrite-test");

                if (options.ContainsKey("number-of-files"))
                    number_of_files = int.Parse(options["number-of-files"]);
                if (options.ContainsKey("min-file-size"))
                    min_file_size = (int)Duplicati.Library.Utility.Sizeparser.ParseSize(options["min-file-size"], "mb");
                if (options.ContainsKey("max-file-size"))
                    max_file_size = (int)Duplicati.Library.Utility.Sizeparser.ParseSize(options["max-file-size"], "mb");

                if (options.ContainsKey("min-filename-length"))
                    min_filename_size = int.Parse(options["min-filename-length"]);
                if (options.ContainsKey("max-filename-length"))
                    max_filename_size = int.Parse(options["max-filename-length"]);

                Random rnd = new Random();
                System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();

                //Create random files
                using (Library.Utility.TempFolder tf = new Duplicati.Library.Utility.TempFolder())
                {
                    List<TempFile> files = new List<TempFile>();
                    for (int i = 0; i < number_of_files; i++)
                    {

                        StringBuilder filename = new StringBuilder();
                        int filenamelen = rnd.Next(min_filename_size, max_filename_size);
                        for (int j = 0; j < filenamelen; j++)
                            filename.Append(allowedChars[rnd.Next(0, allowedChars.Length)]);

                        string localfilename = CreateRandomFile(tf, i, min_file_size, max_file_size, rnd);

                        //Calculate local hash and length
                        using (System.IO.FileStream fs = new System.IO.FileStream(localfilename, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                            files.Add(new TempFile(filename.ToString(), localfilename, sha.ComputeHash(fs), fs.Length));
                    }

                    byte[] dummyFileHash = null;
                    if (!skipOverwriteTest)
                    {
                        Console.WriteLine("Uploading wrong files ...");
                        using (Library.Utility.TempFile dummy = Library.Utility.TempFile.WrapExistingFile(CreateRandomFile(tf, files.Count, 1024, 2048, rnd)))
                        {
                            using (System.IO.FileStream fs = new System.IO.FileStream(dummy, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                                dummyFileHash = sha.ComputeHash(fs);

                            //Upload a dummy file for entry 0 and the last one, they will be replaced by the real files afterwards
                            //We upload entry 0 twice just to try to freak any internal cache list
                            Uploadfile(dummy, 0, files[0].remotefilename, backend, disableStreaming);
                            Uploadfile(dummy, 0, files[0].remotefilename, backend, disableStreaming);
                            Uploadfile(dummy, files.Count - 1, files[files.Count - 1].remotefilename, backend, disableStreaming);
                        }

                    }

                    Console.WriteLine("Uploading files ...");

                    for (int i = 0; i < files.Count; i++)
                        Uploadfile(files[i].localfilename, i, files[i].remotefilename, backend, disableStreaming);

                    Console.WriteLine("Verifying file list ...");

                    curlist = backend.List();
                    foreach (Library.Interface.IFileEntry fe in curlist)
                        if (!fe.IsFolder)
                        {
                            bool found = false;
                            foreach (TempFile tx in files)
                                if (tx.remotefilename == fe.Name)
                                {
                                    if (tx.found)
                                        Console.WriteLine("*** File with name {0} was found more than once", tx.remotefilename);
                                    found = true;
                                    tx.found = true;

                                    if (fe.Size > 0 && tx.length != fe.Size)
                                        Console.WriteLine("*** File with name {0} has size {1} but the size was reported as {2}", tx.remotefilename, tx.length, fe.Size);

                                    break;
                                }

                            if (!found)
                                Console.WriteLine("*** File with name {0} was found on server but not uploaded!", fe.Name);
                        }

                    foreach (TempFile tx in files)
                        if (!tx.found)
                            Console.WriteLine("*** File with name {0} was uploaded but not found afterwards", tx.remotefilename);

                    Console.WriteLine("Downloading files");

                    for (int i = 0; i < files.Count; i++)
                    {
                        using (Duplicati.Library.Utility.TempFile cf = new Duplicati.Library.Utility.TempFile())
                        {
                            Exception e = null;
                            Console.Write("Downloading file {0} ... ", i);

                            try
                            {
                                if (backend is Library.Interface.IStreamingBackend && !disableStreaming)
                                {
                                    using (System.IO.FileStream fs = new System.IO.FileStream(cf, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                                    using (NonSeekableStream nss = new NonSeekableStream(fs))
                                        (backend as Library.Interface.IStreamingBackend).Get(files[i].remotefilename, nss);
                                }
                                else
                                    backend.Get(files[i].remotefilename, cf);

                                e = null;
                            }
                            catch (Exception ex)
                            {
                                e = ex;
                            }

                            if (e != null)
                                Console.WriteLine("failed\n*** Error: {0}", e.ToString());
                            else
                                Console.WriteLine("done");

                            Console.Write("Checking hash ... ");

                            using (System.IO.FileStream fs = new System.IO.FileStream(cf, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                                if (Convert.ToBase64String(sha.ComputeHash(fs)) != Convert.ToBase64String(files[i].hash))
                                {
                                    if (dummyFileHash != null && Convert.ToBase64String(sha.ComputeHash(fs)) == Convert.ToBase64String(dummyFileHash))
                                        Console.WriteLine("failed\n*** Downloaded file was the dummy file");
                                    else
                                        Console.WriteLine("failed\n*** Downloaded file was corrupt");
                                }
                                else
                                    Console.WriteLine("done");
                        }
                    }

                    Console.WriteLine("Deleting files...");

                    foreach (TempFile tx in files)
                        try { backend.Delete(tx.remotefilename); }
                        catch (Exception ex)
                        {
                            Console.WriteLine("*** Failed to delete file {0}, message: {1}", tx.remotefilename, ex.ToString());
                        }

                    curlist = backend.List();
                    foreach (Library.Interface.IFileEntry fe in curlist)
                        if (!fe.IsFolder)
                        {
                            Console.WriteLine("*** Remote folder contains {0} after cleanup", fe.Name);
                        }

                }
            }
            finally
            {
                foreach (Library.Interface.IGenericModule m in loadedModules)
                    if (m is IDisposable)
                        ((IDisposable)m).Dispose();
            }

            return true;
        }

        private static void Uploadfile(string localfilename, int i, string remotefilename, IBackend backend, bool disableStreaming)
        {
            Console.Write("Uploading file {0}, {1} ... ", i, Duplicati.Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(localfilename).Length));
            Exception e = null;

            try
            {
                if (backend is Library.Interface.IStreamingBackend && !disableStreaming)
                {
                    using (System.IO.FileStream fs = new System.IO.FileStream(localfilename, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                    using (NonSeekableStream nss = new NonSeekableStream(fs))
                        (backend as Library.Interface.IStreamingBackend).Put(remotefilename, nss);
                }
                else
                    backend.Put(remotefilename, localfilename);

                e = null;
            }
            catch (Exception ex)
            {
                e = ex;
            }

            if (e != null)
            {
                Console.WriteLine("Failed to upload file {0}, error message: {1}, remote name: {2}", i, e.ToString(), remotefilename);
                while (e.InnerException != null)
                {
                    e = e.InnerException;
                    Console.WriteLine(string.Format("  Inner exception: {0}", e.ToString()));
                }
            }
            else
            {
                Console.WriteLine(" done!");
            }
        }

        private static string CreateRandomFile(Library.Utility.TempFolder tf, int i, int min_file_size, int max_file_size, Random rnd)
        {
            Console.Write("Generating file {0}", i);
            string filename = System.IO.Path.Combine(tf, i.ToString());
            using (System.IO.FileStream fs = new System.IO.FileStream(filename, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write))
            {
                //Random size
                byte[] buf = new byte[1024];
                int size = rnd.Next(min_file_size, max_file_size);

                Console.WriteLine(" ({0})", Duplicati.Library.Utility.Utility.FormatSizeString(size));

                while (size > 0)
                {
                    rnd.NextBytes(buf);
                    fs.Write(buf, 0, Math.Min(buf.Length, size));
                    size -= buf.Length;
                }
            }

            return filename;
        }

        public static IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("reruns", CommandLineArgument.ArgumentType.Integer, "The number of test runs to perform", "A number that describes how many times the test is performed", "5"),
                    new CommandLineArgument("tempdir", CommandLineArgument.ArgumentType.Path, "The path used to store temporary files", "The backend tester will use the system default temp path. You can set this option to choose another path."),
                    new CommandLineArgument("extended-chars", CommandLineArgument.ArgumentType.String, "A list of allowed extended filename chars", "A list of characters besides {a-z, A-Z, 0-9} to use when generating filenames", ExtendedChars),
                    new CommandLineArgument("number-of-files", CommandLineArgument.ArgumentType.Integer, "The number of files to test with", "An integer describing how many files to upload during a test run", "10"),
                    new CommandLineArgument("min-file-size", CommandLineArgument.ArgumentType.Size, "The minimum allowed file size", "File sizes are chosen at random, this value is the lower bound", "1kb"),
                    new CommandLineArgument("max-file-size", CommandLineArgument.ArgumentType.Size, "The maximum allowed file size", "File sizes are chosen at random, this value is the upper bound", "50mb"),
                    new CommandLineArgument("min-filename-length", CommandLineArgument.ArgumentType.Integer, "The minimum allowed filename length", "File name lengths are chosen at random, this value is the lower bound", "5"),
                    new CommandLineArgument("max-filename-length", CommandLineArgument.ArgumentType.Integer, "The minimum allowed filename length", "File name lengths are chosen at random, this value is the upper bound", "80"),
                    new CommandLineArgument("auto-create-folder", CommandLineArgument.ArgumentType.Boolean, "Allows automatic folder creation", "A value that indicates if missing folders are created automatically", "false"),
                    new CommandLineArgument("skip-overwrite-test", CommandLineArgument.ArgumentType.Boolean, "Bypasses the overwrite test", "A value that indicates if dummy files should be uploaded prior to uploading the real files", "false"),
                    new CommandLineArgument("auto-clean", CommandLineArgument.ArgumentType.Boolean, "Removes any files found in target folder", "A value that indicates if all files in the target folder should be deleted before starting the first test", "false"),
                    new CommandLineArgument("force", CommandLineArgument.ArgumentType.Boolean, "Activates file deletion", "A value that indicates if existing files should really be deleted when using auto-clean", "false"),
                });

            }
        }
    }
}
