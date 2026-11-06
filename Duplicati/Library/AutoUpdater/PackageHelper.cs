// Copyright (C) 2026, The Duplicati Team
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
        /// The environment variable a parent process sets on a child it starts,
        /// so the child reports the top-level executable the user started
        /// instead of its own entry assembly
        /// </summary>
        public const string ENTRY_ASSEMBLY_ENV_NAME = "DUPLICATI_ENTRY_ASSEMBLY";

        /// <summary>
        /// The lazily resolved entry assembly name of the top-level executable
        /// </summary>
        private static readonly Lazy<string> _entryAssemblyName = new Lazy<string>(ResolveEntryAssemblyName);

        /// <summary>
        /// The entry assembly name of the top-level executable the user started, such as
        /// <c>Duplicati.CommandLine</c>, <c>Duplicati.GUI.TrayIcon</c>, <c>Duplicati.Server</c>,
        /// <c>Duplicati.Service</c>, <c>Duplicati.WindowsService</c> or <c>Duplicati.Agent</c>.
        /// A process started by another Duplicati process reports the executable of its parent,
        /// passed via <see cref="ENTRY_ASSEMBLY_ENV_NAME"/>.
        /// </summary>
        public static string EntryAssemblyName => _entryAssemblyName.Value;

        /// <summary>
        /// Resolves the entry assembly name of the top-level executable
        /// </summary>
        /// <returns>The entry assembly name of the top-level executable</returns>
        private static string ResolveEntryAssemblyName()
        {
            var inherited = Environment.GetEnvironmentVariable(ENTRY_ASSEMBLY_ENV_NAME);
            if (!string.IsNullOrWhiteSpace(inherited))
                return inherited.Trim();

            var assemblyName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
            return string.IsNullOrWhiteSpace(assemblyName) ? "Unknown" : assemblyName;
        }

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
            SourceTool,
            /// <summary>
            /// The database tool
            /// </summary>
            DatabaseTool,
            /// <summary>
            /// The configure tool
            /// </summary>
            ConfigureTool,
            /// <summary>
            /// The auto tune tool
            /// </summary>
            AutoTuneTool,
            /// <summary>
            /// The stand-alone update helper
            /// </summary>
            UpdateHelper
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
                NamedExecutable.DatabaseTool => OperatingSystem.IsWindows() ? "Duplicati.CommandLine.DatabaseTool.exe" : "duplicati-database-tool",
                NamedExecutable.ConfigureTool => OperatingSystem.IsWindows() ? "Duplicati.CommandLine.ConfigureTool.exe" : "duplicati-configure",
                NamedExecutable.AutoTuneTool => OperatingSystem.IsWindows() ? "Duplicati.CommandLine.AutoTuneTool.exe" : "duplicati-autotune-tool",
                NamedExecutable.UpdateHelper => OperatingSystem.IsWindows() ? "Duplicati.UpdateHelper.exe" : "duplicati-update-helper",
                _ => throw new ArgumentException($"Named executable not known: {exe}", nameof(exe))
            };

    }
}