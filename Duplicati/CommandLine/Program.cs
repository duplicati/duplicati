#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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
        static void Main(string[] args)
        {
            List<string> cargs = new List<string>(args);
            string filter = Duplicati.Library.Core.FilenameFilter.EncodeAsFilter(Duplicati.Library.Core.FilenameFilter.ParseCommandLine(cargs, true));

            if (!string.IsNullOrEmpty(filter))
                cargs.Add(filter);
            
            Dictionary<string, string> options = CommandLineParser.ExtractOptions(cargs);

#if DEBUG
            if (cargs.Count > 1 && cargs[0].ToLower() == "unittest")
            {
                //The unit test is only enabled in DEBUG builds
                //it works by getting a list of folders, and treats them as 
                //if they were they have the same data, but on different times

                //The first folder is used to make a full backup,
                //and each subsequent folder is used to make an incremental backup

                //After all backups are made, the files are restored and verified against
                //the original folders.

                //The best way to test it, is to use SVN checkouts at different
                //revisions, as this is how a regular folder would evolve

                cargs.RemoveAt(0);
                UnitTest.RunTest(cargs.ToArray(), options);
                return;
            }
#endif

            //TODO: Print usage window
            if (cargs.Count < 2)
            {
                PrintUsage(true);
                return;
            }

            string source = cargs[0];
            string target = cargs[1];

            if (source.Trim().ToLower() == "restore" && cargs.Count == 3)
            {
                source = target;
                target = cargs[2];
                options["restore"] = null;
            }

            if (!options.ContainsKey("passphrase"))
                if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PASSPHRASE")))
                    options["passphrase"] = System.Environment.GetEnvironmentVariable("PASSPHRASE");

            if (!options.ContainsKey("ftp-password"))
                if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("FTP_PASSWORD")))
                    options["ftp-password"] = System.Environment.GetEnvironmentVariable("FTP_PASSWORD");

            if (source.Trim().ToLower() == "list")
                Console.WriteLine(string.Join("\r\n", Duplicati.Library.Main.Interface.List(target, options)));
            else if (source.Trim().ToLower() == "list-current-files")
                Console.WriteLine(string.Join("\r\n", new List<string>(Duplicati.Library.Main.Interface.ListContent(target, options)).ToArray()));
            else if (source.Trim().ToLower() == "delete-all-but-n-full")
            {
                int n = 0;
                if (!int.TryParse(target, out n) || n < 0)
                {
                    Console.WriteLine("Unable to parse: \"" + target + "\" into a number");
                    return;
                }

                options["remove-all-but-n-full"] = n.ToString();

                cargs.RemoveAt(0);
                cargs.RemoveAt(0);

                if (cargs.Count != 1)
                {
                    Console.WriteLine("Wrong number of aguments");
                    return;
                }

                Console.WriteLine(Duplicati.Library.Main.Interface.RemoveAllButNFull(cargs[0], options));
            }
            else if (source.Trim().ToLower() == "delete-older-than")
            {
                try
                {
                    Duplicati.Library.Core.Timeparser.ParseTimeSpan(target);
                }
                catch(Exception ex)
                {
                    Console.WriteLine("Unable to parse \"" + target + "\" into a time offset: " + ex.Message);
                    return;
                }

                options["remove-older-than"] = target;

                cargs.RemoveAt(0);
                cargs.RemoveAt(0);

                if (cargs.Count != 1)
                {
                    Console.WriteLine("Wrong number of aguments");
                    return;
                }

                Console.WriteLine(Duplicati.Library.Main.Interface.RemoveOlderThan(cargs[0], options));
            }
            else if (source.Trim().ToLower() == "cleanup")
            {
                cargs.RemoveAt(0);

                if (cargs.Count != 1)
                {
                    Console.WriteLine("Wrong number of aguments");
                    return;
                }

                Console.WriteLine(Duplicati.Library.Main.Interface.Cleanup(cargs[0], options));
            }
            else if (source.IndexOf("://") > 0 || options.ContainsKey("restore"))
            {
                if (!options.ContainsKey("passphrase") && !options.ContainsKey("no-encryption"))
                {
                    string pwd = ReadPassphraseFromConsole(false);
                    if (pwd == null)
                        return;
                    else
                        options["passphrase"] = pwd;
                }

                Console.WriteLine(Duplicati.Library.Main.Interface.Restore(source, target, options));
            }
            else
            {
                if (!options.ContainsKey("passphrase") && !options.ContainsKey("no-encryption"))
                {
                    string pwd = ReadPassphraseFromConsole(true);
                    if (pwd == null)
                        return;
                    else
                        options["passphrase"] = pwd;
                }

                Console.WriteLine(Duplicati.Library.Main.Interface.Backup(source, target, options));
            }
        }

        private static string ReadPassphraseFromConsole(bool confirm)
        {
            Console.Write("\nEnter passphrase: ");
            StringBuilder password = new StringBuilder();

            while (true)
            {
                ConsoleKeyInfo k = Console.ReadKey(true);
                if (k.Key == ConsoleKey.Enter)
                    break;

                if (k.Key == ConsoleKey.Escape)
                    return null;

                password.Append(k.KeyChar);

                //Unix/Linux user know that there is no feedback, Win user gets scared :)
                if (System.Environment.OSVersion.Platform != PlatformID.Unix)
                    Console.Write("*");
            }

            Console.WriteLine();

            if (confirm)
            {
                Console.Write("\nConfirm passphrase: ");
                StringBuilder password2 = new StringBuilder();

                while (true)
                {
                    ConsoleKeyInfo k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Enter)
                        break;

                    if (k.Key == ConsoleKey.Escape)
                        return null;

                    password2.Append(k.KeyChar);

                    //Unix/Linux user know that there is no feedback, Win user gets scared :)
                    if (System.Environment.OSVersion.Platform != PlatformID.Unix)
                        Console.Write("*");
                }
                Console.WriteLine();

                if (password.ToString() != password2.ToString())
                {
                    Console.WriteLine("The passwords do not match");
                    return null;
                }
            }

            if (password.ToString().Length == 0)
            {
                Console.WriteLine("Empty passwords are not allowed");
                return null;
            }

            return password.ToString();
        }

        private static void PrintUsage(bool extended)
        {
            List<string> lines = new List<string>();
            lines.Add("********** Duplicati v. ??? **********");
            lines.Add("");
            lines.Add("Usage:");
            lines.Add("");
            lines.Add(" Backup (make a full or incremental backup):");
            lines.Add("  Duplicati.CommandLine [full] [options] <sourcefolder> <backend>");
            lines.Add("");
            lines.Add(" Restore (restore all or some files):");
            lines.Add("  Duplicati.CommandLine [options] <backend> <destinationfolder>");
            lines.Add("");
            lines.Add(" Cleanup (remove partial and unused files):");
            lines.Add("  Duplicati.CommandLine cleanup [options] <backend>");
            lines.Add("");
            lines.Add(" List files (backup volumes):");
            lines.Add("  Duplicati.CommandLine list [options] <backend>");
            lines.Add("");
            lines.Add(" List content files (backed up files):");
            lines.Add("  Duplicati.CommandLine list-current-files [options] <backend>");
            lines.Add("");
            lines.Add(" Delete old backups:");
            lines.Add("  Duplicati.CommandLine delete-all-but-n-full <number of full backups to keep> [options] <backend>");
            lines.Add("  Duplicati.CommandLine delete-older-than <max allowed age> [options] <backend>");
            lines.Add("");
            lines.Add("");
            lines.Add(" A <backend> is identified by an url like ftp://host/ or ssh://server/.");
            lines.Add(" Using this system, Duplicati can detect if you want to backup or restore.");
            lines.Add(" The cleanup and delete command does not delete files, unless the --force option is specified, so you may examine what files are affected, before actually deleting the files.");
            lines.Add(" The cleanup command should not be used unless a backup was interrupted and has left partial files. Duplicati will inform you if this happens.");
            lines.Add(" The delete commands can be used to remove backup sets when newer backups are present.");
            lines.Add("");
            lines.Add("Option types:");
            lines.Add("");
            lines.Add(" The following option types are avalible:");
            lines.Add("   Integer: a numerical value");
            lines.Add("   Boolean: a truth value, --force and --force=true are equivalent. --force=false is the oposite");
            lines.Add("   Timespan: a time in the special time format");
            lines.Add("   Size: a size like 5mb or 200kb");
            lines.Add("   Enumeration: any of the listed values");
            lines.Add("   Path: the path to a folder or file");
            lines.Add("   String: any other type");
            lines.Add("");
            lines.Add("Times:");
            lines.Add(" Duplicati uses the time system from duplicity, where times may be presented as:");
            lines.Add("  1: the string \"now\", indicating the current time");
            lines.Add("  2: the number of seconds after epoch, eg: 123456890");
            lines.Add("  3: a string like \"2009-03-26T08:30:00+01:00\"");
            lines.Add("  4: an interval string, using Y, M, W, D, h, m, s for Year, Month, Week, Day, hour, minute or second, eg: \"1M4D\" for one month and four days, or \"5m\" for five minutes.");
            lines.Add("");
            lines.Add("Duplicati options:");
            Library.Main.Options opt = new Library.Main.Options(new Dictionary<string, string>());
            foreach (Library.Backend.ICommandLineArgument arg in opt.SupportedCommands)
                PrintArgument(lines, arg);
            lines.Add("");
            lines.Add("");
            lines.Add("Supported backends:");
            foreach (Duplicati.Library.Backend.IBackend back in Library.Backend.BackendLoader.LoadedBackends)
            {
                lines.Add(back.DisplayName + " (" + back.ProtocolKey + "):");
                lines.Add(" " + back.Description);
                lines.Add(" supported options:");
                foreach (Library.Backend.ICommandLineArgument arg in back.SupportedCommands)
                    PrintArgument(lines, arg);

                lines.Add("");
            }
            lines.Add("");

            foreach (string s in lines)
            {
                if (string.IsNullOrEmpty(s))
                {
                    Console.WriteLine();
                    continue;
                }

                string c = s;

                string leadingSpaces = "";
                while (c.Length > 0 && c.StartsWith(" "))
                {
                    leadingSpaces += " ";
                    c = c.Remove(0, 1);
                }

                while (c.Length > 0)
                {
                    int len = Math.Min(Console.WindowWidth - 2, leadingSpaces.Length + c.Length);
                    len -= leadingSpaces.Length;
                    Console.WriteLine(leadingSpaces + c.Substring(0, len));
                    c = c.Remove(0, len);
                }
            }

        }

        private static void PrintArgument(List<string> lines, Duplicati.Library.Backend.ICommandLineArgument arg)
        {
            lines.Add(" --" + arg.Name + " (" + arg.Type.ToString()+ "): " + arg.ShortDescription);
            lines.Add("  " + arg.LongDescription);
            if (arg.Aliases != null && arg.Aliases.Length > 0)
                lines.Add("  aliases: --" + string.Join(", --", arg.Aliases));

            if (arg.ValidValues != null && arg.ValidValues.Length > 0)
                lines.Add("  values: " + string.Join(", ", arg.ValidValues));

            if (!string.IsNullOrEmpty(arg.DefaultValue))
                lines.Add("  default value: " + arg.DefaultValue);

        }
    }
}
