// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duplicati.Library.Common;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class RunScriptTests : BasicSetupHelper
    {
        [Test]
        [Category("Border")]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void RunScriptAfter(int exitCode)
        {
            const string expectedMessage = "Hello";
            string expectedFile = Path.Combine(this.RESTOREFOLDER, "hello.txt");
            List<string> customCommands = new List<string>
            {
                $"echo {expectedMessage}>\"{expectedFile}\""
            };

            Dictionary<string, string> options = this.TestOptions;
            options["run-script-after"] = CreateScript(exitCode, null, null, 0, customCommands);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});

                switch (exitCode)
                {
                    case 0:
                    case 1:
                        Assert.AreEqual(0, backupResults.Errors.Count());
                        Assert.AreEqual(0, backupResults.Warnings.Count());
                        break;
                    case 2:
                    case 3:
                        Assert.AreEqual(0, backupResults.Errors.Count());
                        Assert.AreEqual(1, backupResults.Warnings.Count());
                        break;
                    default:
                        Assert.AreEqual(1, backupResults.Errors.Count());
                        Assert.AreEqual(0, backupResults.Warnings.Count());
                        break;
                }
                // run script after does not set interrupted flag
                Assert.IsFalse(backupResults.Interrupted);

                string[] targetEntries = Directory.EnumerateFileSystemEntries(this.RESTOREFOLDER).ToArray();
                Assert.AreEqual(1, targetEntries.Length);
                Assert.AreEqual(expectedFile, targetEntries[0]);

                string[] lines = File.ReadAllLines(expectedFile);
                Assert.AreEqual(1, lines.Length);
                Assert.AreEqual(expectedMessage, lines[0]);
            }
        }

        [Test]
        [Category("Border")]
        public void RunScriptBefore()
        {
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
                Assert.AreEqual(0, res.Errors.Count());
                Assert.AreEqual(0, res.Warnings.Count());
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

                foreach (int exitCode in new[] {5, 6, 10, 99})
                {
                    System.Threading.Thread.Sleep(PAUSE_TIME);
                    options["run-script-before"] = CreateScript(exitCode);
                    res = c.Backup(new string[] {DATAFOLDER});
                    if (res.ParsedResult != ParsedResultType.Error)
                        throw new Exception($"Unexpected result from backup with return code {exitCode}");
                    if (res.ExaminedFiles > 0)
                        throw new Exception($"Backup did examine files for code {exitCode}?");
                }

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

        [Test]
        [Category("Border")]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void RunScriptParsedResult(int exitCode)
        {
            string parsedResultFile = Path.Combine(this.RESTOREFOLDER, "result.txt");
            List<string> customCommands = new List<string>();
            if (OperatingSystem.IsWindows())
            {
                customCommands.Add($"echo %DUPLICATI__PARSED_RESULT%>\"{parsedResultFile}\"");
            }
            else
            {
                customCommands.Add($"echo $DUPLICATI__PARSED_RESULT>\"{parsedResultFile}\"");
            }

            Dictionary<string, string> options = this.TestOptions;
            options["run-script-before"] = this.CreateScript(exitCode);
            options["run-script-after"] = this.CreateScript(0, null, null, 0, customCommands);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});

                bool expectBackup;
                string expectedParsedResult;
                switch (exitCode)
                {
                    case 0: // OK, run operation
                        Assert.AreEqual(0, backupResults.Errors.Count());
                        Assert.AreEqual(0, backupResults.Warnings.Count());
                        expectBackup = true;
                        expectedParsedResult = ParsedResultType.Success.ToString();
                        break;
                    case 1: // OK, don't run operation
                        Assert.AreEqual(0, backupResults.Errors.Count());
                        Assert.AreEqual(0, backupResults.Warnings.Count());
                        Assert.IsTrue(backupResults.Interrupted);
                        expectBackup = false;
                        expectedParsedResult = ParsedResultType.Success.ToString();
                        break;
                    case 2: // Warning, run operation
                        Assert.AreEqual(0, backupResults.Errors.Count());
                        Assert.AreEqual(1, backupResults.Warnings.Count());
                        Assert.IsFalse(backupResults.Interrupted);
                        expectBackup = true;
                        expectedParsedResult = ParsedResultType.Warning.ToString();
                        break;
                    case 3: // Warning, don't run operation
                        Assert.AreEqual(0, backupResults.Errors.Count());
                        Assert.AreEqual(1, backupResults.Warnings.Count());
                        Assert.IsTrue(backupResults.Interrupted);
                        expectBackup = false;
                        expectedParsedResult = ParsedResultType.Warning.ToString();
                        break;
                    case 4: // Error, run operation
                        Assert.AreEqual(1, backupResults.Errors.Count());
                        Assert.AreEqual(0, backupResults.Warnings.Count());
                        Assert.IsFalse(backupResults.Interrupted);
                        expectBackup = true;
                        expectedParsedResult = ParsedResultType.Error.ToString();
                        break;
                    default: // Error don't run operation
                        Assert.AreEqual(1, backupResults.Errors.Count());
                        Assert.AreEqual(0, backupResults.Warnings.Count());
                        Assert.IsTrue(backupResults.Interrupted);
                        expectBackup = false;
                        expectedParsedResult = ParsedResultType.Error.ToString();
                        break;
                }

                IEnumerable<string> targetEntries = Directory.EnumerateFileSystemEntries(this.TARGETFOLDER);
                if (expectBackup)
                {
                    // We expect a dblock, dlist, and dindex file.
                    Assert.AreEqual(3, targetEntries.Count());
                }
                else
                {
                    Assert.AreEqual(0, targetEntries.Count());
                }

                string[] lines = File.ReadAllLines(parsedResultFile);
                Assert.AreEqual(1, lines.Length);
                Assert.AreEqual(expectedParsedResult, lines[0]);
            }
        }

        [Test]
        [Category("Border")]
        public void CustomRemoteURL()
        {
            string customTargetFolder = Path.Combine(this.TARGETFOLDER, "destination");
            Directory.CreateDirectory(customTargetFolder);

            List<string> customCommands = new List<string>
            {
                $"echo --remoteurl = \"{customTargetFolder}\""
            };

            Dictionary<string, string> options = this.TestOptions;
            options["run-script-before"] = CreateScript(0, null, null, 0, customCommands);
            using (Controller c = new Library.Main.Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            string[] targetEntries = Directory.EnumerateFileSystemEntries(this.TARGETFOLDER).ToArray();
            Assert.AreEqual(1, targetEntries.Length);
            Assert.AreEqual(customTargetFolder, targetEntries[0]);

            // We expect a dblock, dlist, and dindex file.
            IEnumerable<string> customTargetEntries = Directory.EnumerateFileSystemEntries(customTargetFolder);
            Assert.AreEqual(3, customTargetEntries.Count());
        }

        private string CreateScript(int exitcode, string stderr = null, string stdout = null, int sleeptime = 0, List<string> customCommands = null)
        {
            var id = Guid.NewGuid().ToString("N").Substring(0, 6);
            if (OperatingSystem.IsWindows())
            {
                var commands = customCommands ?? new List<string>();
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

                if (customCommands != null)
                {
                    commands.AddRange(customCommands);
                }

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
