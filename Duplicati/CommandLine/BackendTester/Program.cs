#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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
using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Library.Backend;

namespace Duplicati.CommandLine.BackendTester
{
    class Program
    {
        class TempFile
        {
            public string filename;
            public byte[] hash;
            public long length;
            public bool found = false;

            public TempFile(string filename, byte[] hash, long length)
            {
                this.filename = filename;
                this.hash = hash;
                this.length = length;
            }
        }

        private const string ValidFilenameChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ123456789";
        private const string ExtendedChars = "-_',=)(&%$#@! +";


        static void Main(string[] _args)
        {
            List<string> args = new List<string>(_args);
            Dictionary<string, string> options = CommandLineParser.ExtractOptions(args);

            if (!options.ContainsKey("ftp_password") && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("FTP_PASSWORD")))
                options["ftp_password"] = System.Environment.GetEnvironmentVariable("FTP_PASSWORD");

            if (!options.ContainsKey("ftp_username") && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("FTP_USERNAME")))
                options["ftp_username"] = System.Environment.GetEnvironmentVariable("FTP_USERNAME");

            if (options.ContainsKey("tempdir") && !string.IsNullOrEmpty(options["tempdir"]))
                Library.Core.TempFolder.SystemTempPath = options["tempdir"];


            if (args.Count != 1 || args[0].ToLower() == "help" || args[0] == "?")
            {
                Console.WriteLine("Usage: <protocol>://<username>:<password>@<path>");
                Console.WriteLine("Example: ftp://user:pass@server/folder");
                Console.WriteLine();
                Console.WriteLine("Supported backends: " + string.Join(",", Duplicati.Library.Backend.BackendLoader.Backends));

                Console.WriteLine();
                List<string> lines = new List<string>();
                foreach (Library.Backend.ICommandLineArgument arg in SupportedCommands)
                    Library.Backend.CommandLineArgument.PrintArgument(lines, arg);

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

        static bool Run(List<string> args, Dictionary<string, string> options, bool first)
        {
            string allowedChars = ValidFilenameChars;
            if (options.ContainsKey("extended-chars"))
                allowedChars += options["extended-chars"];
            else
                allowedChars += ExtendedChars;

            Library.Backend.IBackend backend = Library.Backend.BackendLoader.GetBackend(args[0], options);
            if (backend == null)
            {
                Console.WriteLine("Unsupported backend");
                Console.WriteLine();
                Console.WriteLine("Supported backends: " + string.Join(",", Duplicati.Library.Backend.BackendLoader.Backends));
                return false;
            }

            List<Library.Backend.FileEntry> curlist = backend.List();
            foreach (Library.Backend.FileEntry fe in curlist)
                if (!fe.IsFolder)
                {
                    if (options.ContainsKey("auto-clean") && first)
                        if (options.ContainsKey("force"))
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
            bool disableStreaming = options.ContainsKey("disable-streaming-transfers");

            if (options.ContainsKey("number-of-files"))
                number_of_files = int.Parse(options["number-of-files"]);
            if (options.ContainsKey("min-file-size"))
                min_file_size = (int)Duplicati.Library.Core.Sizeparser.ParseSize(options["min-file-size"], "mb");
            if (options.ContainsKey("max-file-size"))
                max_file_size = (int)Duplicati.Library.Core.Sizeparser.ParseSize(options["max-file-size"]);

            if (options.ContainsKey("min-filename-length"))
                min_filename_size = int.Parse(options["min-filename-length"]);
            if (options.ContainsKey("max-filename-length"))
                max_filename_size = int.Parse(options["max-filename-length"]);

            Random rnd = new Random();
            System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();

            //Create random files
            using (Library.Core.TempFolder tf = new Duplicati.Library.Core.TempFolder())
            {
                List<TempFile> files = new List<TempFile>();
                for (int i = 0; i < number_of_files; i++)
                {
                    Console.Write("Generating file {0}", i);
                    StringBuilder filename = new StringBuilder();
                    int filenamelen = rnd.Next(min_filename_size, max_filename_size);
                    for (int j = 0; j < filenamelen; j++)
                        filename.Append(allowedChars[rnd.Next(0, allowedChars.Length)]);

                    using (System.IO.FileStream fs = new System.IO.FileStream(System.IO.Path.Combine(tf, i.ToString()), System.IO.FileMode.CreateNew, System.IO.FileAccess.Write))
                    {
                        //Random size
                        byte[] buf = new byte[1024];
                        int size = rnd.Next(min_file_size, max_file_size);

                        Console.WriteLine(" ({0})", Duplicati.Library.Core.Utility.FormatSizeString(size));

                        while (size > 0)
                        {
                            rnd.NextBytes(buf);
                            fs.Write(buf, 0, Math.Min(buf.Length, size));
                            size -= buf.Length;
                        }
                    }

                    //Calculate local hash and length
                    using (System.IO.FileStream fs = new System.IO.FileStream(System.IO.Path.Combine(tf, i.ToString()), System.IO.FileMode.Open, System.IO.FileAccess.Read))
                        files.Add(new TempFile(filename.ToString(), sha.ComputeHash(fs), fs.Length));
                }


                Console.WriteLine("Uploading files ...");

                for (int i = 0; i < files.Count; i++)
                {
                    Console.Write("Uploading file {0}, {1} ... ", i, Duplicati.Library.Core.Utility.FormatSizeString(new System.IO.FileInfo(System.IO.Path.Combine(tf, i.ToString())).Length));
                    try 
                    {
                        if (backend is Library.Backend.IStreamingBackend && !disableStreaming)
                        {
                            using (System.IO.FileStream fs = new System.IO.FileStream(System.IO.Path.Combine(tf, i.ToString()), System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                            using (NonSeekableStream nss = new NonSeekableStream(fs))
                                (backend as Library.Backend.IStreamingBackend).Put(files[i].filename, nss);
                        }
                        else
                            backend.Put(files[i].filename, System.IO.Path.Combine(tf, i.ToString())); 
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed to upload file {0}, error message: {1}", i, ex.ToString());
                    }
                    Console.WriteLine(" done!");
                }

                Console.WriteLine("Verifying file list ...");

                curlist = backend.List();
                foreach (Library.Backend.FileEntry fe in curlist)
                    if (!fe.IsFolder)
                    {
                        bool found = false;
                        foreach (TempFile tx in files)
                            if (tx.filename == fe.Name)
                            {
                                if (tx.found)
                                    Console.WriteLine("*** File with name {0} was found more than once", tx.filename);
                                found = true;
                                tx.found = true;
                                break;
                            }

                        if (!found)
                            Console.WriteLine("*** File with name {0} was found on server but not uploaded!", fe.Name);
                    }

                foreach (TempFile tx in files)
                    if (!tx.found)
                        Console.WriteLine("*** File with name {0} was uploaded but not found afterwards", tx.filename);

                Console.WriteLine("Downloading files");

                for(int i = 0; i < files.Count; i++)
                {
                    using (Duplicati.Library.Core.TempFile cf = new Duplicati.Library.Core.TempFile())
                    {
                        Exception e = null;
                        Console.Write("Downloading file {0} ... ", i);
                        try 
                        {
                            if (backend is Library.Backend.IStreamingBackend && !disableStreaming)
                            {
                                using (System.IO.FileStream fs = new System.IO.FileStream(cf, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                                using (NonSeekableStream nss = new NonSeekableStream(fs))
                                    (backend as Library.Backend.IStreamingBackend).Get(files[i].filename, nss);
                            }
                            else
                                backend.Get(files[i].filename, cf); 
                        }
                        catch (Exception ex) { e = ex; }

                        if (e != null)
                            Console.WriteLine("failed\n*** Error: {0}", e.ToString());
                        else
                            Console.WriteLine("done");

                        Console.Write("Checking hash ... ");

                        using (System.IO.FileStream fs = new System.IO.FileStream(cf, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                            if (Convert.ToBase64String(sha.ComputeHash(fs)) != Convert.ToBase64String(files[i].hash))
                                Console.WriteLine("failed\n*** Downloaded file was corrupt");
                            else
                                Console.WriteLine("done");
                    }
                }

                Console.WriteLine("Deleting files...");

                foreach (TempFile tx in files)
                    try { backend.Delete(tx.filename); }
                    catch (Exception ex)
                    {
                        Console.WriteLine("*** Failed to delete file {0}, message: {1}", tx.filename, ex.ToString());
                    }

                curlist = backend.List();
                foreach (Library.Backend.FileEntry fe in curlist)
                    if (!fe.IsFolder)
                    {
                        Console.WriteLine("*** Remote folder contains {0} after cleanup", fe.Name);
                    }

            }

            return true;
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
                    new CommandLineArgument("min-file-size", CommandLineArgument.ArgumentType.Size, "The minimum allowed file size", "File sizes are chosen at random, this valus is the lower bound", "1kb"),
                    new CommandLineArgument("max-file-size", CommandLineArgument.ArgumentType.Size, "The maximum allowed file size", "File sizes are chosen at random, this valus is the upper bound", "50mb"),
                    new CommandLineArgument("min-filename-length", CommandLineArgument.ArgumentType.Integer, "The minimum allowed filename length", "File name lengths are chosen at random, this valus is the lower bound", "5"),
                    new CommandLineArgument("max-filename-length", CommandLineArgument.ArgumentType.Integer, "The minimum allowed filename length", "File name lengths are chosen at random, this valus is the upper bound", "80")
                });

            }
        }
    }
}
