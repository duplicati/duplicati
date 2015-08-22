//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
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

namespace Duplicati.CommandLine.RecoveryTool
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            return Duplicati.Library.AutoUpdater.UpdaterManager.RunFromMostRecent(typeof(Program).GetMethod("RealMain"), args);
        }

        private delegate int CommandRunner(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter);

        public static int RealMain(string[] _args)
        {
            try
            {
                var args = new List<string>(_args);
                var tmpparsed = Library.Utility.FilterCollector.ExtractOptions(args);
                var options = tmpparsed.Item1;
                var filter = tmpparsed.Item2;

                if (!options.ContainsKey("auth_password") && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_PASSWORD")))
                    options["auth_password"] = System.Environment.GetEnvironmentVariable("AUTH_PASSWORD");

                if (!options.ContainsKey("auth_username") && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_USERNAME")))
                    options["auth_username"] = System.Environment.GetEnvironmentVariable("AUTH_USERNAME");

                if (options.ContainsKey("tempdir") && !string.IsNullOrEmpty(options["tempdir"]))
                    Library.Utility.TempFolder.SystemTempPath = options["tempdir"];

                var actions = new Dictionary<string, CommandRunner>(StringComparer.InvariantCultureIgnoreCase);
                actions["download"] = Download.Run;
                actions["index"] = Index.Run;
                actions["list"] = List.Run;
                actions["restore"] = Restore.Run;
                actions["help"] = Help.Run;

                CommandRunner command;

                actions.TryGetValue(args.FirstOrDefault(), out command);

                command = command ?? actions["help"];

                return command(args, options, filter);
            }
            catch(Exception ex)
            {
                Console.WriteLine("Program crashed: {0}{1}", Environment.NewLine, ex.ToString());
                return 200;
            }
        }
    }
}
