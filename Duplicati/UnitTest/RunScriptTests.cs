//  Copyright (C) 2018, The Duplicati Team
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duplicati.Library.Interface;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class RunScriptTests : BasicSetupHelper
    {
        public override void PrepareSourceData()
        {
            base.PrepareSourceData();

            Directory.CreateDirectory(DATAFOLDER);
            Directory.CreateDirectory(TARGETFOLDER);
        }

        [Test]
        [Category("Border")]
        public void RunScriptBefore()
        {
            PrepareSourceData();

            var blocksize = 10 * 1024;
            var options = TestOptions;
            options["blocksize"] = blocksize.ToString() + "b";
            options["run-script-timeout"] = "5s";

            // We need a small delay as we run very small backups back-to-back
            var PAUSE_TIME = TimeSpan.FromSeconds(3);

            BorderTests.WriteTestFilesToFolder(DATAFOLDER, blocksize, 0);

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, options, null))
            {
                var res = c.Backup(new string[] { DATAFOLDER });
                if (res.ParsedResult != ParsedResultType.Success)
                    throw new Exception("Unexpected result from base backup");
                
                System.Threading.Thread.Sleep(PAUSE_TIME);
                options["run-script-before"] = CreateScript(0);
                res = c.Backup(new string[] { DATAFOLDER });
                if (res.ParsedResult != ParsedResultType.Success)
                    throw new Exception("Unexpected result from backup with return code 0");
                if (res.ExaminedFiles <= 0)
                    throw new Exception("Backup did not examine any files for code 0?");

                System.Threading.Thread.Sleep(PAUSE_TIME);
                options["run-script-before"] = CreateScript(1);
                res = c.Backup(new string[] { DATAFOLDER });
                if (res.ParsedResult != ParsedResultType.Success)
                    throw new Exception("Unexpected result from backup with return code 1");
                if (res.ExaminedFiles > 0)
                    throw new Exception("Backup did examine files for code 1?");

                System.Threading.Thread.Sleep(PAUSE_TIME);
                options["run-script-before"] = CreateScript(2);
                res = c.Backup(new string[] { DATAFOLDER });
                if (res.ParsedResult != ParsedResultType.Warning)
                    throw new Exception("Unexpected result from backup with return code 2");
                if (res.ExaminedFiles <= 0)
                    throw new Exception("Backup did not examine any files for code 2?");

                System.Threading.Thread.Sleep(PAUSE_TIME);
                options["run-script-before"] = CreateScript(3);
                res = c.Backup(new string[] { DATAFOLDER });
                if (res.ParsedResult != ParsedResultType.Warning)
                    throw new Exception("Unexpected result from backup with return code 3");
                if (res.ExaminedFiles > 0)
                    throw new Exception("Backup did examine files for code 3?");

                System.Threading.Thread.Sleep(PAUSE_TIME);
                options["run-script-before"] = CreateScript(4);
                res = c.Backup(new string[] { DATAFOLDER });
                if (res.ParsedResult != ParsedResultType.Error)
                    throw new Exception("Unexpected result from backup with return code 4");
                if (res.ExaminedFiles <= 0)
                    throw new Exception("Backup did not examine any files for code 4?");
                
                System.Threading.Thread.Sleep(PAUSE_TIME);
                options["run-script-before"] = CreateScript(5);
                res = c.Backup(new string[] { DATAFOLDER });
                if (res.ParsedResult != ParsedResultType.Error)
                    throw new Exception("Unexpected result from backup with return code 5");
                if (res.ExaminedFiles > 0)
                    throw new Exception("Backup did examine files for code 5?");


                System.Threading.Thread.Sleep(PAUSE_TIME);
                options["run-script-before"] = CreateScript(2, "TEST WARNING MESSAGE");
                res = c.Backup(new string[] { DATAFOLDER });
                if (res.ParsedResult != ParsedResultType.Warning)
                    throw new Exception("Unexpected result from backup with return code 2");
                if (res.ExaminedFiles <= 0)
                    throw new Exception("Backup did examine files for code 2?");
                if (!res.Warnings.Any(x => x.IndexOf("TEST WARNING MESSAGE", StringComparison.Ordinal) >= 0))
                    throw new Exception("Found no warning message in output for code 2");

                System.Threading.Thread.Sleep(PAUSE_TIME);
                options["run-script-before"] = CreateScript(3, "TEST WARNING MESSAGE");
                res = c.Backup(new string[] { DATAFOLDER });
                if (res.ParsedResult != ParsedResultType.Warning)
                    throw new Exception("Unexpected result from backup with return code 3");
                if (res.ExaminedFiles > 0)
                    throw new Exception("Backup did examine files for code 3?");
                if (!res.Warnings.Any(x => x.IndexOf("TEST WARNING MESSAGE", StringComparison.Ordinal) >= 0))
                    throw new Exception("Found no warning message in output for code 3");

                System.Threading.Thread.Sleep(PAUSE_TIME);
                options["run-script-before"] = CreateScript(4, "TEST ERROR MESSAGE");
                res = c.Backup(new string[] { DATAFOLDER });
                if (res.ParsedResult != ParsedResultType.Error)
                    throw new Exception("Unexpected result from backup with return code 4");
                if (res.ExaminedFiles <= 0)
                    throw new Exception("Backup did examine files for code 4?");
                if (!res.Errors.Any(x => x.IndexOf("TEST ERROR MESSAGE", StringComparison.Ordinal) >= 0))
                    throw new Exception("Found no error message in output for code 4");

                System.Threading.Thread.Sleep(PAUSE_TIME);
                options["run-script-before"] = CreateScript(5, "TEST ERROR MESSAGE");
                res = c.Backup(new string[] { DATAFOLDER });
                if (res.ParsedResult != ParsedResultType.Error)
                    throw new Exception("Unexpected result from backup with return code 5");
                if (res.ExaminedFiles > 0)
                    throw new Exception("Backup did examine files for code 5?");
                if (!res.Errors.Any(x => x.IndexOf("TEST ERROR MESSAGE", StringComparison.Ordinal) >= 0))
                    throw new Exception("Found no error message in output for code 5");

                System.Threading.Thread.Sleep(PAUSE_TIME);
                options["run-script-before"] = CreateScript(0, sleeptime: 10);
                res = c.Backup(new string[] { DATAFOLDER });
                if (res.ParsedResult != ParsedResultType.Warning)
                    throw new Exception("Unexpected result from backup with timeout script");
                if (res.ExaminedFiles <= 0)
                    throw new Exception("Backup did not examine any files after timeout?");
            }
        }


        private string CreateScript(int exitcode, string stderr = null, string stdout = null, int sleeptime = 0)
        {
            var id = Guid.NewGuid().ToString("N").Substring(0, 6);
            if (Library.Utility.Utility.IsClientWindows)
            {
                var commands = new List<string>();
                if (!string.IsNullOrWhiteSpace(stdout))
                    commands.Add($@"echo {stdout}");
                if (!string.IsNullOrWhiteSpace(stderr))
                    commands.Add($@"echo {stderr} 1>&2");
                if (sleeptime > 0)
                    commands.Add($@"sleep {sleeptime}");

                commands.Add($"exit {exitcode}");

                var filename = Path.GetFullPath(Path.Combine(DATAFOLDER, $"run-script-{id}.bat"));
                File.WriteAllLines(filename, commands);

                return filename;
            }
            else
            {
                var commands = new List<string>();
                commands.Add("#!/bin/sh");

                if (!string.IsNullOrWhiteSpace(stdout))
                    commands.Add($@"echo {stdout}");
                if (!string.IsNullOrWhiteSpace(stderr))
                    commands.Add($@"(>&2 echo {stderr})");
                if (sleeptime > 0)
                    commands.Add($@"sleep {sleeptime}");

                commands.Add($"exit {exitcode}");
                var filename = Path.GetFullPath(Path.Combine(DATAFOLDER, $"run-script-{id}.sh"));
                File.WriteAllLines(filename, commands);

                System.Diagnostics.Process.Start("chmod", $@"+x ""{filename}""").WaitForExit();

                return filename;
            }
        }
    }
}
