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

using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.CommandLine.BackendTool
{
    public class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static int Main(string[] _args)
        {
            bool debugoutput = false;
            try
            {
                Library.AutoUpdater.PreloadSettingsLoader.ConfigurePreloadSettings(ref _args, Library.AutoUpdater.PackageHelper.NamedExecutable.BackendTool);

                List<string> args = new List<string>(_args);
                Dictionary<string, string> options = Library.Utility.CommandLineParser.ExtractOptions(args);

                if (!options.ContainsKey("auth_password") && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_PASSWORD")))
                    options["auth_password"] = System.Environment.GetEnvironmentVariable("AUTH_PASSWORD");

                if (!options.ContainsKey("auth_username") && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_USERNAME")))
                    options["auth_username"] = System.Environment.GetEnvironmentVariable("AUTH_USERNAME");

                if (options.ContainsKey("tempdir") && !string.IsNullOrEmpty(options["tempdir"]))
                    Library.Utility.TempFolder.SystemTempPath = options["tempdir"];

                debugoutput = Duplicati.Library.Utility.Utility.ParseBoolOption(options, "debug-output");

                string command = null;
                if (args.Count >= 2)
                {
                    if (args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
                        command = "list";
                    else if (args[0].Equals("get", StringComparison.OrdinalIgnoreCase))
                        command = "get";
                    else if (args[0].Equals("put", StringComparison.OrdinalIgnoreCase))
                        command = "put";
                    else if (args[0].Equals("delete", StringComparison.OrdinalIgnoreCase))
                        command = "delete";
                    else if (args[0].Equals("create-folder", StringComparison.OrdinalIgnoreCase))
                        command = "create";
                    else if (args[0].Equals("createfolder", StringComparison.OrdinalIgnoreCase))
                        command = "create";
                }


                if (args.Count < 2 || HelpOptionExtensions.IsArgumentAnyHelpString(args) || command == null)
                {
                    if (command == null && args.Count >= 2)
                    {
                        Console.WriteLine("Unsupported command: {0}", args[0]);
                        Console.WriteLine();
                    }

                    Console.WriteLine("Usage: <command> <protocol>://<username>:<password>@<path> [filename]");
                    Console.WriteLine("Example: LIST ftp://user:pass@server/folder");
                    Console.WriteLine();
                    Console.WriteLine("Supported backends: " + string.Join(",", Duplicati.Library.DynamicLoader.BackendLoader.Keys));
                    Console.WriteLine("Supported commands: GET PUT LIST DELETE CREATEFOLDER");

                    return 200;
                }

                var modules = (from n in Library.DynamicLoader.GenericLoader.Modules
                               where n is Library.Interface.IConnectionModule
                               select n).ToArray();

                var uri = new Library.Utility.Uri(args[1]);
                var qp = uri.QueryParameters;

                var backendOpts = new Dictionary<string, string>();
                foreach (var k in qp.Keys.Cast<string>())
                    backendOpts[k] = qp[k];

                foreach (var k in backendOpts.Keys)
                {
                    options.Remove(k);
                }

                foreach (var n in modules)
                {
                    n.Configure(options);
                    n.Configure(backendOpts);
                }

                using (var backend = Library.DynamicLoader.BackendLoader.GetBackend(args[1], options))
                {
                    if (backend == null)
                        throw new UserInformationException("Backend not supported", "InvalidBackend");

                    if (command == "list")
                    {
                        if (args.Count != 2)
                            throw new UserInformationException(string.Format("too many arguments: {0}", string.Join(",", args)), "BackendToolTooManyArguments");
                        Console.WriteLine("{0}\t{1}\t{2}\t{3}", "Name", "Dir/File", "LastChange", "Size");

                        foreach (var e in backend.ListAsync(CancellationToken.None).ToBlockingEnumerable())
                            Console.WriteLine("{0}\t{1}\t{2}\t{3}", e.Name, e.IsFolder ? "Dir" : "File", e.LastModification, e.Size < 0 ? "" : Library.Utility.Utility.FormatSizeString(e.Size));

                        return 0;
                    }
                    else if (command == "create")
                    {
                        if (args.Count != 2)
                            throw new UserInformationException(string.Format("too many arguments: {0}", string.Join(",", args)), "BackendToolTooManyArguments");

                        backend.CreateFolderAsync(CancellationToken.None).Await();

                        return 0;
                    }
                    else if (command == "delete")
                    {
                        if (args.Count < 3)
                            throw new UserInformationException("DELETE requires a filename argument", "BackendToolDeleteRequiresAnArgument");
                        if (args.Count > 3)
                            throw new Exception(string.Format("too many arguments: {0}", string.Join(",", args)));
                        backend.DeleteAsync(Path.GetFileName(args[2]), CancellationToken.None).Await();

                        return 0;
                    }
                    else if (command == "get")
                    {
                        if (args.Count < 3)
                            throw new UserInformationException("GET requires a filename argument", "BackendToolGetRequiresAnArgument");
                        if (args.Count > 3)
                            throw new UserInformationException(string.Format("too many arguments: {0}", string.Join(",", args)), "BackendToolTooManyArguments");
                        if (File.Exists(args[2]))
                            throw new UserInformationException("File already exists, not overwriting!", "BackendToolFileAlreadyExists");
                        backend.GetAsync(Path.GetFileName(args[2]), args[2], CancellationToken.None).Await();

                        return 0;
                    }
                    else if (command == "put")
                    {
                        if (args.Count < 3)
                            throw new UserInformationException("PUT requires a filename argument", "BackendToolPutRequiresAndArgument");
                        if (args.Count > 3)
                            throw new UserInformationException(string.Format("too many arguments: {0}", string.Join(",", args)), "BackendToolTooManyArguments");

                        backend.PutAsync(Path.GetFileName(args[2]), args[2], CancellationToken.None).Await();

                        return 0;
                    }

                    throw new Exception("Internal error");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Command failed: " + ex.Message);
                if (debugoutput || !(ex is UserInformationException))
                    Console.WriteLine(ex);
                return 100;
            }
        }
    }
}
