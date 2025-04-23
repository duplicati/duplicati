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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.DynamicLoader;
using FilterGroup = Duplicati.Library.Utility.FilterGroup;

namespace Duplicati.CommandLine
{
    public static class Help
    {
        private static readonly Dictionary<string, string> _document;
        private const string RESOURCE_NAME = "help.txt";
        private static readonly System.Text.RegularExpressions.Regex NAMEDOPTION_REGEX = new System.Text.RegularExpressions.Regex("\\%OPTION\\:(?<name>[^\\%]+)\\%");

        static Help()
        {
            _document = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var sr = new StreamReader(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(Help), RESOURCE_NAME)))
            {
                List<string> keywords = new List<string>();
                StringBuilder sb = new StringBuilder();
                foreach (var line in sr.ReadToEnd().Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
                {
                    if (line.Trim().StartsWith("#", StringComparison.Ordinal))
                        continue;

                    if (line.Trim().StartsWith(">", StringComparison.Ordinal))
                    {
                        if (sb.Length > 0)
                        {
                            string s = sb.ToString();
                            foreach (var k in keywords)
                                _document[k] = s;

                            keywords.Clear();
                            sb.Clear();
                        }

                        string[] elems = line.Split(new string[] { " ", "\t" }, StringSplitOptions.RemoveEmptyEntries);
                        if (elems.Length >= 2 && string.Equals(elems[elems.Length - 2], "help", StringComparison.OrdinalIgnoreCase))
                            keywords.Add(elems[elems.Length - 1]);
                        else if (elems.Length == 3 && string.Equals(elems[elems.Length - 1], "help", StringComparison.OrdinalIgnoreCase))
                            keywords.Add("help");
                    }
                    else
                    {
                        sb.AppendLine(line);
                    }
                }

                if (sb.Length > 0)
                {
                    string s = sb.ToString();
                    foreach (var k in keywords)
                        _document[k] = s;
                }
            }
        }

        public static void PrintUsage(TextWriter outwriter, string topic, IDictionary<string, string> options)
        {
            try
            {
                //Force translation off if this is from the commandline
                if (Program.FROM_COMMANDLINE)
                {
                    CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                    CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
                }
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(topic))
                topic = "help";

            if (string.Equals("help", topic, StringComparison.OrdinalIgnoreCase))
            {
                if (options.Count == 1)
                    topic = new List<string>(options.Keys)[0];
                else if (System.Environment.CommandLine.IndexOf("--exclude", StringComparison.OrdinalIgnoreCase) >= 0)
                    topic = "exclude";
                else if (System.Environment.CommandLine.IndexOf("--include", StringComparison.OrdinalIgnoreCase) >= 0)
                    topic = "include";
            }

            if (_document.ContainsKey(topic))
            {
                string tp = _document[topic];
                Library.Main.Options opts = new Library.Main.Options(new Dictionary<string, string>());

                tp = tp.Replace("%CLI_EXE%", PackageHelper.GetExecutableName(PackageHelper.NamedExecutable.CommandLine));
                tp = tp.Replace("%VERSION%", License.VersionNumbers.VERSION_NAME);
                tp = tp.Replace("%BACKENDS%", string.Join(", ", Library.DynamicLoader.BackendLoader.Keys));
                tp = tp.Replace("%APP_PATH%", Path.Combine(UpdaterManager.INSTALLATIONDIR, PackageHelper.GetExecutableName(PackageHelper.NamedExecutable.CommandLine)));
                tp = tp.Replace("%PATH_SEPARATOR%", System.IO.Path.PathSeparator.ToString());
                tp = tp.Replace("%EXAMPLE_SOURCE_PATH%", !OperatingSystem.IsWindows() ? "/source" : @"D:\source");
                tp = tp.Replace("%EXAMPLE_SOURCE_FILE%", !OperatingSystem.IsWindows() ? "/source/myfile.txt" : @"D:\source\file.txt");
                tp = tp.Replace("%EXAMPLE_RESTORE_PATH%", !OperatingSystem.IsWindows() ? "/restore" : @"D:\restore");
                tp = tp.Replace("%ENCRYPTIONMODULES%", string.Join(", ", Library.DynamicLoader.EncryptionLoader.Keys));
                tp = tp.Replace("%COMPRESSIONMODULES%", string.Join(", ", Library.DynamicLoader.CompressionLoader.Keys));
                tp = tp.Replace("%DEFAULTENCRYPTIONMODULE%", opts.EncryptionModule);
                tp = tp.Replace("%DEFAULTCOMPRESSIONMODULE%", opts.CompressionModule);
                tp = tp.Replace("%GENERICMODULES%", string.Join(", ", Library.DynamicLoader.GenericLoader.Keys));
                var metaGroupNames = new[] { nameof(FilterGroup.None), nameof(FilterGroup.DefaultExcludes), nameof(FilterGroup.DefaultIncludes), };
                tp = tp.Replace("%FILTER_GROUPS_SHORT%", string.Join(Environment.NewLine + "  ", metaGroupNames.Concat(Enum.GetNames(typeof(FilterGroup)).Except(metaGroupNames, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)).Select(group => "{" + group + "}")));
                tp = tp.Replace("%FILTER_GROUPS_LONG%", Library.Utility.FilterGroups.GetOptionDescriptions(4, true));

                if (OperatingSystem.IsWindows())
                {
                    // These properties are only valid for Windows
                    tp = tp.Replace("%EXAMPLE_WILDCARD_DRIVE_SOURCE_PATH%", @"*:\source");
                    tp = tp.Replace("%EXAMPLE_VOLUME_GUID_SOURCE_PATH%", @"\\?\Volume{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}\source");
                    tp = tp.Replace("%KNOWN_DRIVES_AND_VOLUMES%", string.Join(Environment.NewLine + "    ", Library.Utility.Utility.GetVolumeGuidsAndDriveLetters().Select(pair => string.Format("{0}  {1}", pair.Key, pair.Value))));

                    // We don't need to hide things between these tags on Windows
                    tp = tp.Replace("%IF_WINDOWS%", string.Empty);
                    tp = tp.Replace("%END_IF_WINDOWS%", string.Empty);
                }
                else
                {
                    // Specifying the Singleline option allows . to match newlines, so this will detect spans that cover multiple lines
                    tp = System.Text.RegularExpressions.Regex.Replace(tp, @"\%IF_WINDOWS\%.*\%END_IF_WINDOWS\%", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
                }

                if (tp.Contains("%MAINOPTIONS%"))
                {
                    List<string> lines = new List<string>();
                    SortedList<string, Library.Interface.ICommandLineArgument> sorted = new SortedList<string, Library.Interface.ICommandLineArgument>();
                    foreach (Library.Interface.ICommandLineArgument arg in opts.SupportedCommands)
                        sorted.Add(arg.Name, arg);

                    foreach (Library.Interface.ICommandLineArgument arg in Program.SupportedOptions)
                        sorted[arg.Name] = arg;

                    foreach (Library.Interface.ICommandLineArgument arg in sorted.Values)
                        lines.Add(PrintArgSimple(arg, arg.Name));

                    tp = tp.Replace("%MAINOPTIONS%", string.Join(Environment.NewLine, lines.ToArray()));
                }

                if (tp.Contains("%ALLOPTIONS%"))
                {
                    List<string> lines = new List<string>();
                    foreach (Library.Interface.ICommandLineArgument arg in opts.SupportedCommands)
                        Library.Interface.CommandLineArgument.PrintArgument(lines, arg, "  ");


                    foreach (Library.Interface.ICommandLineArgument arg in Program.SupportedOptions)
                        Library.Interface.CommandLineArgument.PrintArgument(lines, arg, "  ");

                    lines.Add("");
                    lines.Add("");
                    lines.Add(Strings.Program.SupportedBackendsHeader);
                    foreach (Duplicati.Library.Interface.IBackend back in Library.DynamicLoader.BackendLoader.Backends)
                        PrintBackend(back, lines);

                    lines.Add("");
                    lines.Add("");
                    lines.Add(Strings.Program.SupportedEncryptionModulesHeader);
                    foreach (Duplicati.Library.Interface.IEncryption mod in Library.DynamicLoader.EncryptionLoader.Modules)
                        PrintEncryptionModule(mod, lines);

                    lines.Add("");
                    lines.Add("");
                    lines.Add(Strings.Program.SupportedCompressionModulesHeader);
                    foreach (Duplicati.Library.Interface.ICompression mod in Library.DynamicLoader.CompressionLoader.Modules)
                        PrintCompressionModule(mod, lines);

                    lines.Add("");

                    lines.Add("");
                    lines.Add("");
                    lines.Add(Strings.Program.GenericModulesHeader);
                    foreach (Duplicati.Library.Interface.IGenericModule mod in Library.DynamicLoader.GenericLoader.Modules)
                        PrintGenericModule(mod, lines);

                    lines.Add("");

                    tp = tp.Replace("%ALLOPTIONS%", string.Join(Environment.NewLine, lines.ToArray()));
                }

                if (tp.Contains("%MODULEOPTIONS%"))
                {
                    //Figure out which module we are in
                    IList<Library.Interface.ICommandLineArgument> args = null;
                    bool found = false;
                    foreach (Duplicati.Library.Interface.IBackend backend in Library.DynamicLoader.BackendLoader.Backends)
                        if (string.Equals(backend.ProtocolKey, topic, StringComparison.OrdinalIgnoreCase))
                        {
                            args = backend.SupportedCommands;
                            found = true;
                            break;
                        }

                    if (args == null)
                        foreach (Duplicati.Library.Interface.IEncryption module in Library.DynamicLoader.EncryptionLoader.Modules)
                            if (string.Equals(module.FilenameExtension, topic, StringComparison.OrdinalIgnoreCase))
                            {
                                args = module.SupportedCommands;
                                found = true;
                                break;
                            }

                    if (args == null)
                        foreach (Duplicati.Library.Interface.ICompression module in Library.DynamicLoader.CompressionLoader.Modules)
                            if (string.Equals(module.FilenameExtension, topic, StringComparison.OrdinalIgnoreCase))
                            {
                                args = module.SupportedCommands;
                                found = true;
                                break;
                            }

                    if (args == null)
                        foreach (Duplicati.Library.Interface.IGenericModule module in Library.DynamicLoader.GenericLoader.Modules)
                            if (string.Equals(module.Key, topic, StringComparison.OrdinalIgnoreCase))
                            {
                                args = module.SupportedCommands;
                                found = true;
                                break;
                            }

                    //If the module is not found, we do not display the description
                    if (found)
                        tp = tp.Replace("%MODULEOPTIONS%", PrintArgsSimple(args));
                    else
                    {
                        outwriter.WriteLine("Topic not found: {0}", topic);
                        outwriter.WriteLine();
                        //Prevent recursive lookups
                        if (topic != "help")
                            PrintUsage(outwriter, "help", new Dictionary<string, string>());
                        return;
                    }
                }

                if (tp.Contains("%SECRETPROVIDERS%"))
                {
                    var lines = new List<string>();
                    foreach (var module in SecretProviderLoader.Keys)
                    {
                        var metadata = SecretProviderLoader.GetProviderMetadata(module);
                        lines.Add($"- {module}: {metadata.DisplayName}");
                    }

                    tp = tp.Replace("%SECRETPROVIDERS%", string.Join(Environment.NewLine, lines.ToArray()));
                }

                if (NAMEDOPTION_REGEX.IsMatch(tp))
                    tp = NAMEDOPTION_REGEX.Replace(tp, new Matcher().MathEvaluator);

                PrintFormatted(outwriter, tp.Split(new string[] { Environment.NewLine }, StringSplitOptions.None));
            }
            else
            {
                List<string> lines = new List<string>();

                foreach (Duplicati.Library.Interface.IBackend backend in Library.DynamicLoader.BackendLoader.Backends)
                    if (string.Equals(backend.ProtocolKey, topic, StringComparison.OrdinalIgnoreCase))
                    {
                        PrintBackend(backend, lines);
                        break;
                    }

                if (lines.Count == 0)
                    foreach (Duplicati.Library.Interface.IEncryption mod in Library.DynamicLoader.EncryptionLoader.Modules)
                        if (string.Equals(mod.FilenameExtension, topic, StringComparison.OrdinalIgnoreCase))
                        {
                            PrintEncryptionModule(mod, lines);
                            break;
                        }

                if (lines.Count == 0)
                    foreach (Duplicati.Library.Interface.ICompression mod in Library.DynamicLoader.CompressionLoader.Modules)
                        if (string.Equals(mod.FilenameExtension, topic, StringComparison.OrdinalIgnoreCase))
                        {
                            PrintCompressionModule(mod, lines);
                            break;
                        }

                if (lines.Count == 0)
                    foreach (Duplicati.Library.Interface.IGenericModule mod in Library.DynamicLoader.GenericLoader.Modules)
                        if (string.Equals(mod.Key, topic, StringComparison.OrdinalIgnoreCase))
                        {
                            PrintGenericModule(mod, lines);
                            break;
                        }

                if (lines.Count == 0)
                    foreach (Duplicati.Library.Interface.ISecretProvider mod in SecretProviderLoader.Modules)
                        if (string.Equals(mod.Key, topic, StringComparison.OrdinalIgnoreCase))
                        {
                            PrintSecretProvider(mod, lines);
                            break;
                        }


                if (lines.Count == 0)
                    PrintArgumentIfFound(new Matcher().Values, topic, lines);

                if (lines.Count != 0)
                {
                    PrintFormatted(outwriter, lines);
                }
                else
                {
                    outwriter.WriteLine("Topic not found: {0}", topic);
                    outwriter.WriteLine();
                    PrintUsage(outwriter, "help", new Dictionary<string, string>());
                }
            }
        }

        private static void PrintArgumentIfFound(IEnumerable<Duplicati.Library.Interface.ICommandLineArgument> args, string topic, List<string> lines)
        {
            if (args == null)
                return;

            foreach (Duplicati.Library.Interface.ICommandLineArgument arg in args)
            {
                if (string.Equals(arg.Name, topic, StringComparison.OrdinalIgnoreCase))
                {
                    Library.Interface.CommandLineArgument.PrintArgument(lines, arg, "  ");
                    return;
                }

                if (arg.Aliases != null)
                    foreach (string k in arg.Aliases)
                    {
                        if (string.Equals(k, topic, StringComparison.OrdinalIgnoreCase))
                        {
                            Library.Interface.CommandLineArgument.PrintArgument(lines, arg, "  ");
                            return;
                        }
                    }
            }
        }

        private static void PrintBackend(Duplicati.Library.Interface.IBackend back, List<string> lines)
        {
            lines.Add(back.DisplayName + " (" + back.ProtocolKey + "):");
            lines.Add(" " + back.Description);
            if (back.SupportedCommands != null && back.SupportedCommands.Count > 0)
            {
                lines.Add(" " + Strings.Program.SupportedOptionsHeader);
                foreach (Library.Interface.ICommandLineArgument arg in back.SupportedCommands)
                    Library.Interface.CommandLineArgument.PrintArgument(lines, arg, "  ");
            }
            lines.Add("");
        }

        private static void PrintEncryptionModule(Duplicati.Library.Interface.IEncryption mod, List<string> lines)
        {
            lines.Add(mod.DisplayName + " (." + mod.FilenameExtension + "):");
            lines.Add(" " + mod.Description);
            if (mod.SupportedCommands != null && mod.SupportedCommands.Count > 0)
            {
                lines.Add(" " + Strings.Program.SupportedOptionsHeader);
                foreach (Library.Interface.ICommandLineArgument arg in mod.SupportedCommands)
                    Library.Interface.CommandLineArgument.PrintArgument(lines, arg, "  ");
            }
            lines.Add("");
        }

        private static void PrintCompressionModule(Duplicati.Library.Interface.ICompression mod, List<string> lines)
        {
            lines.Add(mod.DisplayName + " (." + mod.FilenameExtension + "):");
            lines.Add(" " + mod.Description);
            if (mod.SupportedCommands != null && mod.SupportedCommands.Count > 0)
            {
                lines.Add(" " + Strings.Program.SupportedOptionsHeader);
                foreach (Library.Interface.ICommandLineArgument arg in mod.SupportedCommands)
                    Library.Interface.CommandLineArgument.PrintArgument(lines, arg, "  ");
            }
            lines.Add("");
        }

        private static void PrintGenericModule(Duplicati.Library.Interface.IGenericModule mod, List<string> lines)
        {
            lines.Add(mod.DisplayName + " (" + mod.Key + "):");
            lines.Add(" " + mod.Description);
            lines.Add(" " + (mod.LoadAsDefault ? Strings.Program.ModuleIsLoadedAutomatically : Strings.Program.ModuleIsNotLoadedAutomatically));
            if (mod.SupportedCommands != null && mod.SupportedCommands.Count > 0)
            {
                lines.Add(" " + Strings.Program.SupportedOptionsHeader);
                foreach (Library.Interface.ICommandLineArgument arg in mod.SupportedCommands)
                    Library.Interface.CommandLineArgument.PrintArgument(lines, arg, "  ");
            }
            lines.Add("");
        }

        private static void PrintSecretProvider(Duplicati.Library.Interface.ISecretProvider mod, List<string> lines)
        {
            lines.Add($"{mod.Key}: {mod.Description}");
            if (mod.SupportedCommands != null && mod.SupportedCommands.Count > 0)
            {
                lines.Add(" " + Strings.Program.SupportedOptionsHeader);
                foreach (Library.Interface.ICommandLineArgument arg in mod.SupportedCommands)
                    Library.Interface.CommandLineArgument.PrintArgument(lines, arg, "  ");
            }
            lines.Add("");
        }

        private static void PrintFormatted(TextWriter outwriter, IEnumerable<string> lines)
        {
            int windowWidth = 80;

            try
            {
                // This can go wrong if we have no attached console
                if (outwriter == Console.Out)
                    windowWidth = Math.Max(12, Console.WindowWidth == 0 ? 80 : Console.WindowWidth);
            }
            catch { }

            foreach (string s in lines)
            {
                if (string.IsNullOrEmpty(s) || s.Trim().Length == 0)
                {
                    outwriter.WriteLine();
                    continue;
                }

                string c = s;

                StringBuilder leadingSpaces = new StringBuilder();
                while (c.Length > 0 && c.StartsWith(" ", StringComparison.Ordinal))
                {
                    leadingSpaces.Append(" ");
                    c = c.Remove(0, 1);
                }

                bool extraIndent = c.StartsWith("--", StringComparison.Ordinal);

                while (c.Length > 0)
                {
                    int len = Math.Min(windowWidth - 2, leadingSpaces.Length + c.Length);
                    len -= leadingSpaces.Length;
                    if (len < c.Length)
                    {
                        int ix = c.LastIndexOf(' ', len);
                        if (ix > 0)
                            len = ix;
                    }

                    var lfix = c.IndexOf('\n');
                    if (lfix >= 0 && lfix < len)
                        len = lfix + 1;

                    outwriter.WriteLine(leadingSpaces + c.Substring(0, len).Trim());
                    c = c.Remove(0, len);
                    if (extraIndent)
                    {
                        extraIndent = false;
                        leadingSpaces.Append("  ");
                    }
                }
            }
        }

        private class Matcher
        {
            readonly Dictionary<string, Library.Interface.ICommandLineArgument> args = new Dictionary<string, Library.Interface.ICommandLineArgument>(StringComparer.OrdinalIgnoreCase);

            public Matcher()
            {
                List<IList<Library.Interface.ICommandLineArgument>> foundArgs = new List<IList<Library.Interface.ICommandLineArgument>>
                {
                    new Library.Main.Options(new Dictionary<string, string>()).SupportedCommands,
                    Program.SupportedOptions
                };

                foreach (Duplicati.Library.Interface.IBackend backend in Library.DynamicLoader.BackendLoader.Backends)
                    if (backend.SupportedCommands != null)
                        foundArgs.Add(backend.SupportedCommands);
                foreach (Duplicati.Library.Interface.IEncryption mod in Library.DynamicLoader.EncryptionLoader.Modules)
                    if (mod.SupportedCommands != null)
                        foundArgs.Add(mod.SupportedCommands);
                foreach (Duplicati.Library.Interface.ICompression mod in Library.DynamicLoader.CompressionLoader.Modules)
                    if (mod.SupportedCommands != null)
                        foundArgs.Add(mod.SupportedCommands);
                foreach (Duplicati.Library.Interface.IGenericModule mod in Library.DynamicLoader.GenericLoader.Modules)
                    if (mod.SupportedCommands != null)
                        foundArgs.Add(mod.SupportedCommands);

                foreach (IEnumerable<Library.Interface.ICommandLineArgument> arglst in foundArgs)
                    if (arglst != null)
                    {
                        foreach (Library.Interface.ICommandLineArgument arg in arglst)
                        {
                            if (!args.ContainsKey(arg.Name))
                                args[arg.Name] = arg;

                            if (arg.Aliases != null)
                                foreach (string a in arg.Aliases)
                                    if (!args.ContainsKey(a))
                                        args[a] = arg;
                        }
                    }
            }

            public IEnumerable<Library.Interface.ICommandLineArgument> Values { get { return args.Values; } }

            public string MathEvaluator(System.Text.RegularExpressions.Match m)
            {
                if (!m.Success || !m.Groups["name"].Success)
                    return "";

                Duplicati.Library.Interface.ICommandLineArgument arg;
                if (!args.TryGetValue(m.Groups["name"].Value, out arg))
                    return "";
                else
                    return PrintArgSimple(arg, m.Groups["name"].Value.ToLowerInvariant());
            }
        }

        private static string PrintArgsSimple(IEnumerable<Duplicati.Library.Interface.ICommandLineArgument> args)
        {
            if (args == null)
                return "";

            List<string> lines = new List<string>();
            foreach (Duplicati.Library.Interface.ICommandLineArgument arg in args)
                if (!arg.Deprecated)
                    lines.Add(PrintArgSimple(arg, arg.Name));

            return string.Join(Environment.NewLine, lines.ToArray());
        }


        private static string PrintArgSimple(Duplicati.Library.Interface.ICommandLineArgument arg, string name)
        {
            if (string.IsNullOrEmpty(arg.DefaultValue))
                return string.Format("  --{0}{1}    {2}", name, Environment.NewLine, arg.LongDescription);
            else
                return string.Format("  --{0} = {1}{2}    {3}", name, arg.DefaultValue, Environment.NewLine, arg.LongDescription);
        }
    }
}
