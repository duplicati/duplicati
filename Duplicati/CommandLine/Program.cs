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
using System.Linq;
using Duplicati.Library.Localization.Short;
using System.IO;
using Duplicati.Library.Logging;

namespace Duplicati.CommandLine
{
    public class Program
    {
        private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<Program>();

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
                var knownCommands =
                    new Dictionary<string, Func<TextWriter, Action<Library.Main.Controller>, List<string>,
                        Dictionary<string, string>, Library.Utility.IFilter, int>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["help"] = Commands.Help,
                        ["example"] = Commands.Examples,
                        ["examples"] = Commands.Examples,
                        ["find"] = Commands.List,
                        ["list"] = Commands.List,
                        ["delete"] = Commands.Delete,
                        ["backup"] = Commands.Backup,
                        ["restore"] = Commands.Restore,
                        ["repair"] = Commands.Repair,
                        ["purge"] = Commands.PurgeFiles,
                        ["list-broken-files"] = Commands.ListBrokenFiles,
                        ["purge-broken-files"] = Commands.PurgeBrokenFiles,
                        ["compact"] = Commands.Compact,
                        ["create-report"] = Commands.CreateBugReport,
                        ["compare"] = Commands.ListChanges,
                        ["test"] = Commands.Test,
                        ["verify"] = Commands.Test,
                        ["test-filters"] = Commands.TestFilters,
                        ["test-filter"] = Commands.TestFilters,
                        ["affected"] = Commands.Affected,
                        ["vacuum"] = Commands.Vacuum,
                        ["system-info"] = Commands.SystemInfo,
                        ["systeminfo"] = Commands.SystemInfo,
                        ["send-mail"] = Commands.SendMail
                    };
                
                return knownCommands;
            }
        }

        public static IEnumerable<string> SupportedCommands { get { return CommandMap.Keys; } }

        private static int ShowChangeLog(TextWriter outwriter)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "changelog.txt");
            outwriter.WriteLine(System.IO.File.ReadAllText(path));
            return 0;
        }

        private static void CheckForUpdates(TextWriter outwriter)
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

        private static int ParseCommandLine(TextWriter outwriter, Action<Library.Main.Controller> setup, ref bool verboseErrors, string[] args)
        {
            List<string> cargs = new List<string>(args);

            var tmpparsed = Library.Utility.FilterCollector.ExtractOptions(cargs);
            var options = tmpparsed.Item1;
            var filter = tmpparsed.Item2;

            verboseErrors = Library.Utility.Utility.ParseBoolOption(options, "debug-output");

            if (cargs.Count == 1 && string.Equals(cargs[0], "changelog", StringComparison.OrdinalIgnoreCase))
            {
                return ShowChangeLog(outwriter);
            }

            foreach (string internaloption in Library.Main.Options.InternalOptions)
                if (options.ContainsKey(internaloption))
                {
                    outwriter.WriteLine(Strings.Program.InternalOptionUsedError(internaloption));
                    return 200;
                }

            // Probe for "help" to avoid extra processing
            if (cargs.Count == 0 || (string.Equals(cargs[0], "help", StringComparison.OrdinalIgnoreCase)))
            {
                return Commands.Help(outwriter, setup, cargs, options, filter);
            }

            // try and parse all parameter file aliases
            foreach (string parameterOption in new[] { "parameters-file", "parameters-file", "parameterfile" })
            {
                if (options.ContainsKey(parameterOption) && !string.IsNullOrEmpty(options[parameterOption]))
                {
                    string filename = options[parameterOption];
                    options.Remove(parameterOption);
                    if (!ReadOptionsFromFile(outwriter, filename, ref filter, cargs, options))
                        return 100;
                    break;
                }
            }

            if (!options.ContainsKey("passphrase"))
                if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PASSPHRASE")))
                    options["passphrase"] = System.Environment.GetEnvironmentVariable("PASSPHRASE");

            if (!options.ContainsKey("auth-password"))
                if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_PASSWORD")))
                    options["auth-password"] = System.Environment.GetEnvironmentVariable("AUTH_PASSWORD");

            if (!options.ContainsKey("auth-username"))
                if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_USERNAME")))
                    options["auth-username"] = System.Environment.GetEnvironmentVariable("AUTH_USERNAME");

            var showDeletionErrors = verboseErrors;
            Duplicati.Library.Utility.TempFile.RemoveOldApplicationTempFiles((path, ex) =>
            {
                if (showDeletionErrors)
                    outwriter.WriteLine(string.Format("Failed to delete temp file: {0}", path));
            });

            string command = cargs[0];
            cargs.RemoveAt(0);

            if (verboseErrors)
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


            if (CommandMap.ContainsKey(command))
            {
                var autoupdate = Library.Utility.Utility.ParseBoolOption(options, "auto-update");
                options.Remove("auto-update");

                var res = CommandMap[command](outwriter, setup, cargs, options, filter);

                if (autoupdate && FROM_COMMANDLINE)
                {
                    CheckForUpdates(outwriter);
                }

                return res;
            }
            else
            {
                Commands.PrintInvalidCommand(outwriter, command);
                return 200;
            }
        }

        public static int RunCommandLine(TextWriter outwriter, TextWriter errwriter, Action<Library.Main.Controller> setup, string[] args)
        {
            bool verboseErrors = false;
            try
            {
                return ParseCommandLine(outwriter, setup, ref verboseErrors, args);
            }
            catch (Exception ex)
            {
                Library.UsageReporter.Reporter.Report(ex);

                while (ex is System.Reflection.TargetInvocationException && ex.InnerException != null)
                    ex = ex.InnerException;

                if (ex is Duplicati.Library.Interface.UserInformationException && !verboseErrors)
                {
                    errwriter.WriteLine();
                    errwriter.WriteLine("ErrorID: {0}", ((Duplicati.Library.Interface.UserInformationException)ex).HelpID);
                    errwriter.WriteLine(ex.Message);
                }
                else if (!(ex is Library.Interface.CancelException))
                {
                    errwriter.WriteLine();
                    errwriter.WriteLine(ex);
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
                List<string> fargs = new List<string>(Library.Utility.Utility.ReadFileWithDefaultEncoding(Environment.ExpandEnvironmentVariables(filename)).Replace("\r\n", "\n").Replace("\r", "\n").Split(new String[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
                var newsource = new List<string>();
                string newtarget = null;
                string prependfilter = null;
                string appendfilter = null;
                string replacefilter = null;

                var tmpparsed = Library.Utility.FilterCollector.ExtractOptions(fargs, (key, value) =>
                {
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
                    throw new Duplicati.Library.Interface.UserInformationException(Strings.Program.FiltersCannotBeUsedWithFileError2, "FiltersCannotBeUsedOnCommandLineAndInParameterFile");

                if (!newfilter.Empty)
                    filter = newfilter;

                if (!string.IsNullOrWhiteSpace(prependfilter))
                    filter = Library.Utility.FilterExpression.Combine(Library.Utility.FilterExpression.Deserialize(prependfilter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries)), filter);

                if (!string.IsNullOrWhiteSpace(appendfilter))
                    filter = Library.Utility.FilterExpression.Combine(filter, Library.Utility.FilterExpression.Deserialize(appendfilter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries)));

                if (!string.IsNullOrWhiteSpace(replacefilter))
                    filter = Library.Utility.FilterExpression.Deserialize(replacefilter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries));

                foreach (KeyValuePair<String, String> keyvalue in opt)
                    options[keyvalue.Key] = keyvalue.Value;

                if (!string.IsNullOrEmpty(newtarget))
                {
                    if (cargs.Count <= 1)
                        cargs.Add(newtarget);
                    else
                        cargs[1] = newtarget;
                }

                if (cargs.Count >= 1 && cargs[0].Equals("backup", StringComparison.OrdinalIgnoreCase))
                    cargs.AddRange(newsource);
                else if (newsource.Count > 0)
                    Library.Logging.Log.WriteVerboseMessage(LOGTAG, "NotUsingBackupSources", Strings.Program.SkippingSourceArgumentsOnNonBackupOperation);

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
