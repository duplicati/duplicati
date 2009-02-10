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
                throw new Exception("Not enough parameters");

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

            if (!options.ContainsKey("ftp_password"))
                if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("FTP_PASSWORD")))
                    options["ftp_password"] = System.Environment.GetEnvironmentVariable("FTP_PASSWORD");

            if (source.Trim().ToLower() == "list")
                Console.WriteLine(string.Join("\r\n", Duplicati.Library.Main.Interface.List(target, options)));
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
    }
}
