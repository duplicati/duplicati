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
using System.Linq;
using System.Collections.Generic;

namespace Duplicati.CommandLine.RecoveryTool
{
    public static class Program
    {
        private delegate int CommandRunner(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter);

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static int Main(string[] _args)
        {
            try
            {
                Library.AutoUpdater.PreloadSettingsLoader.ConfigurePreloadSettings(ref _args, Library.AutoUpdater.PackageHelper.NamedExecutable.RecoveryTool);

                var args = new List<string>(_args);
                var tmpparsed = Library.Utility.FilterCollector.ExtractOptions(args);
                var options = tmpparsed.Item1;
                var filter = tmpparsed.Item2;

                if (!options.ContainsKey("auth_password") && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_PASSWORD")))
                    options["auth_password"] = System.Environment.GetEnvironmentVariable("AUTH_PASSWORD");

                if (!options.ContainsKey("auth_username") && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_USERNAME")))
                    options["auth_username"] = System.Environment.GetEnvironmentVariable("AUTH_USERNAME");

                if (options.ContainsKey("tempdir") && !string.IsNullOrEmpty(options["tempdir"]))
                    Library.Utility.TempFolder.SystemTempPath = options["tempdir"];

                bool isHelp = args.Count == 0 || (args.Count >= 1 && string.Equals(args[0], "help", StringComparison.OrdinalIgnoreCase));
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

                var actions = new Dictionary<string, CommandRunner>(StringComparer.OrdinalIgnoreCase);
                actions["download"] = Download.Run;
                actions["recompress"] = Recompress.Run;
                actions["list"] = List.Run;
                actions["restore"] = Restore.Run;
                actions["help"] = Help.Run;

                if (Library.Utility.Utility.ParseBoolOption(options, "build-index-with-files"))
                {
                    actions["index"] = FileIndex.Run;
                }
                else
                {
                    actions["index"] = Index.Run;
                }

                CommandRunner command;

                actions.TryGetValue(args.FirstOrDefault() ?? "", out command);

                command = command ?? actions["help"];

                return command(args, options, filter);
            }
            catch (Exception ex)
            {
                if (ex is Duplicati.Library.Interface.UserInformationException)
                    Console.WriteLine(ex.Message);
                else
                    Console.WriteLine("Program crashed: {0}{1}", Environment.NewLine, ex);
                return 200;
            }
        }

        private static bool ReadOptionsFromFile(string filename, ref Library.Utility.IFilter filter, List<string> cargs, Dictionary<string, string> options)
        {
            try
            {
                List<string> fargs = new List<string>(Library.Utility.Utility.ReadFileWithDefaultEncoding(Environment.ExpandEnvironmentVariables(filename)).Replace("\r\n", "\n").Replace("\r", "\n").Split(new String[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
                var tmpparsed = Library.Utility.FilterCollector.ExtractOptions(fargs);

                var opt = tmpparsed.Item1;
                var newfilter = tmpparsed.Item2;

                // If the user specifies parameters-file, all filters must be in the file.
                // Allowing to specify some filters on the command line could result in wrong filter ordering
                if (!filter.Empty && !newfilter.Empty)
                    throw new Duplicati.Library.Interface.UserInformationException("Filters cannot be specified on the commandline if filters are also present in the parameter file", "RecoveryToolFiltersOnCommandLineAndInParameterFile");

                if (!newfilter.Empty)
                    filter = newfilter;

                foreach (KeyValuePair<String, String> keyvalue in opt)
                    options[keyvalue.Key] = keyvalue.Value;

                cargs.AddRange(
                    from c in fargs
                    where !string.IsNullOrWhiteSpace(c) && !c.StartsWith("#", StringComparison.Ordinal) && !c.StartsWith("!", StringComparison.Ordinal) && !c.StartsWith("REM ", StringComparison.OrdinalIgnoreCase)
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
