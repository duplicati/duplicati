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
        static void Main(string[] args)
        {
            try
            {
                List<string> cargs = new List<string>(args);
                string filter = Duplicati.Library.Utility.FilenameFilter.EncodeAsFilter(Duplicati.Library.Utility.FilenameFilter.ParseCommandLine(cargs, true));
                Dictionary<string, string> options = CommandLineParser.ExtractOptions(cargs);

                foreach (string internaloption in Library.Main.Options.InternalOptions)
                    if (options.ContainsKey(internaloption))
                    {
                        Console.WriteLine(Strings.Program.InternalOptionUsedError, internaloption);
                        return;
                    }

                if (!string.IsNullOrEmpty(filter))
                    options["filter"] = filter;

                //If we are on Windows, append the bundled "win-tools" programs to the search path
                //We add it last, to allow the user to override with other versions
                if (!Library.Utility.Utility.IsClientLinux)
                {
                    Environment.SetEnvironmentVariable("PATH",
                        Environment.GetEnvironmentVariable("PATH") +
                        System.IO.Path.PathSeparator.ToString() +
                        System.IO.Path.Combine(
                            System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                            "win-tools")
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

                if (cargs.Count > 0)
                {
                    //Because options are of the format --name=value, it seems natural to write "delete-all-but-n-full=5",
                    // so we allow that format as well
                    if (cargs[0].Trim().ToLower().StartsWith("delete-all-but-n-full="))
                    {
                        string p = cargs[0].Substring(cargs[0].IndexOf('=') + 1);
                        cargs[0] = "delete-all-but-n-full";
                        cargs.Insert(1, p);
                    }

                    //Because options are of the format --name=value, it seems natural to write "delete-older-than=5D",
                    // so we allow that format as well
                    if (cargs[0].Trim().ToLower().StartsWith("delete-older-than="))
                    {
                        string p = cargs[0].Substring(cargs[0].IndexOf('=') + 1);
                        cargs[0] = "delete-older-than";
                        cargs.Insert(1, p);
                    }
                }

                if (cargs.Count < 2)
                {
                    if (cargs.Count == 0 || cargs[0].Trim().Equals("help", StringComparison.InvariantCultureIgnoreCase))
                        PrintUsage(true);
                    else
                    {
                        PrintUsage(false);
                        PrintWrongNumberOfArguments(cargs, 2);
                    }
                    

                    return;
                }


                string source = cargs[0];
                string target = cargs[1];

                if (source.Trim().ToLower() == "restore" && cargs.Count == 3)
                {
                    source = target;
                    target = cargs[2];
                    options["restore"] = null;
                    cargs.RemoveAt(0);
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

                if (source.Trim().ToLower() == "list")
                    Console.WriteLine(string.Join("\r\n", Duplicati.Library.Main.Interface.List(target, options)));
                else if (source.Trim().ToLower() == "list-current-files")
                {
                    cargs.RemoveAt(0);

                    if (cargs.Count != 1)
                    {
                        PrintWrongNumberOfArguments(cargs, 1);
                        return;
                    }

                    Console.WriteLine(string.Join("\r\n", new List<string>(Duplicati.Library.Main.Interface.ListCurrentFiles(target, options)).ToArray()));
                }
                else if (source.Trim().ToLower() == "list-source-folders")
                {
                    cargs.RemoveAt(0);

                    if (cargs.Count != 1)
                    {
                        PrintWrongNumberOfArguments(cargs, 1);
                        return;
                    }

                    Console.WriteLine(string.Join("\r\n", Duplicati.Library.Main.Interface.ListSourceFolders(target, options) ?? new string[0]));
                }
                else if (source.Trim().ToLower() == "list-actual-signature-files")
                {
                    cargs.RemoveAt(0);

                    if (cargs.Count != 1)
                    {
                        PrintWrongNumberOfArguments(cargs, 1);
                        return;
                    }

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
                else if (source.Trim().ToLower() == "collection-status")
                {
                    cargs.RemoveAt(0);

                    if (cargs.Count != 1)
                    {
                        PrintWrongNumberOfArguments(cargs, 1);
                        return;
                    }

                    List<Duplicati.Library.Main.ManifestEntry> entries = Duplicati.Library.Main.Interface.ParseFileList(cargs[0], options);
                    
                    Console.WriteLine(Strings.Program.CollectionStatusHeader.Replace("\\t", "\t"), entries.Count);
                    
                    foreach (Duplicati.Library.Main.ManifestEntry m in entries)
                    {
                        Console.WriteLine();

                        long size = Math.Max(m.Fileentry.Size ,0);
                        foreach (KeyValuePair<Duplicati.Library.Main.SignatureEntry, Duplicati.Library.Main.ContentEntry> x in m.Volumes)
                            size += Math.Max(x.Key.Fileentry.Size, 0) + Math.Max(x.Value.Fileentry.Size, 0);

                        Console.WriteLine(Strings.Program.CollectionStatusLineFull.Replace("\\t", "\t"), m.Time.ToString(), m.Volumes.Count, Library.Utility.Utility.FormatSizeString(size));

                        foreach (Duplicati.Library.Main.ManifestEntry mi in m.Incrementals)
                        {
                            size = Math.Max(mi.Fileentry.Size, 0);
                            foreach (KeyValuePair<Duplicati.Library.Main.SignatureEntry, Duplicati.Library.Main.ContentEntry> x in mi.Volumes)
                                size += Math.Max(x.Key.Fileentry.Size, 0) + Math.Max(x.Value.Fileentry.Size, 0);

                            Console.WriteLine(Strings.Program.CollectionStatusLineInc.Replace("\\t", "\t"), mi.Time.ToString(), mi.Volumes.Count, Library.Utility.Utility.FormatSizeString(size));
                        }
                    }
                }
                else if (source.Trim().ToLower() == "delete-all-but-n-full")
                {
                    int n = 0;
                    if (!int.TryParse(target, out n) || n < 0)
                    {
                        Console.WriteLine(string.Format(Strings.Program.IntegerParseError, target));
                        return;
                    }

                    options["delete-all-but-n-full"] = n.ToString();

                    cargs.RemoveAt(0);
                    cargs.RemoveAt(0);

                    if (cargs.Count != 1)
                    {
                        PrintWrongNumberOfArguments(cargs, 1);
                        return;
                    }

                    Console.WriteLine(Duplicati.Library.Main.Interface.DeleteAllButNFull(cargs[0], options));
                }
                else if (source.Trim().ToLower() == "delete-older-than")
                {
                    try
                    {
                        Duplicati.Library.Utility.Timeparser.ParseTimeSpan(target);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(string.Format(Strings.Program.TimeParseError, target, ex.Message));
                        return;
                    }

                    options["delete-older-than"] = target;

                    cargs.RemoveAt(0);
                    cargs.RemoveAt(0);

                    if (cargs.Count != 1)
                    {
                        PrintWrongNumberOfArguments(cargs, 1);
                        return;
                    }

                    Console.WriteLine(Duplicati.Library.Main.Interface.DeleteOlderThan(cargs[0], options));
                }
                else if (source.Trim().ToLower() == "cleanup")
                {
                    cargs.RemoveAt(0);

                    if (cargs.Count != 1)
                    {
                        PrintWrongNumberOfArguments(cargs, 1);
                        return;
                    }

                    Console.WriteLine(Duplicati.Library.Main.Interface.Cleanup(cargs[0], options));
                }
                else if (source.Trim().ToLower() == "create-folder")
                {
                    cargs.RemoveAt(0);

                    if (cargs.Count != 1)
                    {
                        PrintWrongNumberOfArguments(cargs, 1);
                        return;
                    }

                    Duplicati.Library.Main.Interface.CreateFolder(cargs[0], options);
                    Console.WriteLine(string.Format(Strings.Program.FolderCreatedMessage, cargs[0]));
                }
                else if (source.Trim().ToLower() == "find-last-version")
                {
                    cargs.RemoveAt(0);

                    if (cargs.Count != 1)
                    {
                        PrintWrongNumberOfArguments(cargs, 1);
                        return;
                    }

                    List<KeyValuePair<string, DateTime>> results = Duplicati.Library.Main.Interface.FindLastFileVersion(cargs[0], options);
                    Console.WriteLine(Strings.Program.FindLastVersionHeader.Replace("\\t", "\t"));
                    foreach(KeyValuePair<string, DateTime> k in results)
                        Console.WriteLine(string.Format(Strings.Program.FindLastVersionEntry.Replace("\\t", "\t"), k.Value.Ticks == 0 ? Strings.Program.FileEntryNotFound : k.Value.ToLocalTime().ToString("yyyyMMdd hhmmss"), k.Key));
                }
                else if (source.Trim().ToLower() == "verify")
                {
                    cargs.RemoveAt(0);

                    if (cargs.Count != 1)
                    {
                        PrintWrongNumberOfArguments(cargs, 1);
                        return;
                    }

                    List<KeyValuePair<Duplicati.Library.Main.BackupEntryBase, Exception>> results = Duplicati.Library.Main.Interface.VerifyBackup(cargs[0], options);

                    int manifests = 0;
                    int signatures = 0;
                    int contentfiles = 0;
                    int errors = 0;

                    foreach (KeyValuePair<Duplicati.Library.Main.BackupEntryBase, Exception> x in results)
                    {
                        if (x.Key is Duplicati.Library.Main.ManifestEntry)
                            manifests++;
                        else if (x.Key is Duplicati.Library.Main.SignatureEntry)
                            signatures++;
                        else if (x.Key is Duplicati.Library.Main.ContentEntry)
                            contentfiles++;

                        if (x.Value != null)
                            errors++;
                    }

                    Console.WriteLine(string.Format(Strings.Program.VerificationCompleted, manifests, signatures, contentfiles, errors));
                    if (errors > 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine(Strings.Program.VerificationErrorHeader);
                        Console.WriteLine();

                        foreach (KeyValuePair<Duplicati.Library.Main.BackupEntryBase, Exception> x in results)
                            if (x.Value != null)
                                Console.WriteLine(string.Format("{0}: {1}", x.Key.Filename, x.Value.Message));
                    }
                }
                else if (source.IndexOf("://") > 0 || options.ContainsKey("restore"))
                {
                    if (cargs.Count != 2)
                    {
                        PrintWrongNumberOfArguments(cargs, 2);
                        return;
                    }

                    Console.WriteLine(Duplicati.Library.Main.Interface.Restore(source, target.Split(System.IO.Path.PathSeparator), options));
                }
                else
                {
                    if (cargs.Count != 2)
                    {
                        PrintWrongNumberOfArguments(cargs, 2);
                        return;
                    } 
                    
                    Console.WriteLine(Duplicati.Library.Main.Interface.Backup(source.Split(System.IO.Path.PathSeparator), target, options));
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
                        Console.Error.WriteLine(ex.Message);
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
            }
        }

        private static void PrintWrongNumberOfArguments(List<string> args, int expected)
        {
            Console.WriteLine(Strings.Program.WrongNumberOfCommandsError_v2, args.Count, expected, args.Count == 0 ? "" : "\"" + string.Join("\", \"", args.ToArray()) + "\"");

        }

        private static void PrintUsage(bool extended)
        {
            bool isLinux = Library.Utility.Utility.IsClientLinux;

            List<string> lines = new List<string>();
            lines.AddRange(
                string.Format(
                    Strings.Program.ProgramUsageHeader.Replace("\r", ""), 
                    License.VersionNumbers.Version
                ).Split('\n')
            );

            lines.AddRange(("\n " + Strings.Program.ProgramUsageBackup.Replace("\r", "")).Split('\n'));
            lines.AddRange(("\n " + Strings.Program.ProgramUsageRestore.Replace("\r", "")).Split('\n'));
            lines.AddRange(("\n " + Strings.Program.ProgramUsageCleanup.Replace("\r", "")).Split('\n'));
            lines.AddRange(("\n " + Strings.Program.ProgramUsageListFiles.Replace("\r", "")).Split('\n'));
            lines.AddRange(("\n " + Strings.Program.ProgramUsageListSets.Replace("\r", "")).Split('\n'));
            lines.AddRange(("\n " + Strings.Program.ProgramUsageListContentFiles.Replace("\r", "")).Split('\n'));
            lines.AddRange(("\n " + Strings.Program.ProgramUsageListSourceFolders.Replace("\r", "")).Split('\n'));
            lines.AddRange(("\n " + Strings.Program.ProgramUsageListSignatureFiles.Replace("\r", "")).Split('\n'));
            lines.AddRange(("\n " + Strings.Program.ProgramUsageFindLastVersion.Replace("\r", "")).Split('\n'));
            lines.AddRange(("\n " + Strings.Program.ProgramUsageVerify.Replace("\r", "")).Split('\n'));
            lines.AddRange(("\n " + Strings.Program.ProgramUsagePurgeCache.Replace("\r", "")).Split('\n'));
            lines.AddRange(("\n " + Strings.Program.ProgramUsageDeleteOld.Replace("\r", "")).Split('\n'));
            lines.AddRange(("\n " + Strings.Program.ProgramUsageCreateFolders.Replace("\r", "")).Split('\n'));

            lines.AddRange(("\n " + Strings.Program.ProgramUsageBackend.Replace("\r", "")).Split('\n'));
            lines.AddRange(("\n" + Strings.Program.ProgramUsageOptionTypes.Replace("\r", "")).Split('\n'));
            lines.AddRange(("\n" + Strings.Program.ProgramUsageTimes.Replace("\r", "")).Split('\n'));
            lines.AddRange(
                string.Format( 
                    "\n" + Strings.Program.ProgramUsageFilters.Replace("\r", ""), 
                    isLinux ? Strings.Program.UsageExampleLinux : Strings.Program.UsageExampleWindows
                ).Split('\n')
            );

            lines.Add("");

            if (extended)
            {

                lines.Add(Strings.Program.DuplicatiOptionsHeader);
                Library.Main.Options opt = new Library.Main.Options(new Dictionary<string, string>());
                foreach (Library.Interface.ICommandLineArgument arg in opt.SupportedCommands)
                    Library.Interface.CommandLineArgument.PrintArgument(lines, arg);

                lines.Add("");
                lines.Add("");
                lines.Add(Strings.Program.SupportedBackendsHeader);
                foreach (Duplicati.Library.Interface.IBackend back in Library.DynamicLoader.BackendLoader.Backends)
                {
                    lines.Add(back.DisplayName + " (" + back.ProtocolKey + "):");
                    lines.Add(" " + back.Description);
                    if (back.SupportedCommands != null && back.SupportedCommands.Count > 0)
                    {
                        lines.Add(" " + Strings.Program.SupportedOptionsHeader);
                        foreach (Library.Interface.ICommandLineArgument arg in back.SupportedCommands)
                            Library.Interface.CommandLineArgument.PrintArgument(lines, arg);
                    }
                    lines.Add("");
                }

                lines.Add("");
                lines.Add("");
                lines.Add(Strings.Program.SupportedEncryptionModulesHeader);
                foreach (Duplicati.Library.Interface.IEncryption mod in Library.DynamicLoader.EncryptionLoader.Modules)
                {
                    lines.Add(mod.DisplayName + " (." + mod.FilenameExtension + "):");
                    lines.Add(" " + mod.Description);
                    if (mod.SupportedCommands != null && mod.SupportedCommands.Count > 0)
                    {
                        lines.Add(" " + Strings.Program.SupportedOptionsHeader);
                        foreach (Library.Interface.ICommandLineArgument arg in mod.SupportedCommands)
                            Library.Interface.CommandLineArgument.PrintArgument(lines, arg);
                    }
                    lines.Add("");
                }

                lines.Add("");
                lines.Add("");
                lines.Add(Strings.Program.SupportedCompressionModulesHeader);
                foreach (Duplicati.Library.Interface.ICompression mod in Library.DynamicLoader.CompressionLoader.Modules)
                {
                    lines.Add(mod.DisplayName + " (." + mod.FilenameExtension + "):");
                    lines.Add(" " + mod.Description);
                    if (mod.SupportedCommands != null && mod.SupportedCommands.Count > 0)
                    {
                        lines.Add(" " + Strings.Program.SupportedOptionsHeader);
                        foreach (Library.Interface.ICommandLineArgument arg in mod.SupportedCommands)
                            Library.Interface.CommandLineArgument.PrintArgument(lines, arg);
                    }
                    lines.Add("");
                }
                lines.Add("");

                lines.Add("");
                lines.Add("");
                lines.Add(Strings.Program.GenericModulesHeader);
                foreach (Duplicati.Library.Interface.IGenericModule mod in Library.DynamicLoader.GenericLoader.Modules)
                {
                    lines.Add(mod.DisplayName + " (." + mod.Key + "):");
                    lines.Add(" " + mod.Description);
                    lines.Add(" " + (mod.LoadAsDefault ? Strings.Program.ModuleIsLoadedAutomatically : Strings.Program.ModuleIsNotLoadedAutomatically));
                    if (mod.SupportedCommands != null && mod.SupportedCommands.Count > 0)
                    {
                        lines.Add(" " + Strings.Program.SupportedOptionsHeader);
                        foreach (Library.Interface.ICommandLineArgument arg in mod.SupportedCommands)
                            Library.Interface.CommandLineArgument.PrintArgument(lines, arg);
                    }
                    lines.Add("");
                }
                lines.Add("");
            }

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
