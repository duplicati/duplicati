//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Linq;
using System.Collections.Generic;

namespace Duplicati.CommandLine.RecoveryTool
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            Duplicati.Library.AutoUpdater.UpdaterManager.IgnoreWebrootFolder = true;
            return Duplicati.Library.AutoUpdater.UpdaterManager.RunFromMostRecent(typeof(Program).GetMethod("RealMain"), args);
        }

        private delegate int CommandRunner(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter);

        public static int RealMain(string[] _args)
        {
            try
            {
                var args = new List<string>(_args);
                var tmpparsed = Library.Utility.FilterCollector.ExtractOptions(args);
                var options = tmpparsed.Item1;
                var filter = tmpparsed.Item2;

                if (!options.ContainsKey("auth_password") && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_PASSWORD")))
                    options["auth_password"] = System.Environment.GetEnvironmentVariable("AUTH_PASSWORD");

                if (!options.ContainsKey("auth_username") && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_USERNAME")))
                    options["auth_username"] = System.Environment.GetEnvironmentVariable("AUTH_USERNAME");

                if (options.ContainsKey("tempdir") && !string.IsNullOrEmpty(options["tempdir"]))
                    Library.Utility.TempFolder.SetSystemTempPath(options["tempdir"]);

                bool isHelp = args.Count == 0 || (args.Count >= 1 && string.Equals(args[0], "help", StringComparison.InvariantCultureIgnoreCase));
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

                    if (!ReadOptionsFromFile(filename, ref filter, args, options))
                        return 100;
                }

                var actions = new Dictionary<string, CommandRunner>(StringComparer.InvariantCultureIgnoreCase);
                actions["download"] = Download.Run;
                actions["recompress"] = Recompress.Run;
                actions["index"] = Index.Run;
                actions["list"] = List.Run;
                actions["restore"] = Restore.Run;
                actions["help"] = Help.Run;

                CommandRunner command;

                actions.TryGetValue(args.FirstOrDefault() ?? "", out command);

                command = command ?? actions["help"];

                return command(args, options, filter);
            }
            catch(Exception ex)
            {
                if (ex is Duplicati.Library.Interface.UserInformationException)
                    Console.WriteLine(ex.Message);
                else
                    Console.WriteLine("Program crashed: {0}{1}", Environment.NewLine, ex.ToString());
                return 200;
            }
        }

        private static bool ReadOptionsFromFile(string filename, ref Library.Utility.IFilter filter, List<string> cargs, Dictionary<string, string> options)
        {
            try
            {
                List<string> fargs = new List<string>(Library.Utility.Utility.ReadFileWithDefaultEncoding(Library.Utility.Utility.ExpandEnvironmentVariables(filename)).Replace("\r\n", "\n").Replace("\r", "\n").Split(new String[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
                var tmpparsed = Library.Utility.FilterCollector.ExtractOptions(fargs);

                var opt = tmpparsed.Item1;
                var newfilter = tmpparsed.Item2;

                // If the user specifies parameters-file, all filters must be in the file.
                // Allowing to specify some filters on the command line could result in wrong filter ordering
                if (!filter.Empty && !newfilter.Empty)
                    throw new Duplicati.Library.Interface.UserInformationException("Filters cannot be specified on the commandline if filters are also present in the parameter file");

                if (!newfilter.Empty)
                    filter = newfilter;

                foreach(KeyValuePair<String, String> keyvalue in opt)
                    options[keyvalue.Key] = keyvalue.Value;

                cargs.AddRange(
                    from c in fargs
                    where !string.IsNullOrWhiteSpace(c) && !c.StartsWith("#") && !c.StartsWith("!") && !c.StartsWith("REM ", StringComparison.InvariantCultureIgnoreCase)
                    select c
                );
                    
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(@"Unable to read the parameters file ""{0}"", reason: {1}", filename, e.Message);
                return false;
            }
        }
    }
}
