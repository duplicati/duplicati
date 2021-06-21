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

using NUnit.Framework;

using Duplicati.Library.Common.IO;
using Duplicati.Library.Common;
using System;
using System.Collections.Generic;
using Duplicati.Library.Utility.Win32;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests adjusting process/thread token privileges on Windows computers.
    /// </summary>
    /// <remarks>
    /// These tests should be executed only on Windows computers.
    /// To run tests in Visual Studio, restart Visual Studio in elevated mode
    /// (ie Run As Administrator).
    /// </remarks>
    [Category("Windows-Privilege")]
    public class IOWinPrivilegeTests
    {
        private static class Cmd
        {
            public static string Execute(string cmd, string arguments)
            {
                return Execute(cmd, arguments, false);
            }
            public static string Execute(string cmd, string arguments, bool elevated)
            {
                string outfile = System.IO.Path.GetTempFileName();
                string cmdOut = null;
                try
                {
                    var args = arguments + " > \"" + outfile + "\"";
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(cmd, args)
                    {
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                        UseShellExecute = true
                    };

                    if (elevated)
                    {
                        psi.Verb = "runas";
                    }

                    using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                    {
                        p.WaitForExit();
                    }
                }
                finally
                {
                    cmdOut = System.IO.File.ReadAllText(outfile);
                    System.IO.File.Delete(outfile);
                }
                return cmdOut;
            }
        }
        private class WhoAmIModel
        {
            public UserInfoModel User;
            public PrivilegeInfoModel Privileges;
            public WhoAmIModel(bool elevated)
            {
                string whoamiOutput = Cmd.Execute("cmd.exe", "/c whoami /all", elevated);
                string[] lines = whoamiOutput.Split(Environment.NewLine.ToCharArray(),
                    StringSplitOptions.RemoveEmptyEntries);
                User = new UserInfoModel(lines);
                Privileges = new PrivilegeInfoModel(lines);
            }
        }
        private class UserInfoModel
        {
            public const string MagicString = "USER INFORMATION";

            public string Name;
            public string Sid;

            public UserInfoModel(string[] cmdOutputLines)
            {
                if (cmdOutputLines.Length == 0)
                    throw new ArgumentException(nameof(cmdOutputLines));
                var i = 0;

                while (cmdOutputLines[i].IndexOf(MagicString) == -1)
                {
                    i++;
                    if (i == cmdOutputLines.Length) throw new InvalidOperationException();
                }
                while (cmdOutputLines[i].IndexOf("=") != 0)
                {
                    i++;
                    if (i == cmdOutputLines.Length) throw new InvalidOperationException();
                }
                var usernameLength = cmdOutputLines[i].IndexOf("= =") + 1;
                i++;
                Name = cmdOutputLines[i].Substring(0, usernameLength);
                Sid = cmdOutputLines[i].Substring(usernameLength + 1);
            }
        }
        private class PrivilegeInfoModel
        {
            public const string MagicString = "PRIVILEGES INFORMATION";

            public Dictionary<string, bool> Privileges;

            public PrivilegeInfoModel(string[] cmdOutputLines)
            {
                if (cmdOutputLines.Length == 0)
                    throw new ArgumentException(nameof(cmdOutputLines));
                var i = 0;

                while (cmdOutputLines[i].IndexOf(MagicString) == -1)
                {
                    i++;
                    if (i == cmdOutputLines.Length) throw new InvalidOperationException();
                }
                while (cmdOutputLines[i].IndexOf("=") != 0)
                {
                    i++;
                    if (i == cmdOutputLines.Length) throw new InvalidOperationException();
                }
                var privNameLength = cmdOutputLines[i].IndexOf("= =") + 1;
                var stateStart = cmdOutputLines[i].IndexOf("= =", privNameLength) + 2;
                i++;
                Privileges = new Dictionary<string, bool>();
                while (i < cmdOutputLines.Length && cmdOutputLines[i].Trim() != "")
                {
                    var privName = cmdOutputLines[i].Substring(0, privNameLength).Trim();
                    var privState = cmdOutputLines[i].Substring(stateStart).Trim();
                    var privStateBool = string.Equals("Enabled", privState, StringComparison.OrdinalIgnoreCase);
                    Privileges.Add(privName, privStateBool);
                    i++;
                }
            }
        }


        public IOWinPrivilegeTests()
        {
            if (!Platform.IsClientWindows)
            {
                throw new Exception("This test can run only on Windows systems.");
            }
        }


        [Test]
        public void TestProcessHasBackupPrivilege()
        {
            var whoami = new WhoAmIModel(true);

            Assert.DoesNotThrow(() => { bool privState = whoami.Privileges.Privileges["SeBackupPrivilege"]; },
                "Process token does not have SeBackupPrivilege.");
            Assert.DoesNotThrow(() => { bool privState = whoami.Privileges.Privileges["SeRestorePrivilege"]; },
                "Process token does not have SeRestorePrivilege.");
        }



        [Test]
        public void TestFileOpenReadWithBackupSemantics()
        {
            var content = "This is a temp file from " + typeof(IOTests).FullName +
                " created at " + System.DateTime.Now.ToString("O") +
                System.Environment.NewLine;

            var tempFile = System.IO.Path.GetTempFileName();

            using (var stream = SystemIO.IO_OS.FileOpenWrite(tempFile))
            using (var writer = new System.IO.StreamWriter(stream))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush();

                writer.Close();
                stream.Close();
            }

            var whoami = new WhoAmIModel(false);

            // Add DENY permission
            Cmd.Execute("cmd.exe", $"/c icacls \"{tempFile}\" /deny {whoami.User.Name}:(F)");

            Assert.Throws<System.UnauthorizedAccessException>(() =>
            {
                using (var stream = SystemIO.IO_OS.FileOpenRead(tempFile)) { }
            });

            // Read file with backup privilege
            Privilege.RunWithPrivileges(() =>
            {
                using (var stream = SystemIO.IO_OS.FileOpenRead(tempFile))
                using (var reader = new System.IO.StreamReader(stream))
                {
                    var fileContent = reader.ReadToEnd();
                    Assert.AreEqual(content, fileContent);
                }
            }, Privilege.Backup);

            // Remove DENY permission, so we can delete the file
            Cmd.Execute("cmd.exe", $"/c icacls \"{tempFile}\" /remove:d {whoami.User.Name}");

            System.IO.File.Delete(tempFile);
        }
    }
}
