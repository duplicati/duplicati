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

namespace Duplicati.Library.AutoUpdater
{
    /// <summary>
    /// Helper method for various packaging related settings
    /// </summary>
    public static class PackageHelper
    {
        /// <summary>
        /// The named executables that are installed via packages
        /// </summary>
        public enum NamedExecutable
        {
            /// <summary>
            /// The primary executable, with an embedded GUI
            /// </summary>
            TrayIcon,
            /// <summary>
            /// The primary commandline application
            /// </summary>
            CommandLine,
            /// <summary>
            /// The autoupdater
            /// </summary>
            AutoUpdater,
            /// <summary>
            /// The server runner
            /// </summary>
            Server,
            /// <summary>
            /// The windows service helper wrapping the server
            /// </summary>
            WindowsService,
            /// <summary>
            /// The backend manipulation tool
            /// </summary>
            BackendTool,
            /// <summary>
            /// The recovery tool
            /// </summary>
            RecoveryTool,
            /// <summary>
            /// The backend testing tool
            /// </summary>
            BackendTester,
            /// <summary>
            /// The SharpAESCrypt tool
            /// </summary>
            SharpAESCrypt,
            /// <summary>
            /// The snapshot tool
            /// </summary>
            Snapshots,
            /// <summary>
            /// The server utility
            /// </summary>
            ServerUtil,
            /// <summary>
            /// The service wrapping the server
            /// </summary>
            Service,
            /// <summary>
            /// The remote managed agent
            /// </summary>
            Agent,
            /// <summary>
            /// The secret tool
            /// </summary>
            SecretTool,
            /// <summary>
            /// The sync tool
            /// </summary>
            SyncTool,
            /// <summary>
            /// The source tool
            /// </summary>
            SourceTool

        }

        /// <summary>
        /// Returns the operating system mappings of project executables
        /// </summary>
        /// <param name="exe">The executable to get then name for</param>
        /// <returns>The name of the executable on the current operating system</returns>
        /// <remarks>Note that the values here mirror the values in the ReleaseBuilder tool, so changes should be coordinated between the two</remarks>
        public static string GetExecutableName(NamedExecutable exe)
            => exe switch
            {
                NamedExecutable.TrayIcon => OperatingSystem.IsWindows() ? "Duplicati.GUI.TrayIcon.exe" : "duplicati",
                NamedExecutable.CommandLine => OperatingSystem.IsWindows() ? "Duplicati.CommandLine.exe" : "duplicati-cli",
                NamedExecutable.AutoUpdater => OperatingSystem.IsWindows() ? "Duplicati.CommandLine.AutoUpdater.exe" : "duplicati-autoupdater",
                NamedExecutable.Server => OperatingSystem.IsWindows() ? "Duplicati.Server.exe" : "duplicati-server",
                NamedExecutable.WindowsService => "Duplicati.WindowsServer.exe",
                NamedExecutable.BackendTool => OperatingSystem.IsWindows() ? "Duplicati.CommandLine.BackendTool.exe" : "duplicati-backend-tool",
                NamedExecutable.RecoveryTool => OperatingSystem.IsWindows() ? "Duplicati.CommandLine.RecoveryTool.exe" : "duplicati-recovery-tool",
                NamedExecutable.BackendTester => OperatingSystem.IsWindows() ? "Duplicati.CommandLine.BackendTester.exe" : "duplicati-backend-tester",
                NamedExecutable.SharpAESCrypt => OperatingSystem.IsWindows() ? "Duplicati.CommandLine.SharpAESCrypt.exe" : "duplicati-aescrypt",
                NamedExecutable.Snapshots => OperatingSystem.IsWindows() ? "Duplicati.CommandLine.Snapshots.exe" : "duplicati-snapshots",
                NamedExecutable.ServerUtil => OperatingSystem.IsWindows() ? "Duplicati.CommandLine.ServerUtil.exe" : "duplicati-server-util",
                NamedExecutable.Service => OperatingSystem.IsWindows() ? "Duplicati.Service.exe" : "duplicati-service",
                NamedExecutable.Agent => OperatingSystem.IsWindows() ? "Duplicati.Agent.exe" : "duplicati-agent",
                NamedExecutable.SecretTool => OperatingSystem.IsWindows() ? "Duplicati.CommandLine.SecretTool.exe" : "duplicati-secret-tool",
                NamedExecutable.SyncTool => OperatingSystem.IsWindows() ? "Duplicati.CommandLine.SyncTool.exe" : "duplicati-sync-tool",
                NamedExecutable.SourceTool => OperatingSystem.IsWindows() ? "Duplicati.CommandLine.Sourcetool.exe" : "duplicati-source-tool",
                _ => throw new ArgumentException($"Named executable not known: {exe}", nameof(exe))
            };

    }
}