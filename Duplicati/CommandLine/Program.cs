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
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.CommandLine
{
    class Program
    {
        static int Main(string[] args)
        {
            bool verboseErrors = false;
            try
            {
                List<string> cargs = new List<string>(args);
                string filter = Duplicati.Library.Utility.FilenameFilter.EncodeAsFilter(Duplicati.Library.Utility.FilenameFilter.ParseCommandLine(cargs, true));
                Dictionary<string, string> options = Library.Utility.CommandLineParser.ExtractOptions(cargs);

                verboseErrors = Library.Utility.Utility.ParseBoolOption(options, "debug-output");

                //If we are on Windows, append the bundled "win-tools" programs to the search path
                //We add it last, to allow the user to override with other versions
                if (!Library.Utility.Utility.IsClientLinux)
                {
                    string wintools = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "win-tools");
                    Environment.SetEnvironmentVariable("PATH",
                        Environment.GetEnvironmentVariable("PATH") +
                        System.IO.Path.PathSeparator.ToString() +
                        wintools +
                        System.IO.Path.PathSeparator.ToString() +
                        System.IO.Path.Combine(wintools, "gpg") //GPG needs to be in a subfolder for wrapping reasons
                    );
                }

#if DEBUG
                if (cargs.Count > 1 && cargs[0].ToLower() == "unittest")
                {
                    //The unit test is only enabled in DEBUG builds
                    //it works by getting a list of folders, and treats them as 
                    //if they have the same data, but on different times

                    //The first folder is used to make a full backup,
                    //and each subsequent folder is used to make an incremental backup

                    //After all backups are made, the files are restored and verified against
                    //the original folders.

                    //The best way to test it, is to use SVN checkouts at different
                    //revisions, as this is how a regular folder would evolve

                    cargs.RemoveAt(0);
                    UnitTest.RunTest(cargs.ToArray(), options);
                    return 0;
                }
#endif

                foreach (string internaloption in Library.Main.Options.InternalOptions)
                    if (options.ContainsKey(internaloption))
                    {
                        Console.WriteLine(Strings.Program.InternalOptionUsedError, internaloption);
                        return 200;
                    }
                
                if ((options.ContainsKey("parameters-file") && !string.IsNullOrEmpty("parameters-file")) || (options.ContainsKey("parameter-file") && !string.IsNullOrEmpty("parameter-file")))
                {
                    string filename;
                    if (options.ContainsKey("parameters-file") && !string.IsNullOrEmpty("parameters-file"))
                    {
                        filename = options["parameters-file"];
                        options.Remove("parameters-file");
                    }
                    else
                    {
                        filename = options["parameter-file"];
                        options.Remove("parameter-file");
                    }

                    if (!ReadOptionsFromFile(filename, ref filter, cargs, options))
                        return 100;
                }

                //After checking for internal options, we set the filter option
                if (!string.IsNullOrEmpty(filter))
                    options["filter"] = filter;

                string command = cargs[0];
                cargs.RemoveAt(0);

                if (!options.ContainsKey("passphrase"))
                    if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PASSPHRASE")))
                        options["passphrase"] = System.Environment.GetEnvironmentVariable("PASSPHRASE");

                if (!options.ContainsKey("auth-password"))
                    if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_PASSWORD")))
                        options["auth-password"] = System.Environment.GetEnvironmentVariable("AUTH_PASSWORD");

                if (!options.ContainsKey("auth-username"))
                    if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_USERNAME")))
                        options["auth-username"] = System.Environment.GetEnvironmentVariable("AUTH_USERNAME");

                var knownCommands = new Dictionary<string, Func<List<string>, Dictionary<string, string>, int>>(StringComparer.InvariantCultureIgnoreCase);
                knownCommands["help"] = Commands.Help;                
                knownCommands["list"] = Commands.List;
                knownCommands["delete"] = Commands.Delete;
                knownCommands["backup"] = Commands.Backup;
                knownCommands["restore"] = Commands.Restore;

                knownCommands["repair"] = Commands.Repair;

                knownCommands["compact"] = Commands.Compact;
                knownCommands["recreate-database"] = Commands.RecreateDatabase;
                knownCommands["create-bugreport-database"] = Commands.CreateBugreportDatabase;

                
                if (knownCommands.ContainsKey(command))
                {
                    return knownCommands[command](cargs, options);
                }
                else
                {
                    Commands.PrintInvalidCommand(cargs);
                    return 200;
                }
            }
            catch (Exception ex)
            {
                while (ex is System.Reflection.TargetInvocationException && ex.InnerException != null)
                    ex = ex.InnerException;

                if (!(ex is Library.Interface.CancelException))
                {
                    if (!string.IsNullOrEmpty(ex.Message))
                    {
                        Console.Error.WriteLine();
                        Console.Error.WriteLine(verboseErrors ? ex.ToString() : ex.Message);
                    }
                }
                else
                {
                    Console.Error.WriteLine(Strings.Program.UnhandledException, ex.ToString());

                    while (ex.InnerException != null)
                    {
                        ex = ex.InnerException;
                        Console.Error.WriteLine();
                        Console.Error.WriteLine(Strings.Program.UnhandledInnerException, ex.ToString());
                    }
                }

                //Error = 100
                return 100;
            }
        }

        public static IList<Library.Interface.ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<Library.Interface.ICommandLineArgument>(new Library.Interface.ICommandLineArgument[] {
                    new Library.Interface.CommandLineArgument("parameters-file", Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.ParametersFileOptionShort, Strings.Program.ParametersFileOptionLong, "", new string[] {"parameter-file"})
                });
            }
        }

        private static bool ReadOptionsFromFile(string filename, ref string filter, IList<string> cargs, Dictionary<string, string> options)
        {
            try
            {
                List<string> fargs = new List<string>(Library.Utility.Utility.ReadFileWithDefaultEncoding(filename).Replace("\r", "").Split(new String[] { "\n" }, StringSplitOptions.RemoveEmptyEntries));

                string newfilter = Duplicati.Library.Utility.FilenameFilter.EncodeAsFilter(Duplicati.Library.Utility.FilenameFilter.ParseCommandLine(fargs, true));

                // If the user specifies parameters-file, all filters must be in the file.
                // Allowing to specify some filters on the command line could result in wrong filter ordering
                if (!string.IsNullOrEmpty(filter) && !string.IsNullOrEmpty(newfilter))
                    throw new Exception(Strings.Program.FiltersCannotBeUsedWithFileError);

                filter = newfilter;

                Dictionary<string, string> opt = Library.Utility.CommandLineParser.ExtractOptions(fargs);
                string newsource = null, newtarget = null;
                foreach (KeyValuePair<String, String> keyvalue in opt)
                {
                    switch (keyvalue.Key.ToLower())
                    {
                        // This replaces any previous value, file parameters take precedence;
                        case "source":
                            newsource = keyvalue.Value;
                            break;
                        case "target":
                            newtarget = keyvalue.Value;
                            break;
                        default:
                            options[keyvalue.Key] = keyvalue.Value;
                            break;
                    }
                }

                // When the action is to backup, allow the source and/or the target 
                // to be specified in the parameters file as --source= and --target=. 
                // Note: this block is faily complex due to the way parameters are handled by the rest of the
                // procedure. It could likely be much simpler and versatile (allow --source and --target on 
                // restore or other actions) with some refactoring of the parameters decision tree
                if (!string.IsNullOrEmpty(newsource) || !string.IsNullOrEmpty(newtarget))
                {
                    bool isrestore = cargs.Count > 0 && cargs[0] == "restore";

                    if (cargs.Count == 1 || cargs.Count == 0)
                    {
                        // if either is empty loading will fail later, so we don't really care.
                        if (!String.IsNullOrEmpty(newsource)) cargs.Add(newsource);
                        if (!String.IsNullOrEmpty(newtarget)) cargs.Add(newtarget);
                    }
                    else
                    {
                        if (isrestore)
                        {
                            if (!String.IsNullOrEmpty(newtarget)) cargs[1] = newtarget;
                            if (!String.IsNullOrEmpty(newsource)) cargs.Insert(1, newsource);
                        }
                        else
                        {
                            if (!String.IsNullOrEmpty(newtarget)) cargs[1] = newsource;
                            if (!String.IsNullOrEmpty(newsource)) cargs.Add(newtarget);
                        }
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(Strings.Program.FailedToParseParametersFileError, filename, e.Message);
                return false;
            }
        }
    }
}
