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

        public static int List (List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            using (var i = new Library.Main.Controller(args[0], options)) {
                args.RemoveAt(0);
                
                if (args.Count == 1) {
                    long v;
                    if (long.TryParse(args[0], out v)) {
                        if (!options.ContainsKey("version")) {
                            args.RemoveAt(0);
                            args.Add("*");
                            options["version"] = v.ToString();
                        }
                    } else if (args[0].IndexOfAny(new char[] {'*', '?'}) < 0 && !args[0].StartsWith("[")) {
                        try {
                            var t = Library.Utility.Timeparser.ParseTimeInterval(args[0], DateTime.Now, true);
                            args.RemoveAt(0);
                            args.Add("*");
                            options["time"] = t.ToString();
                            
                        } catch {
                        }
                    }
                }
                
                var res = i.List(args);
                if (args.Count == 0) {
                    Console.WriteLine("Listing backup filesets:");
                    foreach (var e in res.Filesets)
                        Console.WriteLine("{0}\t: {1}", e.Key, e.Value);
                } else {
                    if (res.Filesets.Count() == 0) 
                    {
                        Console.WriteLine("No backup times matched");
                    }
                    else if (res.Filesets.Count() == 1)
                    {
                        var f = res.Filesets.First();
                        Console.WriteLine("Listing contents {0} ({1}):", f.Value, f.Key);
                        foreach(var e in res.Files)
                            Console.WriteLine("{0} {1}", e.Path, e.Path.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()) ? "" : "(" + Library.Utility.Utility.FormatSizeString(e.Sizes.First()) + ")");
                    }
                    else
                    {
                        Console.WriteLine("Listing files and versions:");
                        foreach(var e in res.Files)
                        {
                            Console.WriteLine(e.Path);
                            bool created = false;
                            foreach(var nx in res.Filesets.Zip(e.Sizes, (a, b) => new { Index = a.Key, Time = a.Value, Size = b } ))
                            {
                                Console.WriteLine("{0}\t: {1} {2}", nx.Index, nx.Time, nx.Size < 0 ? " - " : Library.Utility.Utility.FormatSizeString(nx.Size));
                                created |= nx.Size >= 0;
                            }
                                
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
        
			using(var i = new Library.Main.Controller(args[0], options))
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

            using(var i = new Duplicati.Library.Main.Controller(args[0], options))
                i.Repair();

            return 0;
        }

        public static int Restore(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count < 1)
                return PrintWrongNumberOfArguments(args, 1);
                
            string backend = args[0];
            args.RemoveAt(0);

            using(var i = new Library.Main.Controller(backend, options))
                i.Restore(args.ToArray(), filter);
            
            return 0;
        }

        public static int Backup(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count < 2)
                return PrintWrongNumberOfArguments(args, 2);
                
            var backend = args[0];
            args.RemoveAt(0);
			var dirs = args.ToArray();

            Library.Main.IBackupResults result;
            using(var i = new Library.Main.Controller(backend, options))
                result = i.Backup(dirs, filter);

			Console.WriteLine("Backup completed");
			Library.Utility.Utility.PrintSerializeObject(result, Console.Out);

            //Interrupted = 50
            if (result.PartialBackup)
                return 50;

            //Completed with warnings = 2
            /*if (result.Warnings > 0)
                return 2;

            //Success, but no upload = 1
            if (result.BytesUploaded == 0)
                return 1;
            */
            return 0;
        }
                
        public static int Compact(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
                
            using(var i = new Library.Main.Controller(args[0], options))
                i.Compact();

            return 0;
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
                
        public static int RecreateDatabase(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
                
            using(var i = new Library.Main.Controller(args[0], options))
                i.RecreateDatabase();

            return 0;
        }

        public static int CreateBugreportDatabase(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
                
            using(var i = new Library.Main.Controller(args[0], options))
                i.CreateLogDatabase();

            return 0;
        }
    }
}

