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
using System.IO;

namespace Duplicati.CommandLine
{
    public class Program
    {
        public static bool FROM_COMMANDLINE = false;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            Duplicati.Library.AutoUpdater.UpdaterManager.IgnoreWebrootFolder = true;
            return Duplicati.Library.AutoUpdater.UpdaterManager.RunFromMostRecent(typeof(Program).GetMethod("RealMain"), args);
        }

        public static int RealMain(string[] args)
        {
            Library.UsageReporter.Reporter.Initialize();
            FROM_COMMANDLINE = true;
            try
            {
                //If we are on Windows, append the bundled "win-tools" programs to the search path
                //We add it last, to allow the user to override with other versions
                if (Library.Utility.Utility.IsClientWindows)
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

                return RunCommandLine(Console.Out, Console.Error, c => { }, args);
            }
            finally
            {
                Library.UsageReporter.Reporter.ShutDown();
            }
        }

        private static Dictionary<string, Func<TextWriter, Action<Library.Main.Controller>, List<string>, Dictionary<string, string>, Library.Utility.IFilter, int>> CommandMap
        {
            get
            {
                var knownCommands = new Dictionary<string, Func<TextWriter, Action<Library.Main.Controller>, List<string>, Dictionary<string, string>, Library.Utility.IFilter, int>>(StringComparer.InvariantCultureIgnoreCase);
                knownCommands["help"] = Commands.Help;
                knownCommands["example"] = Commands.Examples;
                knownCommands["examples"] = Commands.Examples;

                knownCommands["find"] = Commands.List;
                knownCommands["list"] = Commands.List;

                knownCommands["delete"] = Commands.Delete;
                knownCommands["backup"] = Commands.Backup;
                knownCommands["restore"] = Commands.Restore;

                knownCommands["repair"] = Commands.Repair;
                knownCommands["purge"] = Commands.PurgeFiles;
                knownCommands["list-broken-files"] = Commands.ListBrokenFiles;
                knownCommands["purge-broken-files"] = Commands.PurgeBrokenFiles;

                knownCommands["compact"] = Commands.Compact;
                knownCommands["create-report"] = Commands.CreateBugReport;
                knownCommands["compare"] = Commands.ListChanges;
                knownCommands["test"] = Commands.Test;
                knownCommands["verify"] = Commands.Test;
                knownCommands["test-filters"] = Commands.TestFilters;
                knownCommands["test-filter"] = Commands.TestFilters;
                knownCommands["affected"] = Commands.Affected;

                knownCommands["system-info"] = Commands.SystemInfo;
                knownCommands["systeminfo"] = Commands.SystemInfo;

                knownCommands["send-mail"] = Commands.SendMail;

                return knownCommands;
            }
        }

        public static IEnumerable<string> SupportedCommands { get { return CommandMap.Keys; } }

        public static int RunCommandLine(TextWriter outwriter, TextWriter errwriter, Action<Library.Main.Controller> setup, string[] args)
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

                if (cargs.Count == 1 && string.Equals(cargs[0], "changelog", StringComparison.InvariantCultureIgnoreCase))
                {
                    var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "changelog.txt");
                    outwriter.WriteLine(System.IO.File.ReadAllText(path));
                    return 0;
                }

                foreach (string internaloption in Library.Main.Options.InternalOptions)
                    if (options.ContainsKey(internaloption))
                    {
                        outwriter.WriteLine(Strings.Program.InternalOptionUsedError(internaloption));
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
                    else if (options.ContainsKey("parameter-file") && !string.IsNullOrEmpty("parameter-file"))
                    {
                        filename = options["parameter-file"];
                        options.Remove("parameter-file");
                    }
                    else
                    {
                        filename = options["parameterfile"];
                        options.Remove("parameterfile");
                    }

                    if (!ReadOptionsFromFile(outwriter, filename, ref filter, cargs, options))
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

                var knownCommands = CommandMap;

                if (!isHelp && verbose)
                {
                    outwriter.WriteLine("Input command: {0}", command);

                    outwriter.WriteLine("Input arguments: ");
                    foreach (var a in cargs)
                        outwriter.WriteLine("\t{0}", a);
                    outwriter.WriteLine();

                    outwriter.WriteLine("Input options: ");
                    foreach (var n in options)
                        outwriter.WriteLine("{0}: {1}", n.Key, n.Value);
                    outwriter.WriteLine();
                }

                Duplicati.Library.Utility.TempFile.RemoveOldApplicationTempFiles((path, ex) =>
                {
                    if (verbose)
                        outwriter.WriteLine(string.Format("Failed to delete temp file: {0}", path));
                });

                var autoupdate = Library.Utility.Utility.ParseBoolOption(options, "auto-update");
                options.Remove("auto-update");

                if (knownCommands.ContainsKey(command))
                {
                    var res = knownCommands[command](outwriter, setup, cargs, options, filter);

                    if (autoupdate && FROM_COMMANDLINE)
                    {
                        var update = Library.AutoUpdater.UpdaterManager.LastUpdateCheckVersion;
                        if (update == null)
                            update = Library.AutoUpdater.UpdaterManager.CheckForUpdate();

                        if (update != null && update.Version != Library.AutoUpdater.UpdaterManager.SelfVersion.Version)
                        {
                            outwriter.WriteLine("Found update \"{0}\", downloading ...", update.Displayname);
                            long lastpg = 0;
                            Library.AutoUpdater.UpdaterManager.DownloadAndUnpackUpdate(update, f =>
                            {
                                var npg = (long)(f * 100);
                                if (Math.Abs(npg - lastpg) >= 5 || (npg == 100 && lastpg != 100))
                                {
                                    lastpg = npg;
                                    outwriter.WriteLine("Downloading {0}% ...", npg);
                                }
                            });
                            outwriter.WriteLine("Update \"{0}\" ({1}) installed, using on next launch", update.Displayname, update.Version);
                        }
                    }

                    return res;
                }
                else
                {
                    Commands.PrintInvalidCommand(outwriter, command, cargs);
                    return 200;
                }
            }
            catch (Exception ex)
            {
                Library.UsageReporter.Reporter.Report(ex);

                while (ex is System.Reflection.TargetInvocationException && ex.InnerException != null)
                    ex = ex.InnerException;

                if (ex is Duplicati.Library.Interface.UserInformationException && !verboseErrors)
                {
                    errwriter.WriteLine();
                    errwriter.WriteLine(ex.Message);
                }
                else if (!(ex is Library.Interface.CancelException))
                {
                    errwriter.WriteLine();
                    errwriter.WriteLine(ex.ToString());
                }
                else
                {
                    errwriter.WriteLine(Strings.Program.UnhandledException(ex.ToString()));

                    while (ex.InnerException != null)
                    {
                        ex = ex.InnerException;
                        errwriter.WriteLine();
                        errwriter.WriteLine(Strings.Program.UnhandledInnerException(ex.ToString()));
                    }
                }

                //Error = 100
                return 100;
            }
        }
            
        public static IList<Library.Interface.ICommandLineArgument> SupportedOptions
        {
            get
            {
                return new List<Library.Interface.ICommandLineArgument>(new Library.Interface.ICommandLineArgument[] {
                    new Library.Interface.CommandLineArgument("parameters-file", Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.ParametersFileOptionShort, Strings.Program.ParametersFileOptionLong2, "", new string[] {"parameter-file", "parameterfile"}),
                    new Library.Interface.CommandLineArgument("include", Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.IncludeShort, Strings.Program.IncludeLong),
                    new Library.Interface.CommandLineArgument("exclude", Library.Interface.CommandLineArgument.ArgumentType.String, Strings.Program.ExcludeShort, Strings.Program.ExcludeLong),
                    new Library.Interface.CommandLineArgument("control-files", Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.ControlFilesOptionShort, Strings.Program.ControlFilesOptionLong, "false"),
                    new Library.Interface.CommandLineArgument("quiet-console", Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.Program.QuietConsoleOptionShort, Strings.Program.QuietConsoleOptionLong, "false"),
                    new Library.Interface.CommandLineArgument("auto-update", Library.Interface.CommandLineArgument.ArgumentType.Boolean, LC.L("Toggle automatic updates"), LC.L("Set this option if you prefer to have the commandline version automatically update"), "false"),
                });
            }
        }

        private static bool ReadOptionsFromFile(TextWriter outwriter, string filename, ref Library.Utility.IFilter filter, List<string> cargs, Dictionary<string, string> options)
        {
            try
            {
                List<string> fargs = new List<string>(Library.Utility.Utility.ReadFileWithDefaultEncoding(Library.Utility.Utility.ExpandEnvironmentVariables(filename)).Replace("\r\n", "\n").Replace("\r", "\n").Split(new String[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
                var newsource = new List<string>();
                string newtarget = null;
                string prependfilter = null;
                string appendfilter = null;
                string replacefilter = null;

                var tmpparsed = Library.Utility.FilterCollector.ExtractOptions(fargs, (key, value) => {
                    if (key.Equals("source", StringComparison.OrdinalIgnoreCase))
                    {
                        newsource.Add(value);
                        return false;
                    }
                    else if (key.Equals("target", StringComparison.OrdinalIgnoreCase))
                    {
                        newtarget = value;
                        return false;
                    }
                    else if (key.Equals("append-filter", StringComparison.OrdinalIgnoreCase))
                    {
                        appendfilter = value;
                        return false;
                    }
                    else if (key.Equals("prepend-filter", StringComparison.OrdinalIgnoreCase))
                    {
                        prependfilter = value;
                        return false;
                    }
                    else if (key.Equals("replace-filter", StringComparison.OrdinalIgnoreCase))
                    {
                        replacefilter = value;
                        return false;
                    }

                    return true;
                });
                
                var opt = tmpparsed.Item1;
                var newfilter = tmpparsed.Item2;

                // If the user specifies parameters-file, all filters must be in the file.
                // Allowing to specify some filters on the command line could result in wrong filter ordering
                if (!filter.Empty && !newfilter.Empty)
                    throw new Duplicati.Library.Interface.UserInformationException(Strings.Program.FiltersCannotBeUsedWithFileError2);

                if (!newfilter.Empty)
                    filter = newfilter;

                if (!string.IsNullOrWhiteSpace(prependfilter))
                    filter = Library.Utility.FilterExpression.Combine(Library.Utility.FilterExpression.Deserialize(prependfilter.Split(new string[] {System.IO.Path.PathSeparator.ToString()}, StringSplitOptions.RemoveEmptyEntries)), filter);

                if (!string.IsNullOrWhiteSpace(appendfilter))
                    filter = Library.Utility.FilterExpression.Combine(filter, Library.Utility.FilterExpression.Deserialize(appendfilter.Split(new string[] {System.IO.Path.PathSeparator.ToString()}, StringSplitOptions.RemoveEmptyEntries)));

                if (!string.IsNullOrWhiteSpace(replacefilter))
                    filter = Library.Utility.FilterExpression.Deserialize(replacefilter.Split(new string[] {System.IO.Path.PathSeparator.ToString()}, StringSplitOptions.RemoveEmptyEntries));

                foreach (KeyValuePair<String, String> keyvalue in opt)
                    options[keyvalue.Key] = keyvalue.Value;
                    
                if (!string.IsNullOrEmpty(newtarget))
                   {
                       if (cargs.Count <= 1)
                           cargs.Add(newtarget);
                       else
                           cargs[1] = newtarget;
                   }

                if (cargs.Count >= 1 && cargs[0].Equals("backup", StringComparison.InvariantCultureIgnoreCase))
                       cargs.AddRange(newsource);
                else if (newsource.Count > 0 && Library.Utility.Utility.ParseBoolOption(options, "verbose"))
                    outwriter.WriteLine(Strings.Program.SkippingSourceArgumentsOnNonBackupOperation);

                return true;
            }
            catch (Exception e)
            {
                outwriter.WriteLine(Strings.Program.FailedToParseParametersFileError(filename, e.Message));
                return false;
            }
        }
    }
}
