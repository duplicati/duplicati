//  Copyright (C) 2011, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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
using System.Collections.Generic;

namespace Duplicati.CommandLine
{
    public static class Commands
    {
        public static int PurgeSignatureCache(List<string> args, Dictionary<string, string> options)
        {
            Library.Main.Interface.PurgeSignatureCache(options); 
            return 0;
        }

        public static int Help(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count < 1)
                Duplicati.CommandLine.Help.PrintUsage("help", options);
            else
                Duplicati.CommandLine.Help.PrintUsage(args[0], options);
                
            return 0;
        }

        public static int List(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);

            Console.WriteLine(string.Join(Environment.NewLine, Duplicati.Library.Main.Interface.List(args[0], options)));
            
            return 0;
        }

        public static int ListCurrentFiles(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
    
            Console.WriteLine(string.Join(Environment.NewLine, new List<string>(Duplicati.Library.Main.Interface.ListCurrentFiles(args[0], options)).ToArray()));
            
            return 0;
        }

        public static int ListSourceFolders(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
            if (options.ContainsKey("fh-dbpath"))
                return PrintNotSupportedWithFhdb("list-source-folders");

            Console.WriteLine(string.Join(Environment.NewLine, Duplicati.Library.Main.Interface.ListSourceFolders(args[0], options) ?? new string[0]));
            
            return 0;
        }

        public static int ListActualSignatureFiles(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
            if (options.ContainsKey("fh-dbpath"))
                return PrintNotSupportedWithFhdb("list-source-folders");

            List<KeyValuePair<Duplicati.Library.Main.RSync.RSyncDir.PatchFileType, string>> files = Duplicati.Library.Main.Interface.ListActualSignatureFiles(args[0], options);

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

            Console.WriteLine();
            Console.WriteLine("* " + Strings.Program.SymlinksHeader + ":");
            foreach (KeyValuePair<Duplicati.Library.Main.RSync.RSyncDir.PatchFileType, string> x in files)
                if (x.Key == Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.Symlink)
                    Console.WriteLine(x.Value);
            
            return 0;
        }

        public static int CollectionStatus(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
            if (options.ContainsKey("fh-dbpath"))
                return PrintNotSupportedWithFhdb("list-source-folders");

            List<Duplicati.Library.Main.ManifestEntry> entries = Duplicati.Library.Main.Interface.ParseFileList(args[0], options);
            
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
            
            return 0;
        }

        public static int DeleteAllButN(List<string> args, Dictionary<string, string> options)
        {
            int n = 0;
            if (!int.TryParse(args[0], out n) || n < 0)
            {
                Console.WriteLine(string.Format(Strings.Program.IntegerParseError, args[0]));
                return 200;
            }
    
            options["delete-all-but-n-full"] = n.ToString();
    
            args.RemoveAt(0);
    
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
    
            Console.WriteLine(Duplicati.Library.Main.Interface.DeleteAllButN(args[0], options));
            
            return 0;
        }

        public static int DeleteAllButNFull(List<string> args, Dictionary<string, string> options)
        {
            int n = 0;
            if (!int.TryParse(args[0], out n) || n < 0)
            {
                Console.WriteLine(string.Format(Strings.Program.IntegerParseError, args[0]));
                return 200;
            }
    
            options["delete-all-but-n-full"] = n.ToString();
    
            args.RemoveAt(0);
    
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
    
            Console.WriteLine(Duplicati.Library.Main.Interface.DeleteAllButNFull(args[0], options));
            
            return 0;
        }

        public static int DeleteOlderThan(List<string> args, Dictionary<string, string> options)
        {
            try
            {
                Duplicati.Library.Utility.Timeparser.ParseTimeSpan(args[0]);
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format(Strings.Program.TimeParseError, args[0], ex.Message));
                return 200;
            }

            options["delete-older-than"] = args[0];

            args.RemoveAt(0);

            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);

            Console.WriteLine(Duplicati.Library.Main.Interface.DeleteOlderThan(args[0], options));
            
            return 0;
        }

        public static int Cleanup(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);

            Console.WriteLine(Duplicati.Library.Main.Interface.Cleanup(args[0], options));
            
            return 0;
        }

        public static int CreateFolder(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);

            Duplicati.Library.Main.Interface.CreateFolder(args[0], options);
            Console.WriteLine(string.Format(Strings.Program.FolderCreatedMessage, args[0]));
            
            return 0;
        }

        public static int FindLastVersion(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);

            List<KeyValuePair<string, DateTime>> results = Duplicati.Library.Main.Interface.FindLastFileVersion(args[0], options);
            Console.WriteLine(Strings.Program.FindLastVersionHeader.Replace("\\t", "\t"));
            foreach(KeyValuePair<string, DateTime> k in results)
                Console.WriteLine(string.Format(Strings.Program.FindLastVersionEntry.Replace("\\t", "\t"), k.Value.Ticks == 0 ? Strings.Program.FileEntryNotFound : k.Value.ToLocalTime().ToString("yyyyMMdd hhmmss"), k.Key));
            
            return 0;
        }

        public static int Verify(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);

            List<KeyValuePair<Duplicati.Library.Main.BackupEntryBase, Exception>> results = Duplicati.Library.Main.Interface.VerifyBackup(args[0], options);

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
            
            return 0;
        }

        public static int Restore(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 2)
                return PrintWrongNumberOfArguments(args, 2);

            Console.WriteLine(Duplicati.Library.Main.Interface.Restore(args[0], args[1].Split(System.IO.Path.PathSeparator), options));
            
            return 0;
        }

        public static int Backup(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 2)
                return PrintWrongNumberOfArguments(args, 2);

            string result = Duplicati.Library.Main.Interface.Backup(args[0].Split(System.IO.Path.PathSeparator), args[1], options);
            Console.WriteLine(result);

            Dictionary<string, string> tmp = ParseDuplicatiOutput(result);
            
            //Interrupted = 50
            if (tmp.ContainsKey("PartialBackup"))
                return 50;

            //Completed with warnings = 2
            if (tmp.ContainsKey("NumberOfWarnings"))
                return 2;

            //Success, but no upload = 1
            if (tmp.ContainsKey("BytesUploaded"))
            {
                long s;
                if (long.TryParse(tmp["BytesUploaded"], out s) && s == 0)
                    return 1;
            }
            
            return 0;
        }
        
        private static Dictionary<string, string> ParseDuplicatiOutput(string output)
        {
            System.Text.RegularExpressions.Regex re = new System.Text.RegularExpressions.Regex("(?<key>[^\\:]+)\\:(?<value>[^\\n]*)", System.Text.RegularExpressions.RegexOptions.Singleline);
            Dictionary<string, string> res = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in re.Matches(output))
                res[m.Groups["key"].Value.Trim()] = m.Groups["value"].Value.Trim();

            return res;
        }
        
        private static int PrintWrongNumberOfArguments(List<string> args, int expected)
        {
            Console.WriteLine(Strings.Program.WrongNumberOfCommandsError_v2, args.Count, expected, args.Count == 0 ? "" : "\"" + string.Join("\", \"", args.ToArray()) + "\"");
            return 200;
        }

        public static int PrintInvalidCommand(List<string> args)
        {
            Console.WriteLine(Strings.Program.InvalidCommandError, args.Count == 0 ? "" : args[0]);
            return 200;
        }

        private static int PrintRequiresFhDb(string command)
        {
            Console.WriteLine(string.Format("The command \"{0}\" requires that the option --{1} is set", command, "fh-dbpath"));
            return 200;
        }

		private static int PrintNotSupportedWithFhdb(string command)
		{
            Console.WriteLine(string.Format("The command \"{0}\" is not supported when the option --{1} is set", command, "fh-dbpath"));
            return 200;
		}
        
        public static int DeleteFilesets(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 2)
                return PrintWrongNumberOfArguments(args, 2);
            if (options.ContainsKey("fh-dbpath"))
                return PrintRequiresFhDb("delete-filesets");
                
            Console.WriteLine(Duplicati.Library.Main.Interface.DeleteFilesets(args[0], args[1], options, null));
            return 0;
        }
        
        public static int Repair(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
            if (options.ContainsKey("fh-dbpath"))
                return PrintRequiresFhDb("repair");
                
            Console.WriteLine(Duplicati.Library.Main.Interface.Cleanup(args[0], options));
            return 0;
        }

        public static int Compact(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
            if (options.ContainsKey("fh-dbpath"))
                return PrintRequiresFhDb("compact");
                
            Console.WriteLine(Duplicati.Library.Main.Interface.CompactBlocks(args[0], options, null));
            return 0;
        }

        public static int RecreateDatabase(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
            if (options.ContainsKey("fh-dbpath"))
                return PrintRequiresFhDb("recreate-database");
                
            Console.WriteLine(Duplicati.Library.Main.Interface.RecreateDatabase(args[0], options, null));
            return 0;
        }

        public static int CreateBugreportDatabase(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
            if (options.ContainsKey("fh-dbpath"))
                return PrintRequiresFhDb("create-bugreport-database");
                
            Console.WriteLine(Duplicati.Library.Main.Interface.CreateLogDatabase(args[0], options, null));
            return 0;
        }
    }
}

