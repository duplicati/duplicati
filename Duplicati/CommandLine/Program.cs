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
using System.Linq;
using Duplicati.Library.Localization.Short;

namespace Duplicati.CommandLine
{
    public class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            return Duplicati.Library.AutoUpdater.UpdaterManager.RunFromMostRecent(typeof(Program).GetMethod("RealMain"), args);
        }

        public static int RealMain(string[] args)
        {
            bool verboseErrors = false;
            bool verbose = false;
            try
            {
            	List<string> cargs = new List<string>(args);

                var tmpparsed = Library.Utility.FilterCollector.ExtractOptions(cargs);
				var options = tmpparsed.Item1;
				var filter = tmpparsed.Item2;
				
                verboseErrors = Library.Utility.Utility.ParseBoolOption(options, "debug-output");
                verbose = Library.Utility.Utility.ParseBoolOption(options, "verbose");

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

                if (cargs.Count == 1 && string.Equals(cargs[0], "changelog", StringComparison.InvariantCultureIgnoreCase))
                {
                    var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "changelog.txt");
                    Console.WriteLine(System.IO.File.ReadAllText(path));
                    return 0;
                }

                foreach (string internaloption in Library.Main.Options.InternalOptions)
                    if (options.ContainsKey(internaloption))
                    {
                        Console.WriteLine(Strings.Program.InternalOptionUsedError(internaloption));
                        return 200;
                    }
                
                // Probe for "help" to avoid extra processing
                bool isHelp = cargs.Count == 0 || (cargs.Count >= 1 && string.Equals(cargs[0], "help", StringComparison.InvariantCultureIgnoreCase));
                if (!isHelp && ((options.ContainsKey("parameters-file") && !string.IsNullOrEmpty("parameters-file")) || (options.ContainsKey("parameter-file") && !string.IsNullOrEmpty("parameter-file")) || (options.ContainsKey("parameterfile") && !string.IsNullOrEmpty("parameterfile"))))
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

                string command;
                if (cargs.Count > 0)
                {
                    command = cargs[0];
                    cargs.RemoveAt(0);
                }
                else
                    command = "help";
                
                // Update probe for help
                isHelp = string.Equals(command, "help", StringComparison.InvariantCultureIgnoreCase);

                // Skip the env read if the command is help, otherwise we may report weirdness
                if (!isHelp)
                {
                    if (!options.ContainsKey("passphrase"))
                        if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PASSPHRASE")))
                            options["passphrase"] = System.Environment.GetEnvironmentVariable("PASSPHRASE");
    
                    if (!options.ContainsKey("auth-password"))
                        if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_PASSWORD")))
                            options["auth-password"] = System.Environment.GetEnvironmentVariable("AUTH_PASSWORD");
    
                    if (!options.ContainsKey("auth-username"))
                        if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_USERNAME")))
                            options["auth-username"] = System.Environment.GetEnvironmentVariable("AUTH_USERNAME");
                }
                
                var knownCommands = new Dictionary<string, Func<List<string>, Dictionary<string, string>, Library.Utility.IFilter, int>>(StringComparer.InvariantCultureIgnoreCase);
                knownCommands["help"] = Commands.Help;                
                knownCommands["find"] = Commands.List;
                knownCommands["list"] = Commands.List;
                knownCommands["delete"] = Commands.Delete;
                knownCommands["backup"] = Commands.Backup;
                knownCommands["restore"] = Commands.Restore;

                knownCommands["repair"] = Commands.Repair;

                knownCommands["compact"] = Commands.Compact;
                knownCommands["create-report"] = Commands.CreateBugReport;
                knownCommands["compare"] = Commands.ListChanges;
                knownCommands["test"] = Commands.Test;
                knownCommands["verify"] = Commands.Test;
                knownCommands["test-filters"] = Commands.TestFilters;
                knownCommands["affected"] = Commands.Affected;

                if (!isHelp && verbose)
                {
                    Console.WriteLine("Input command: {0}", command);
                    
                    Console.WriteLine("Input arguments: ");
                    foreach(var a in cargs)
                        Console.WriteLine("\t{0}", a);
                    Console.WriteLine();                        
                        
                    Console.WriteLine("Input options: ");
                    foreach(var n in options)
                        Console.WriteLine("{0}: {1}", n.Key, n.Value);
                    Console.WriteLine();                        
                }

                Duplicati.Library.Utility.TempFile.RemoveOldApplicationTempFiles((path, ex) => {
                    if (verbose)
                        Console.WriteLine(string.Format("Failed to delete temp file: {0}", path)); 
                });

                var autoupdate = Library.Utility.Utility.ParseBoolOption(options, "auto-update");
                options.Remove("auto-update");

                if (knownCommands.ContainsKey(command))
                {
                    var res = knownCommands[command](cargs, options, filter);

                    if (autoupdate)
                    {
                        var update = Library.AutoUpdater.UpdaterManager.LastUpdateCheckVersion;
                        if (update == null)
                            update = Library.AutoUpdater.UpdaterManager.CheckForUpdate();

                        if (update != null && update.Version != Library.AutoUpdater.UpdaterManager.SelfVersion.Version)
                        {
                            Console.WriteLine("Found update \"{0}\", downloading ...", update.Displayname);
                            long lastpg = 0;
                            Library.AutoUpdater.UpdaterManager.DownloadAndUnpackUpdate(update, f => {
                                var npg = (long)(f*100);
                                if (Math.Abs(npg - lastpg) >= 5 || (npg == 100 && lastpg != 100))
                                {
                                    lastpg = npg;
                                    Console.WriteLine("Downloading {0}% ...", npg);
                                }
                            });
                            Console.WriteLine("Update \"{0}\" ({1}) installed, using on next launch", update.Displayname, update.Version);
                        }
                    }

                    return res;
                }
                else
                {
                    Commands.PrintInvalidCommand(command, cargs);
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
                    Console.Error.WriteLine(Strings.Program.UnhandledException(ex.ToString()));

                    while (ex.InnerException != null)
                    {
                        ex = ex.InnerException;
                        Console.Error.WriteLine();
                        Console.Error.WriteLine(Strings.Program.UnhandledInnerException(ex.ToString()));
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
                    new Library.Interface.CommandLineArgument("parameters-file", Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.ParametersFileOptionShort, Strings.Program.ParametersFileOptionLong, "", new string[] {"parameter-file", "parameterfile"}),
                    new Library.Interface.CommandLineArgument("include", Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.IncludeShort, Strings.Program.IncludeLong),
                    new Library.Interface.CommandLineArgument("exclude", Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.ExcludeShort, Strings.Program.ExcludeLong),
                    new Library.Interface.CommandLineArgument("control-files", Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.ControlFilesOptionShort, Strings.Program.ControlFilesOptionLong, "false"),
                    new Library.Interface.CommandLineArgument("quiet-console", Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.QuietConsoleOptionShort, Strings.Program.QuietConsoleOptionLong, "false"),
                    new Library.Interface.CommandLineArgument("auto-update", Library.Interface.CommandLineArgument.ArgumentType.Boolean, LC.L("Toggle automatic updates"), LC.L("Set this option if you prefer to have the commandline version automatically update"), "false"),
                });
            }
        }

        private static bool ReadOptionsFromFile(string filename, ref Library.Utility.IFilter filter, List<string> cargs, Dictionary<string, string> options)
        {
            try
            {
                List<string> fargs = new List<string>(Library.Utility.Utility.ReadFileWithDefaultEncoding(filename).Replace("\r", "").Split(new String[] { "\n" }, StringSplitOptions.RemoveEmptyEntries));
                var newsource = new List<string>();
                string newtarget = null;

                var tmpparsed = Library.Utility.FilterCollector.ExtractOptions(fargs, (key, value) => {
					if (key.Equals("source", StringComparison.InvariantCultureIgnoreCase))
					{
						newsource.Add(value);
						return false;
					}
					else if (key.Equals("target", StringComparison.InvariantCultureIgnoreCase))
					{
						newtarget = value;
						return false;
					}
					
					return true;
				});
				
                var opt = tmpparsed.Item1;
                var newfilter = tmpparsed.Item2;

                // If the user specifies parameters-file, all filters must be in the file.
                // Allowing to specify some filters on the command line could result in wrong filter ordering
                if (!filter.Empty && !newfilter.Empty)
                    throw new Exception(Strings.Program.FiltersCannotBeUsedWithFileError);

				if (!newfilter.Empty)
                	filter = newfilter;

                foreach (KeyValuePair<String, String> keyvalue in opt)
                    options[keyvalue.Key] = keyvalue.Value;
                    
                if (!string.IsNullOrEmpty(newtarget))
               	{
               		if (cargs.Count <= 1)
               			cargs.Add(newtarget);
               		else
               			cargs[1] = newtarget;
               	}
               	
               	cargs.AddRange(newsource);

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(Strings.Program.FailedToParseParametersFileError(filename, e.Message));
                return false;
            }
        }
    }
}
