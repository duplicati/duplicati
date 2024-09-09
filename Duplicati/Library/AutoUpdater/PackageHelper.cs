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
            Service

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
                _ => throw new ArgumentException($"Named executable not known: {exe}", nameof(exe))
            };

    }
}