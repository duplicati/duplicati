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
        public static int Help(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count < 1)
                Duplicati.CommandLine.Help.PrintUsage("help", options);
            else
                Duplicati.CommandLine.Help.PrintUsage(args[0], options);
                
            return 0;
        }

        public static int List (List<string> args, Dictionary<string, string> options)
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

        public static int DeleteAllButN(List<string> args, Dictionary<string, string> options)
        {
            int n = 0;
            if (!int.TryParse(args[0], out n) || n < 0)
            {
                Console.WriteLine(string.Format(Strings.Program.IntegerParseError, args[0]));
                return 200;
            }
    
            options["delete-all-but-n"] = n.ToString();
    
            args.RemoveAt(0);
    
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
                
            using(var i = new Library.Main.Controller(args[0], options))
                i.Delete();
    
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
                
            using(var i = new Duplicati.Library.Main.Controller(args[0], options))
                i.Delete();
            
            return 0;
        }

        public static int Repair(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);

            using(var i = new Duplicati.Library.Main.Controller(args[0], options))
                i.Repair();

            return 0;
        }

        public static int Restore(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 2)
                return PrintWrongNumberOfArguments(args, 2);

            using(var i = new Library.Main.Controller(args[0], options))
                i.Restore(args[1].Split(System.IO.Path.PathSeparator));
            
            return 0;
        }

        public static int Backup(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 2)
                return PrintWrongNumberOfArguments(args, 2);

            string result;
            using(var i = new Library.Main.Controller(args[1], options))
                result = i.Backup(args[0].Split(System.IO.Path.PathSeparator));

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
        
        public static int DeleteFilesets(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 2)
                return PrintWrongNumberOfArguments(args, 2);
                
            using(var i = new Library.Main.Controller(args[0], options))
                i.DeleteFilesets(args[1]);

            return 0;
        }
        
        public static int Compact(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
                
            using(var i = new Library.Main.Controller(args[0], options))
                i.CompactBlocks();

            return 0;
        }

        public static int RecreateDatabase(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
                
            using(var i = new Library.Main.Controller(args[0], options))
                i.RecreateDatabase();

            return 0;
        }

        public static int CreateBugreportDatabase(List<string> args, Dictionary<string, string> options)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(args, 1);
                
            using(var i = new Library.Main.Controller(args[0], options))
                i.CreateLogDatabase();

            return 0;
        }
    }
}

