#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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
            try
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
                    //if they have the same data, but on different times

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

                if (cargs.Count == 1)
                {
                    switch (cargs[0].Trim().ToLower())
                    {
                        case "purge-signature-cache":
                            Library.Main.Interface.PurgeSignatureCache(options);
                            return;
                    }
                }

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

                if (!options.ContainsKey("ftp-username"))
                    if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("FTP_USERNAME")))
                        options["ftp-username"] = System.Environment.GetEnvironmentVariable("FTP_USERNAME");

                if (!Library.Core.Utility.IsClientLinux && (source.StartsWith("file://", StringComparison.InvariantCultureIgnoreCase) || target.StartsWith("file://", StringComparison.InvariantCultureIgnoreCase)))
                {
                    string tmpShort;
                    options.TryGetValue("short-filenames", out tmpShort);

                    if (!options.ContainsKey("time-separator") && !options.ContainsKey("time-seperator") && !Library.Core.Utility.ParseBool(tmpShort, false))
                    {
                        Console.Error.WriteLine(Strings.Program.SettingDefaultTimeSeperatorWarning);
                        options["time-separator"] = "'";
                    }
                }

                if (source.Trim().ToLower() == "list")
                    Console.WriteLine(string.Join("\r\n", Duplicati.Library.Main.Interface.List(target, options)));
                else if (source.Trim().ToLower() == "list-current-files")
                {
                    cargs.RemoveAt(0);

                    if (cargs.Count != 1)
                    {
                        Console.WriteLine(Strings.Program.WrongNumberOfArgumentsError);
                        return;
                    }

                    if (!EnsurePassphrase(options))
                        return;

                    Console.WriteLine(string.Join("\r\n", new List<string>(Duplicati.Library.Main.Interface.ListContent(target, options)).ToArray()));
                }
                else if (source.Trim().ToLower() == "list-source-folders")
                {
                    cargs.RemoveAt(0);

                    if (cargs.Count != 1)
                    {
                        Console.WriteLine(Strings.Program.WrongNumberOfArgumentsError);
                        return;
                    }

                    if (!EnsurePassphrase(options))
                        return;

                    Console.WriteLine(string.Join("\r\n", Duplicati.Library.Main.Interface.ListSourceFolders(target, options) ?? new string[0]));
                }
                else if (source.Trim().ToLower() == "list-actual-signature-files")
                {
                    cargs.RemoveAt(0);

                    if (cargs.Count != 1)
                    {
                        Console.WriteLine(Strings.Program.WrongNumberOfArgumentsError);
                        return;
                    }

                    if (!EnsurePassphrase(options))
                        return;

                    List<KeyValuePair<Duplicati.Library.Main.RSync.RSyncDir.PatchFileType, string>> files = Duplicati.Library.Main.Interface.ListActualSignatureFiles(cargs[0], options);

                    Console.WriteLine("* " + Strings.Program.DeletedFoldersHeader + ":");
                    foreach (KeyValuePair<Duplicati.Library.Main.RSync.RSyncDir.PatchFileType, string> x in files)
                        if (x.Key == Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.DeletedFolder)
                            Console.WriteLine(x.Value);

                    Console.WriteLine();
                    Console.WriteLine("* " + Strings.Program.AddedFoldersHeader + ":");
                    foreach (KeyValuePair<Duplicati.Library.Main.RSync.RSyncDir.PatchFileType, string> x in files)
                        if (x.Key == Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.AddedFolder)
                            Console.WriteLine(x.Value);

                    Console.WriteLine();
                    Console.WriteLine("* " + Strings.Program.DeletedFilesHeader + ":");
                    foreach (KeyValuePair<Duplicati.Library.Main.RSync.RSyncDir.PatchFileType, string> x in files)
                        if (x.Key == Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.DeletedFile)
                            Console.WriteLine(x.Value);

                    bool hasCombinedSignatures = false;
                    foreach (KeyValuePair<Duplicati.Library.Main.RSync.RSyncDir.PatchFileType, string> x in files)
                        if (x.Key == Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.AddedOrUpdatedFile)
                        {
                            hasCombinedSignatures = true;
                            break;
                        }

                    if (hasCombinedSignatures)
                    {
                        Console.WriteLine();
                        Console.WriteLine("* " + Strings.Program.NewOrModifiedFilesHeader + ":");
                        foreach (KeyValuePair<Duplicati.Library.Main.RSync.RSyncDir.PatchFileType, string> x in files)
                            if (x.Key == Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.AddedOrUpdatedFile)
                                Console.WriteLine(x.Value);
                    }
                    else
                    {
                        Console.WriteLine();
                        Console.WriteLine("* " + Strings.Program.NewFilesHeader + ":");
                        foreach (KeyValuePair<Duplicati.Library.Main.RSync.RSyncDir.PatchFileType, string> x in files)
                            if (x.Key == Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.AddedFile)
                                Console.WriteLine(x.Value);

                        Console.WriteLine();
                        Console.WriteLine("* " + Strings.Program.ModifiedFilesHeader + ":");
                        foreach (KeyValuePair<Duplicati.Library.Main.RSync.RSyncDir.PatchFileType, string> x in files)
                            if (x.Key == Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.UpdatedFile)
                                Console.WriteLine(x.Value);
                    }

                    Console.WriteLine();
                    Console.WriteLine("* " + Strings.Program.ControlFilesHeader + ":");
                    foreach (KeyValuePair<Duplicati.Library.Main.RSync.RSyncDir.PatchFileType, string> x in files)
                        if (x.Key == Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.ControlFile)
                            Console.WriteLine(x.Value);
                }
                else if (source.Trim().ToLower() == "delete-all-but-n-full")
                {
                    int n = 0;
                    if (!int.TryParse(target, out n) || n < 0)
                    {
                        Console.WriteLine(string.Format(Strings.Program.IntegerParseError, target));
                        return;
                    }

                    options["remove-all-but-n-full"] = n.ToString();

                    cargs.RemoveAt(0);
                    cargs.RemoveAt(0);

                    if (cargs.Count != 1)
                    {
                        Console.WriteLine(Strings.Program.WrongNumberOfArgumentsError);
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
                    catch (Exception ex)
                    {
                        Console.WriteLine(string.Format(Strings.Program.TimeParseError, target, ex.Message));
                        return;
                    }

                    options["remove-older-than"] = target;

                    cargs.RemoveAt(0);
                    cargs.RemoveAt(0);

                    if (cargs.Count != 1)
                    {
                        Console.WriteLine(Strings.Program.WrongNumberOfArgumentsError);
                        return;
                    }

                    Console.WriteLine(Duplicati.Library.Main.Interface.RemoveOlderThan(cargs[0], options));
                }
                else if (source.Trim().ToLower() == "cleanup")
                {
                    cargs.RemoveAt(0);

                    if (cargs.Count != 1)
                    {
                        Console.WriteLine(Strings.Program.WrongNumberOfArgumentsError);
                        return;
                    }

                    Console.WriteLine(Duplicati.Library.Main.Interface.Cleanup(cargs[0], options));
                }
                else if (source.IndexOf("://") > 0 || options.ContainsKey("restore"))
                {
                    if (!EnsurePassphrase(options))
                        return;

                    Console.WriteLine(Duplicati.Library.Main.Interface.Restore(source, target.Split(System.IO.Path.PathSeparator), options));
                }
                else
                {
                    if (!EnsurePassphrase(options))
                        return;

                    Console.WriteLine(Duplicati.Library.Main.Interface.Backup(source.Split(System.IO.Path.PathSeparator), target, options));
                }
            }
            catch (Exception ex)
            {
                while (ex is System.Reflection.TargetInvocationException && ex.InnerException != null)
                    ex = ex.InnerException;

                Console.Error.WriteLine(Strings.Program.UnhandledException, ex.ToString());

                while (ex.InnerException != null)
                {
                    ex = ex.InnerException;
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(Strings.Program.UnhandledInnerException, ex.ToString());
                }
            }
        }

        private static bool EnsurePassphrase(Dictionary<string, string> options)
        {
            if (!options.ContainsKey("passphrase") && !options.ContainsKey("no-encryption"))
            {
                string pwd = ReadPassphraseFromConsole(false);
                if (pwd == null)
                    return false;
                else
                    options["passphrase"] = pwd;
            }

            return true;
        }

        private static string ReadPassphraseFromConsole(bool confirm)
        {
            Console.Write("\n" + Strings.Program.EnterPassphrasePrompt + ": ");
            StringBuilder passphrase = new StringBuilder();

            while (true)
            {
                ConsoleKeyInfo k = Console.ReadKey(true);
                if (k.Key == ConsoleKey.Enter)
                    break;

                if (k.Key == ConsoleKey.Escape)
                    return null;

                passphrase.Append(k.KeyChar);

                //Unix/Linux user know that there is no feedback, Win user gets scared :)
                if (System.Environment.OSVersion.Platform != PlatformID.Unix)
                    Console.Write("*");
            }

            Console.WriteLine();

            if (confirm)
            {
                Console.Write("\n" + Strings.Program.ConfirmPassphrasePrompt + ": ");
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

                if (passphrase.ToString() != password2.ToString())
                {
                    Console.WriteLine(Strings.Program.PassphraseMismatchError);
                    return null;
                }
            }

            if (passphrase.ToString().Length == 0)
            {
                Console.WriteLine(Strings.Program.EmptyPassphraseError);
                return null;
            }

            return passphrase.ToString();
        }

        private static void PrintUsage(bool extended)
        {
            bool isLinux = Library.Core.Utility.IsClientLinux;

            List<string> lines = new List<string>();
            lines.AddRange(
                string.Format(
                    Strings.Program.Usage.Replace("\r", ""), 
                    System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    isLinux ? Strings.Program.ExampleLinux : Strings.Program.ExampleWindows
                ).Split('\n')
            );

            lines.Add(Strings.Program.DuplicatiOptionsHeader);
            Library.Main.Options opt = new Library.Main.Options(new Dictionary<string, string>());
            foreach (Library.Backend.ICommandLineArgument arg in opt.SupportedCommands)
                Library.Backend.CommandLineArgument.PrintArgument(lines, arg);

            lines.Add("");
            lines.Add("");
            lines.Add(Strings.Program.SupportedBackendsHeader);
            foreach (Duplicati.Library.Backend.IBackend back in Library.Backend.BackendLoader.LoadedBackends)
            {
                lines.Add(back.DisplayName + " (" + back.ProtocolKey + "):");
                lines.Add(" " + back.Description);
                lines.Add(" " + Strings.Program.SupportedOptionsHeader);
                foreach (Library.Backend.ICommandLineArgument arg in back.SupportedCommands)
                    Library.Backend.CommandLineArgument.PrintArgument(lines, arg);

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
                    if (len < c.Length)
                    {
                        int ix = c.LastIndexOf(" ", len);
                        if (ix > 0)
                            len = ix;
                    }

                    Console.WriteLine(leadingSpaces + c.Substring(0, len).Trim());
                    c = c.Remove(0, len);
                }
            }
        }
    }
}
