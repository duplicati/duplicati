using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Duplicati.CommandLine
{
    public static class Help
    {
        private static readonly Dictionary<string, string> _document;
        private const string RESOURCE_NAME = "Duplicati.CommandLine.help.txt";
        private static readonly System.Text.RegularExpressions.Regex NAMEDOPTION_REGEX = new System.Text.RegularExpressions.Regex("\\%OPTION\\:(?<name>[^\\%]+)\\%");

        static Help()
        {
            _document = new Dictionary<string,string>(StringComparer.InvariantCultureIgnoreCase);

            using (System.IO.StreamReader sr = new System.IO.StreamReader(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(RESOURCE_NAME)))
            {
                List<string> keywords = new List<string>();
                StringBuilder sb = new StringBuilder();
                foreach(var line in sr.ReadToEnd().Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
                {
                    if (line.Trim().StartsWith("#"))
                        continue;

                    if (line.Trim().StartsWith(">"))
                    {
                        if (sb.Length > 0)
                        {
                            string s = sb.ToString();
                            foreach(var k in keywords)
                                _document[k] = s;

                            keywords.Clear();
                            sb.Clear();
                        }

                        string[] elems = line.Split(new string[] {" ", "\t"}, StringSplitOptions.RemoveEmptyEntries);
                        if (elems.Length >= 2 && string.Equals(elems[elems.Length - 2], "help", StringComparison.InvariantCultureIgnoreCase))
                            keywords.Add(elems[elems.Length - 1]);
                        else if (elems.Length == 3 && string.Equals(elems[elems.Length - 1], "help", StringComparison.InvariantCultureIgnoreCase))
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
                    foreach(var k in keywords)
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
                    System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                    System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
                }
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(topic))
                topic = "help";

            if (string.Equals("help", topic, StringComparison.InvariantCultureIgnoreCase))
            {
                if (options.Count == 1)
                    topic = new List<string>(options.Keys)[0];
                else if (System.Environment.CommandLine.IndexOf("--exclude", StringComparison.InvariantCultureIgnoreCase) >= 0)
                    topic = "exclude";
                else if (System.Environment.CommandLine.IndexOf("--include", StringComparison.InvariantCultureIgnoreCase) >= 0)
                    topic = "include";
            }


            if (_document.ContainsKey(topic))
            {
                string tp = _document[topic];
                Library.Main.Options opts = new Library.Main.Options(new Dictionary<string, string>());

                tp = tp.Replace("%VERSION%", License.VersionNumbers.Version);
                tp = tp.Replace("%BACKENDS%", string.Join(", ", Library.DynamicLoader.BackendLoader.Keys));
                tp = tp.Replace("%MONO%", Library.Utility.Utility.IsMono ? "mono " : "");
                tp = tp.Replace("%APP_PATH%", System.IO.Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location));
                tp = tp.Replace("%PATH_SEPARATOR%", System.IO.Path.PathSeparator.ToString());
                tp = tp.Replace("%EXAMPLE_SOURCE_PATH%", Library.Utility.Utility.IsClientLinux ? "/source" : "D:\\source");
                tp = tp.Replace("%EXAMPLE_SOURCE_FILE%", Library.Utility.Utility.IsClientLinux ? "/source/myfile.txt" : "D:\\source\\file.txt");
                tp = tp.Replace("%EXAMPLE_RESTORE_PATH%", Library.Utility.Utility.IsClientLinux ? "/restore" : "D:\\restore");
                tp = tp.Replace("%ENCRYPTIONMODULES%", string.Join(", ", Library.DynamicLoader.EncryptionLoader.Keys));
                tp = tp.Replace("%COMPRESSIONMODULES%", string.Join(", ", Library.DynamicLoader.CompressionLoader.Keys));
                tp = tp.Replace("%DEFAULTENCRYPTIONMODULE%", opts.EncryptionModule);
                tp = tp.Replace("%DEFAULTCOMPRESSIONMODULE%", opts.CompressionModule);
                tp = tp.Replace("%GENERICMODULES%", string.Join(", ", Library.DynamicLoader.GenericLoader.Keys));

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
                        if (string.Equals(backend.ProtocolKey, topic, StringComparison.InvariantCultureIgnoreCase))
                        {
                            args = backend.SupportedCommands;
                            found = true;
                            break;
                        }

                    if (args == null)
                        foreach (Duplicati.Library.Interface.IEncryption module in Library.DynamicLoader.EncryptionLoader.Modules)
                            if (string.Equals(module.FilenameExtension, topic, StringComparison.InvariantCultureIgnoreCase))
                            {
                                args = module.SupportedCommands;
                                found = true;
                                break;
                            }

                    if (args == null)
                        foreach (Duplicati.Library.Interface.ICompression module in Library.DynamicLoader.CompressionLoader.Modules)
                            if (string.Equals(module.FilenameExtension, topic, StringComparison.InvariantCultureIgnoreCase))
                            {
                                args = module.SupportedCommands;
                                found = true;
                                break;
                            }

                    if (args == null)
                        foreach (Duplicati.Library.Interface.IGenericModule module in Library.DynamicLoader.GenericLoader.Modules)
                            if (string.Equals(module.Key, topic, StringComparison.InvariantCultureIgnoreCase))
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

                if (NAMEDOPTION_REGEX.IsMatch(tp))
                    tp = NAMEDOPTION_REGEX.Replace(tp, new Matcher().MathEvaluator); 

                PrintFormatted(outwriter, tp.Split(new string[] { Environment.NewLine }, StringSplitOptions.None));
            }
            else
            {
                List<string> lines = new List<string>();

                foreach (Duplicati.Library.Interface.IBackend backend in Library.DynamicLoader.BackendLoader.Backends)
                    if (string.Equals(backend.ProtocolKey, topic, StringComparison.InvariantCultureIgnoreCase))
                    {
                        PrintBackend(backend, lines);
                        break;
                    }

                if (lines.Count == 0)
                    foreach (Duplicati.Library.Interface.IEncryption mod in Library.DynamicLoader.EncryptionLoader.Modules)
                        if (string.Equals(mod.FilenameExtension, topic, StringComparison.InvariantCultureIgnoreCase))
                        {
                            PrintEncryptionModule(mod, lines);
                            break;
                        }

                if (lines.Count == 0)
                    foreach (Duplicati.Library.Interface.ICompression mod in Library.DynamicLoader.CompressionLoader.Modules)
                        if (string.Equals(mod.FilenameExtension, topic, StringComparison.InvariantCultureIgnoreCase))
                        {
                            PrintCompressionModule(mod, lines);
                            break;
                        }

                if (lines.Count == 0)
                    foreach (Duplicati.Library.Interface.IGenericModule mod in Library.DynamicLoader.GenericLoader.Modules)
                        if (string.Equals(mod.Key, topic, StringComparison.InvariantCultureIgnoreCase))
                        {
                            PrintGenericModule(mod, lines);
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
                if (string.Equals(arg.Name, topic, StringComparison.InvariantCultureIgnoreCase))
                {
                    Library.Interface.CommandLineArgument.PrintArgument(lines, arg, "  ");
                    return;
                }

                if (arg.Aliases != null)
                    foreach (string k in arg.Aliases)
                    {
                        if (string.Equals(k, topic, StringComparison.InvariantCultureIgnoreCase))
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

        private static string PrintArguments(IEnumerable<Duplicati.Library.Interface.ICommandLineArgument> args)
        {
            if (args == null)
                return "";

            List<string> lines = new List<string>();
            foreach (Library.Interface.ICommandLineArgument arg in args)
                Library.Interface.CommandLineArgument.PrintArgument(lines, arg, "  ");

            return string.Join(Environment.NewLine, lines.ToArray());

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

                string leadingSpaces = "";
                while (c.Length > 0 && c.StartsWith(" "))
                {
                    leadingSpaces += " ";
                    c = c.Remove(0, 1);
                }

                bool extraIndent = c.StartsWith("--");

                while (c.Length > 0)
                {
                    int len = Math.Min(windowWidth - 2, leadingSpaces.Length + c.Length);
                    len -= leadingSpaces.Length;
                    if (len < c.Length)
                    {
                        int ix = c.LastIndexOf(" ", len);
                        if (ix > 0)
                            len = ix;
                    }

                    outwriter.WriteLine(leadingSpaces + c.Substring(0, len).Trim());
                    c = c.Remove(0, len);
                    if (extraIndent)
                    {
                        extraIndent = false;
                        leadingSpaces += "  ";
                    }
                }
            }
        }

        private class Matcher
        {
            Dictionary<string, Library.Interface.ICommandLineArgument> args = new Dictionary<string, Library.Interface.ICommandLineArgument>(StringComparer.InvariantCultureIgnoreCase);

            public Matcher()
            {
                List<IList<Library.Interface.ICommandLineArgument>> foundArgs = new List<IList<Library.Interface.ICommandLineArgument>>();
                foundArgs.Add(new Library.Main.Options(new Dictionary<string, string>()).SupportedCommands);
                foundArgs.Add(Program.SupportedOptions);

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
            foreach(Duplicati.Library.Interface.ICommandLineArgument arg in args)
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
