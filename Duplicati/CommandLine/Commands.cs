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
using System.Linq;
using System.Collections.Generic;

namespace Duplicati.CommandLine
{
    public static class Commands
    {
        public static int Help(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count < 1)
                Duplicati.CommandLine.Help.PrintUsage("help", options);
            else
                Duplicati.CommandLine.Help.PrintUsage(args[0], options);
                
            return 0;
        }

        public static int List(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            using(var i = new Library.Main.Controller(args[0], options, new ConsoleOutput(options)))
            {
                args.RemoveAt(0);
                                
                if (args.Count == 1)
                {
                    long v;
                    if (long.TryParse(args[0], out v))
                    {
                        if (!options.ContainsKey("version"))
                        {
                            args.RemoveAt(0);
                            args.Add("*");
                            options["version"] = v.ToString();
                        }
                    }
                    else if (args[0].IndexOfAny(new char[] { '*', '?' }) < 0 && !args[0].StartsWith("["))
                    {
                        try
                        {
                            var t = Library.Utility.Timeparser.ParseTimeInterval(args[0], DateTime.Now, true);
                            args.RemoveAt(0);
                            args.Add("*");
                            options["time"] = t.ToString();
                        }
                        catch
                        {
                        }
                    }
                }
                
                // Prefix all filenames with "*/" so we search all folders
                for(var ix = 0; ix < args.Count; ix++) 
                    if (args[ix].IndexOfAny(new char[] { '*', '?', System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar }) < 0 && !args[ix].StartsWith("["))
                        args[ix] = "*" + System.IO.Path.DirectorySeparatorChar.ToString() + args[ix];
                
                bool controlFiles = Library.Utility.Utility.ParseBoolOption(options, "control-files");
                options.Remove("control-files");
                
                var res = controlFiles ? i.ListControlFiles(args, filter) : i.List(args, filter);
                
                //If there are no files matching, and we are looking for one or more files, 
                // try again with all-versions set
                var isRequestForFiles = 
                    !controlFiles && res.Filesets.Count() != 0 && 
                    (res.Files == null || res.Files.Count() == 0) && 
                    new Library.Utility.FilterExpression(args).Type != Duplicati.Library.Utility.FilterType.Empty;
                
                if (isRequestForFiles && !Library.Utility.Utility.ParseBoolOption(options, "all-versions"))
                {
                    Console.WriteLine("No files matching, looking in all versions");
                    options["all-versions"] = "true";
                    options.Remove("time");
                    options.Remove("version");
                    res = i.List(args, filter);
                }
                
                if (res.Filesets.Count() != 0 && (res.Files == null || res.Files.Count() == 0) && new Library.Utility.FilterExpression(args).Type == Duplicati.Library.Utility.FilterType.Empty)
                {
                    Console.WriteLine("Listing filesets:");
                    
                    foreach(var e in res.Filesets)
                    {
                        if (e.FileCount >= 0)
                            Console.WriteLine("{0}\t: {1} ({2} files, {3})", e.Version, e.Time, e.FileCount, Library.Utility.Utility.FormatSizeString(e.FileSizes));
                        else
                            Console.WriteLine("{0}\t: {1}", e.Version, e.Time);
                    }
                }
                else
                {
                    if (res.Filesets.Count() == 0)
                    {
                        Console.WriteLine("No time or version matched a fileset");
                    }
                    else if (res.Files == null || res.Files.Count() == 0)
                    {
                        Console.WriteLine("Found {0} filesets, but no files matched", res.Filesets.Count());
                    }
                    else if (res.Filesets.Count() == 1)
                    {
                        var f = res.Filesets.First();
                        Console.WriteLine("Listing contents {0} ({1}):", f.Version, f.Time);
                        foreach(var e in res.Files)
                            Console.WriteLine("{0} {1}", e.Path, e.Path.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()) ? "" : "(" + Library.Utility.Utility.FormatSizeString(e.Sizes.First()) + ")");
                    }
                    else
                    {
                        Console.WriteLine("Listing files and versions:");
                        foreach(var e in res.Files)
                        {
                            Console.WriteLine(e.Path);
                            foreach(var nx in res.Filesets.Zip(e.Sizes, (a, b) => new { Index = a.Version, Time = a.Time, Size = b } ))
                                Console.WriteLine("{0}\t: {1} {2}", nx.Index, nx.Time, nx.Size < 0 ? " - " : Library.Utility.Utility.FormatSizeString(nx.Size));
                                
                            Console.WriteLine();
                        }
                        
                    }
                }
            }
            
            return 0;
        }
        
        public static int Delete(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
		{
			var requiredOptions = new string[] { "keep-time", "keep-versions", "version" };
            
			if (!options.Keys.Where(x => requiredOptions.Contains(x, StringComparer.InvariantCultureIgnoreCase)).Any())
			{
				Console.WriteLine(string.Format(Strings.Program.DeleteCommandNeedsOptions, "delete", string.Join(",", requiredOptions))); 
				return 200;
			}
        
			using(var i = new Library.Main.Controller(args[0], options, new ConsoleOutput(options)))
			{
				args.RemoveAt(0);
				var res = i.Delete();
                
				if (res.DeletedSets.Count() == 0)
				{
					Console.WriteLine(Strings.Program.NoFilesetsMatching);
				}
				else
				{
					if (res.Dryrun)
						Console.WriteLine(Strings.Program.WouldDeleteBackups);
					else
						Console.WriteLine(Strings.Program.DeletedBackups);
						
					foreach(var f in res.DeletedSets)
						Console.WriteLine(f);
				}
            }
            
            return 0;
        
        }

        public static int Repair(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);

            using(var i = new Duplicati.Library.Main.Controller(args[0], options, new ConsoleOutput(options)))
                i.Repair();

            return 0;
        }

        public static int Restore(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count < 1)
                return PrintWrongNumberOfArguments(args, 1);
                
            string backend = args[0];
            args.RemoveAt(0);
            
            bool controlFiles = Library.Utility.Utility.ParseBoolOption(options, "control-files");
            options.Remove("control-files");
            
            var output = new ConsoleOutput(options);
            output.MessageEvent(string.Format("Restore started at {0}", DateTime.Now));            

            using(var i = new Library.Main.Controller(backend, options, output))
                if (controlFiles)
                {
                    var res = i.RestoreControlFiles(args.ToArray(), filter);
                    output.MessageEvent("Restore control files completed:");
                    foreach(var s in res.Files)
                        Console.WriteLine(s);
                }
                else
                {
                    output.PhaseChanged += (phase, previousPhase) => {
                        if (phase == Duplicati.Library.Main.OperationPhase.Restore_PreRestoreVerify)
                            output.MessageEvent("Checking remote backup ...");
                        else if (phase == Duplicati.Library.Main.OperationPhase.Restore_ScanForExistingFiles)
                            output.MessageEvent("Checking existing target files ...");
                        /*else if (phase == Duplicati.Library.Main.OperationPhase.Restore_DownloadingRemoteFiles)
                            output.MessageEvent("Downloading remote files ..."); */
                        else if (phase == Duplicati.Library.Main.OperationPhase.Restore_PatchWithLocalBlocks)
                            output.MessageEvent("Updating target files with local data ...");
                        else if (phase == Duplicati.Library.Main.OperationPhase.Restore_PostRestoreVerify)
                            output.MessageEvent("Verifying restored files ...");
                        else if (phase == Duplicati.Library.Main.OperationPhase.Restore_ScanForLocalBlocks)
                            output.MessageEvent("Scanning local files for needed data ...");
                            
                    };
                
                    var res = i.Restore(args.ToArray(), filter);
                    string restorePath;
                    options.TryGetValue("restore-path", out restorePath);
                    
                    output.MessageEvent(string.Format("Restored {0} ({1}) files to {2}", res.FilesRestored, Library.Utility.Utility.FormatSizeString(res.SizeOfRestoredFiles), string.IsNullOrEmpty(restorePath) ? "original path" : restorePath));
                    if (res.FilesRestored > 0)
                    {
                        output.MessageEvent("Did we help save your files? If so, please support Duplicati with a donation.");
                        output.MessageEvent("");
                        output.MessageEvent("Paypal: http://goo.gl/P4XJ6S");
                        output.MessageEvent("Bitcoin: 1ADgoUoE9uN725Ypeh9M9WTKFLJzfWWMBh");
                    }
                    
                    if (output.VerboseOutput)
                        Library.Utility.Utility.PrintSerializeObject(res, Console.Out);
                }
            
            return 0;
        }

        public static int Backup(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count < 2)
                return PrintWrongNumberOfArguments(args, 2);
                
            var backend = args[0];
            args.RemoveAt(0);
            var dirs = args.ToArray();
            var output = new ConsoleOutput(options);
            
            var readyEvent = new System.Threading.ManualResetEvent(false);
            var finishEvent = new System.Threading.ManualResetEvent(false);
            
            output.MessageEvent(string.Format("Backup started at {0}", DateTime.Now));
            
            output.PhaseChanged += (phase, previousPhase) => 
            {
                if (phase == Duplicati.Library.Main.OperationPhase.Backup_ProcessingFiles)
                    readyEvent.Set();
                else if (phase == Duplicati.Library.Main.OperationPhase.Backup_Finalize)
                    finishEvent.Set();
                else if (phase == Duplicati.Library.Main.OperationPhase.Backup_PostBackupVerify)
                    output.MessageEvent("Checking remote backup ...");
                else if (phase == Duplicati.Library.Main.OperationPhase.Backup_Compact)
                    output.MessageEvent("Compacting remote backup ...");
            };

            System.Threading.ThreadStart periodicOutput = () => {
                
                readyEvent.WaitOne();
                if (finishEvent.WaitOne(TimeSpan.FromMilliseconds(10), true))
                    return;
                    
                var last_count = -1L;
                var finished = false;
                
                while (true)
                {
                    Duplicati.Library.Main.OperationPhase phase;
                    float progress;
                    long filesprocessed;
                    long filesizeprocessed;
                    long filecount;
                    long filesize;
                    bool counting;
                    output.OperationProgress.UpdateOverall(out phase, out progress, out filesprocessed, out filesizeprocessed, out filecount, out filesize, out counting);
                    
                    var files = Math.Max(0, filecount - filesprocessed);
                    
                    if (files != 0 || (last_count >= 0 && files != last_count) || finished)
                        output.MessageEvent(string.Format("{0} files need to be examined ({1}){2}", files, Library.Utility.Utility.FormatSizeString(Math.Max(0, filesize - filesizeprocessed)), counting ? " (still counting)" : ""));
                    
                    if (!counting)
                        last_count = files;
                        
                    if (finished)
                        return;

                    finished = finishEvent.WaitOne(TimeSpan.FromSeconds(5), true); 
                }
            };

            Library.Interface.IBackupResults result;
            using(var i = new Library.Main.Controller(backend, options, output))
            {
                System.Threading.Thread reporter = new System.Threading.Thread(periodicOutput);
                try
                {
                    reporter.IsBackground = true;
                    reporter.Start();
                    result = i.Backup(dirs, filter);
                }
                finally
                {
                    finishEvent.Set();
                    readyEvent.Set();
                    
                    if (reporter != null && reporter.IsAlive)
                    {
                        reporter.Abort();
                        reporter.Join(500);
                    }
                }
            }

            output.MessageEvent("Backup completed successfully!");
            
            if (output.VerboseOutput)
            {
                Library.Utility.Utility.PrintSerializeObject(result, Console.Out);
            }
            else
            {
                var parsedStats = result.BackendStatistics as Duplicati.Library.Interface.IParsedBackendStatistics;
                output.MessageEvent(string.Format("Duration of backup: {0}", result.Duration));
                if (parsedStats != null && parsedStats.KnownFileCount > 0)
                {
                    output.MessageEvent(string.Format("Remote files: {0}", parsedStats.KnownFileCount));
                    output.MessageEvent(string.Format("Remote size: {0}", Library.Utility.Utility.FormatSizeString(parsedStats.KnownFileSize)));
                }
                
                output.MessageEvent(string.Format("Files added: {0}", result.AddedFiles));
                output.MessageEvent(string.Format("Files deleted: {0}", result.DeletedFiles));
                output.MessageEvent(string.Format("Files changed: {0}", result.ModifiedFiles));
                
                output.MessageEvent(string.Format("Data uploaded: {0}", Library.Utility.Utility.FormatSizeString(result.BackendStatistics.BytesUploaded)));
                output.MessageEvent(string.Format("Data downloaded: {0}", Library.Utility.Utility.FormatSizeString(result.BackendStatistics.BytesDownloaded)));
            }

            //Interrupted = 50
            if (result.PartialBackup)
                return 50;

            //Completed with warnings = 2
            if (result.Warnings.Count() > 0 || result.Errors.Count() > 0)
                return 2;

            //Success, but no upload = 1
            if (result.BackendStatistics.BytesUploaded == 0)
                return 1;
            
            return 0;
        }
                
        public static int Compact(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
                
            using(var i = new Library.Main.Controller(args[0], options, new ConsoleOutput(options)))
                i.Compact();

            return 0;
        }

        public static int Test(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count != 1 && args.Count != 2)
                return PrintWrongNumberOfArguments(args, 1);
            
            var tests = 1L;
            if (args.Count == 2)
            {
                if (new string[] { "all", "everything" }.Contains(args[1], StringComparer.InvariantCultureIgnoreCase))
                    tests = long.MaxValue;
                else
                    tests = Convert.ToInt64(args[1]);
            }
            
            Library.Interface.ITestResults result;
            using(var i = new Library.Main.Controller(args[0], options, new ConsoleOutput(options)))
                result = i.Test(tests);
            
            var totalFiles = result.Changes.Count();
            if (totalFiles == 0)
            {
                Console.WriteLine("No files examined, is the remote destination is empty?");
            }
            else
            {
                var filtered = from n in result.Changes where n.Value.Count() != 0 select n;
                if (filtered.Count() == 0)
                    Console.WriteLine("Examined {0} files and found no errors", totalFiles);
                else
                {
                    if (Library.Utility.Utility.ParseBoolOption(options, "verbose"))
                    {
                        foreach(var n in result.Changes)
                        {
                            var changecount = n.Value.Count();
                            if (changecount == 0)
                                Console.WriteLine("{0}: No errors", n.Key);
                            else
                            {
                                Console.WriteLine("{0}: {1} errors", n.Key, changecount);
                                foreach(var c in n.Value)
                                    Console.WriteLine("{0}: {1}", c.Key, c.Value);
                                Console.WriteLine();
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Examined {0} files and found errors in the following files:", totalFiles);
                        foreach(var n in filtered)
                            Console.WriteLine(n.Key);
                        Console.WriteLine();
                    }
                }

            }
            return 0;
        }
                
        private static int PrintWrongNumberOfArguments(List<string> args, int expected)
        {
            Console.WriteLine(Strings.Program.WrongNumberOfCommandsError_v2, args.Count, expected, args.Count == 0 ? "" : "\"" + string.Join("\", \"", args.ToArray()) + "\"");
            return 200;
        }

        public static int PrintInvalidCommand(string command, List<string> args)
        {
            Console.WriteLine(Strings.Program.InvalidCommandError, command);
            return 200;
        }

        public static int CreateBugReport(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count != 2)
                return PrintWrongNumberOfArguments(args, 2);
                
            using(var i = new Library.Main.Controller(args[0], options, new ConsoleOutput(options)))
                i.CreateLogDatabase(args[1]);

            return 0;
        }
        
        public static int ListChanges(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count < 1)
                return PrintWrongNumberOfArguments(args, 1);
            
            Library.Interface.IListChangesResults result;
            using(var i = new Library.Main.Controller(args[0], options, new ConsoleOutput(options)))
                if (args.Count == 2)
                    result = i.ListChanges(null, args[1], null, filter);
                else            
                    result = i.ListChanges(args.Count > 1 ? args[1] : null, args.Count > 2 ? args[2] : null, null, filter);
            
            Console.WriteLine("Listing changes from");
            Console.WriteLine("{0}: {1}", result.BaseVersionIndex, result.BaseVersionTimestamp);
            Console.WriteLine(" to");
            Console.WriteLine("{0}: {1}", result.CompareVersionIndex, result.CompareVersionTimestamp);
            Console.WriteLine();
            
            if (result.ChangeDetails != null)
            {
                var added = result.ChangeDetails.Where(x => x.Item1 == Library.Interface.ListChangesChangeType.Added);
                var deleted = result.ChangeDetails.Where(x => x.Item1 == Library.Interface.ListChangesChangeType.Deleted);
                var modified = result.ChangeDetails.Where(x => x.Item1 == Library.Interface.ListChangesChangeType.Modified);
            
                var count = added.Count();
                if (count > 0)
                {
                    Console.WriteLine("{0} added entries:", count);
                    foreach(var n in added)
                        Console.WriteLine(" + {0}", n.Item3);
                    Console.WriteLine();
                }
                count = modified.Count();
                if (count > 0)
                {
                    Console.WriteLine("{0} modified entries:", count);
                    foreach(var n in modified)
                        Console.WriteLine(" ~ {0}", n.Item3);
                    Console.WriteLine();
                }
                count = deleted.Count();
                if (count > 0)
                {
                    Console.WriteLine("{0} deleted entries:", count);
                    foreach(var n in deleted)
                        Console.WriteLine(" - {0}", n.Item3);
                    Console.WriteLine();
                }
            }
            else
            {
                if (result.AddedFolders > 0)
                    Console.WriteLine("{0} added folders", result.AddedFolders);
                if (result.AddedSymlinks > 0)
                    Console.WriteLine("{0} added symlinks", result.AddedSymlinks);
                if (result.AddedFiles > 0)
                    Console.WriteLine("{0} added files", result.AddedFiles);
                if (result.DeletedFolders > 0)
                    Console.WriteLine("{0} deleted folders", result.DeletedFolders);
                if (result.DeletedSymlinks > 0)
                    Console.WriteLine("{0} deleted symlinks", result.DeletedSymlinks);
                if (result.DeletedFiles > 0)
                    Console.WriteLine("{0} deleted files", result.DeletedFiles);
                if (result.ModifiedFolders > 0)
                    Console.WriteLine("{0} modified folders", result.ModifiedFolders);
                if (result.ModifiedSymlinks > 0)
                    Console.WriteLine("{0} modified symlinks", result.ModifiedSymlinks);
                if (result.ModifiedFiles > 0)
                    Console.WriteLine("{0} modified files", result.ModifiedFiles);

                if (result.AddedFolders + result.AddedSymlinks + result.AddedFolders +
                    result.ModifiedFolders + result.ModifiedSymlinks + result.ModifiedFiles +
                    result.DeletedFolders + result.DeletedSymlinks + result.DeletedFiles == 0)
                        Console.WriteLine("No changes found");

                Console.WriteLine();
            }
            
            Console.WriteLine("Previous size: {0}", Library.Utility.Utility.FormatSizeString(result.PreviousSize));
            Console.WriteLine(" Size of added files   {0}", Library.Utility.Utility.FormatSizeString(result.AddedSize));
            Console.WriteLine(" Size of removed files {0}", Library.Utility.Utility.FormatSizeString(result.DeletedSize));
            Console.WriteLine("Current size: {0}", Library.Utility.Utility.FormatSizeString(result.CurrentSize));
            
            return 0;
        }
    }
}

