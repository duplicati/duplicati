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

namespace Duplicati.Library.SharpRSync
{
    /// <summary>
    /// This class represents a console application that can be activated by compiling
    /// this project as a Console Application. It works like the rdiff program on Linux,
    /// but does not support any options
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main entry point for the console application
        /// </summary>
        /// <param name="args">The commandline arguments</param>
        /// <returns>Zero on success, non-zero otherwise</returns>
        public static int Main(string[] _args)
        {
            try
            {
                List<string> args = new List<string>(_args);

#if DEBUG
                bool useRdiff = false;
                if (args.Count > 0 && args[0].Equals("unittest", StringComparison.InvariantCultureIgnoreCase))
                {
                    args.RemoveAt(0);

                    for (int i = 0; i < args.Count; i++)
                        if (args[i].Equals("--use-rdiff", StringComparison.InvariantCultureIgnoreCase))
                        {
                            useRdiff = true;
                            args.RemoveAt(i);
                            i--;
                        }

                    if (args.Count % 2 != 0)
                        args.RemoveAt(args.Count - 1);

                    List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>();
                    for(int i = 0; i < args.Count; i+=2)
                        items.Add(new KeyValuePair<string,string>(args[i], args[i+1]));

                    UnitTest.DoTest(items, useRdiff);
                    return 0;
                }

#endif

                if (args.Count < 3)
                {
                    PrintUsage();
                    return -1;
                }

                if (string.Equals(args[0], "signature", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (args.Count != 3)
                    {
                        PrintUsage();
                        return -1;
                    }

                    Interface.GenerateSignature(args[1], args[2]);
                }
                else if (string.Equals(args[0], "delta", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (args.Count != 4)
                    {
                        PrintUsage();
                        return -1;
                    }

                    Interface.GenerateDelta(args[1], args[2], args[3]);
                }
                else if (string.Equals(args[0], "patch", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (args.Count != 4)
                    {
                        PrintUsage();
                        return -1;
                    }

                    Interface.PatchFile(args[1], args[2], args[3]);
                }
                else
                {
                    PrintUsage();
                    return -1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format(Strings.Program.ErrorOccurred, ex.ToString()));
                return -1;
            }

            return 0;
        }

        private static void PrintUsage()
        {
            Console.WriteLine(string.Format(Strings.Program.UsageMessage, System.IO.Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location)));
        }
    }
}
