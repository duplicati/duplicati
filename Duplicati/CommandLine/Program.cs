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
        private static readonly string[] COMMANDS_AS_ARGUMENTS = new string[] { "delete-all-but-n-full", "delete-all-but-n", "delete-older-than", "delete-filesets" };

        static int Main(string[] args)
        {
            bool verboseErrors = false;
            try
            {
                List<string> cargs = new List<string>(args);
                string filter = Duplicati.Library.Utility.FilenameFilter.EncodeAsFilter(Duplicati.Library.Utility.FilenameFilter.ParseCommandLine(cargs, true));
                Dictionary<string, string> options = Library.Utility.CommandLineParser.ExtractOptions(cargs);

                verboseErrors = Library.Utility.Utility.ParseBoolOption(options, "debug-output");

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
                    return 0;
                }
#endif

                if (cargs.Count > 0)
                {
                    //Because options are of the format --name=value, it seems natural to write "delete-all-but-n-full=5",
                    // so we allow that format as well
                    foreach (string s in COMMANDS_AS_ARGUMENTS)
                        if (cargs[0].Trim().ToLower().StartsWith(s + "="))
                        {
                            cargs.Insert(1, cargs[0].Substring(s.Length + 1));
                            cargs[0] = s;
                        }
                }

                //AFTER converting options to commands, we check for internal switches
                foreach (string internaloption in Library.Main.Options.InternalOptions)
                    if (options.ContainsKey(internaloption))
                    {
                        Console.WriteLine(Strings.Program.InternalOptionUsedError, internaloption);
                        return 200;
                    }
                
                if ((options.ContainsKey("parameters-file") && !string.IsNullOrEmpty("parameters-file")) || (options.ContainsKey("parameter-file") && !string.IsNullOrEmpty("parameter-file")))
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

                //After checking for internal options, we set the filter option
                if (!string.IsNullOrEmpty(filter))
                    options["filter"] = filter;

                string command = cargs[0];
                cargs.RemoveAt(0);

                if (!options.ContainsKey("passphrase"))
                    if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PASSPHRASE")))
                        options["passphrase"] = System.Environment.GetEnvironmentVariable("PASSPHRASE");

                if (!options.ContainsKey("ftp-password"))
                    if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("FTP_PASSWORD")))
                        options["ftp-password"] = System.Environment.GetEnvironmentVariable("FTP_PASSWORD");

                if (!options.ContainsKey("ftp-username"))
                    if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("FTP_USERNAME")))
                        options["ftp-username"] = System.Environment.GetEnvironmentVariable("FTP_USERNAME");

                var knownCommands = new Dictionary<string, Func<List<string>, Dictionary<string, string>, int>>(StringComparer.InvariantCultureIgnoreCase);
                knownCommands["purge-signature-cache"] = Commands.PurgeSignatureCache;
                knownCommands["help"] = Commands.Help;                
                knownCommands["list"] = Commands.List;
                knownCommands["list-current-files"] = Commands.ListCurrentFiles;
                knownCommands["list-source-folders"] = Commands.ListSourceFolders;
                knownCommands["list-actual-signature-files"] = Commands.ListActualSignatureFiles;
                knownCommands["collection-status"] = Commands.CollectionStatus;
                knownCommands["delete-all-but-n-full"] = Commands.DeleteAllButNFull;
                knownCommands["delete-all-but-n"] = Commands.DeleteAllButN;
                knownCommands["delete-older-than"] = Commands.DeleteOlderThan;
                knownCommands["cleanup"] = Commands.Cleanup;
                knownCommands["create-folder"] = Commands.CreateFolder;
                knownCommands["find-last-version"] = Commands.FindLastVersion;
                knownCommands["verify"] = Commands.Verify;
                knownCommands["restore"] = Commands.Restore;
                knownCommands["backup"] = Commands.Backup;

                knownCommands["delete-filesets"] = Commands.DeleteFilesets;
                knownCommands["repair"] = Commands.Repair;
                knownCommands["compact"] = Commands.Compact;
                knownCommands["recreate-database"] = Commands.RecreateDatabase;
                knownCommands["create-bugreport-database"] = Commands.CreateBugreportDatabase;
                
                if (knownCommands.ContainsKey(command))
                {
                    return knownCommands[command](cargs, options);
                }
                else
                {
                    Commands.PrintInvalidCommand(cargs);
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
                    Console.Error.WriteLine(Strings.Program.UnhandledException, ex.ToString());

                    while (ex.InnerException != null)
                    {
                        ex = ex.InnerException;
                        Console.Error.WriteLine();
                        Console.Error.WriteLine(Strings.Program.UnhandledInnerException, ex.ToString());
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
                    new Library.Interface.CommandLineArgument("parameters-file", Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.Program.ParametersFileOptionShort, Strings.Program.ParametersFileOptionLong, "", new string[] {"parameter-file"})
                });
            }
        }

        private static bool ReadOptionsFromFile(string filename, ref string filter, IList<string> cargs, Dictionary<string, string> options)
        {
            try
            {
                List<string> fargs = new List<string>(Library.Utility.Utility.ReadFileWithDefaultEncoding(filename).Replace("\r", "").Split(new String[] { "\n" }, StringSplitOptions.RemoveEmptyEntries));

                string newfilter = Duplicati.Library.Utility.FilenameFilter.EncodeAsFilter(Duplicati.Library.Utility.FilenameFilter.ParseCommandLine(fargs, true));

                // If the user specifies parameters-file, all filters must be in the file.
                // Allowing to specify some filters on the command line could result in wrong filter ordering
                if (!string.IsNullOrEmpty(filter) && !string.IsNullOrEmpty(newfilter))
                    throw new Exception(Strings.Program.FiltersCannotBeUsedWithFileError);

                filter = newfilter;

                Dictionary<string, string> opt = Library.Utility.CommandLineParser.ExtractOptions(fargs);
                string newsource = null, newtarget = null;
                foreach (KeyValuePair<String, String> keyvalue in opt)
                {
                    switch (keyvalue.Key.ToLower())
                    {
                        // This replaces any previous value, file parameters take precedence;
                        case "source":
                            newsource = keyvalue.Value;
                            break;
                        case "target":
                            newtarget = keyvalue.Value;
                            break;
                        default:
                            options[keyvalue.Key] = keyvalue.Value;
                            break;
                    }
                }

                // When the action is to backup, allow the source and/or the target 
                // to be specified in the parameters file as --source= and --target=. 
                // Note: this block is faily complex due to the way parameters are handled by the rest of the
                // procedure. It could likely be much simpler and versatile (allow --source and --target on 
                // restore or other actions) with some refactoring of the parameters decision tree
                if (!string.IsNullOrEmpty(newsource) || !string.IsNullOrEmpty(newtarget))
                {
                    bool isrestore = cargs.Count > 0 && cargs[0] == "restore";

                    if (cargs.Count == 1 || cargs.Count == 0)
                    {
                        // if either is empty loading will fail later, so we don't really care.
                        if (!String.IsNullOrEmpty(newsource)) cargs.Add(newsource);
                        if (!String.IsNullOrEmpty(newtarget)) cargs.Add(newtarget);
                    }
                    else
                    {
                        if (isrestore)
                        {
                            if (!String.IsNullOrEmpty(newtarget)) cargs[1] = newtarget;
                            if (!String.IsNullOrEmpty(newsource)) cargs.Insert(1, newsource);
                        }
                        else
                        {
                            if (!String.IsNullOrEmpty(newtarget)) cargs[1] = newsource;
                            if (!String.IsNullOrEmpty(newsource)) cargs.Add(newtarget);
                        }
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(Strings.Program.FailedToParseParametersFileError, filename, e.Message);
                return false;
            }
        }

        private static void PrintOldUsage(bool extended)
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

        	int windowWidth = Math.Max(12, Console.WindowWidth == 0 ? 80 : Console.WindowWidth);
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
                    int len = Math.Min(windowWidth - 2, leadingSpaces.Length + c.Length);
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
