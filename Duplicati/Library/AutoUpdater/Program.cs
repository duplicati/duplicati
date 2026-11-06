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
using System.IO;
using System.Linq;
using Duplicati.Library.Utility;

namespace Duplicati.Library.AutoUpdater
{
    public static class Program
    {
        public static int Main(string[] _args)
        {
            PreloadSettingsLoader.ConfigurePreloadSettings(ref _args, PackageHelper.NamedExecutable.AutoUpdater);

            var args = _args.ToList();
            Duplicati.Library.Utility.CommandLineParser.ExtractOptions(args);

            if (args.Count == 0)
            {
                WriteUsage();
                return 100;
            }
            else if (args.Count != 1)
            {
                Console.WriteLine("Invalid number of arguments, got {0}:{1}{1}{2}{1}{1}", args.Count, Environment.NewLine, string.Join(Environment.NewLine, args));
                return 100;
            }

            UpdaterManager.OnError += (ex) => Console.WriteLine("Error detected: {0}", ex);

            var cmd = args[0].ToLowerInvariant().Trim();
            if ((cmd == "check" || cmd == "download") && UpdaterManager.DISABLE_UPDATE_CHECK)
            {
                Console.WriteLine("Update checks are disabled (default for debug builds, controlled by the environment variable {0})", string.Format(UpdaterManager.SKIPUPDATE_ENVNAME_TEMPLATE, AutoUpdateSettings.AppName));
                return 0;
            }

            switch (cmd)
            {
                case "help":
                    WriteUsage();
                    return 0;
                case "check":
                    {
                        Console.WriteLine($"Checking for updates on channel {AutoUpdateSettings.DefaultUpdateChannel} ...");
                        var update = UpdaterManager.CheckForUpdate();
                        if (update == null)
                            Console.WriteLine("No updates found");
                        else if (update.Version == UpdaterManager.SelfVersion.Version)
                            Console.WriteLine("You are running the latest version: {0}", update.Displayname);
                        else
                            Console.WriteLine("New version is available: {0}", update.Displayname);

                        return 0;
                    }
                case "download":
                    {
                        var update = UpdaterManager.CheckForUpdate();
                        if (update == null || update.Version == UpdaterManager.SelfVersion.Version)
                        {
                            Console.WriteLine("You are running the latest version: {0} ({1})", UpdaterManager.SelfVersion.Displayname, UpdaterManager.SelfVersion.Version);
                            return 0;
                        }

                        var package = update.FindPackage();
                        if (package == null)
                        {
                            Console.WriteLine($"Failed to locate a matching package for this machine, please visit this link and select the correct package: {update.GetGenericUpdatePageUrl()}");
                        }
                        else
                        {
                            var filename = Path.GetFullPath(package.GetFilename());
                            Console.WriteLine("Downloading update \"{0}\" to {1} ...", update.Displayname, filename);

                            long lastpg = 0;
                            UpdaterManager.DownloadUpdate(update, package, filename, f =>
                            {
                                var npg = (long)(f * 100);
                                if (Math.Abs(npg - lastpg) >= 5 || (npg == 100 && lastpg != 100))
                                {
                                    lastpg = npg;
                                    Console.WriteLine("Downloading {0}% ...", npg);
                                }
                            });
                        }

                        Console.WriteLine("New version \"{0}\" downloaded!", update.Displayname);
                        return 0;
                    }
                case "install":
                    return Install();
                default:
                    Console.WriteLine("Unknown command: \"{0}\"", args[0]);
                    Console.WriteLine();
                    Console.WriteLine("Try the command \"{0}\" instead", "help");
                    return 100;
            }
        }

        /// <summary>
        /// Downloads the update and hands it to the update helper, which installs it once this process has exited.
        /// Nothing is restarted; the install is completed and reported the next time the server starts.
        /// </summary>
        /// <returns>The exit code</returns>
        private static int Install()
        {
            var host = HostEnvironment.Detect("AutoUpdater", Environment.GetCommandLineArgs().Skip(1));
            // The CLI only installs, so the helper must not try to restart anything
            host = host with { Supervisor = SupervisorKind.None, ParentPid = -1, ProcessPath = "" };

            var support = UpdateInstallSupport.Evaluate(host);
            if (!support.Supported)
            {
                Console.WriteLine("Automatic installation is not available: {0}", support.Reason);
                return 1;
            }

            var dataFolder = DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ReadWritePermissionSet);
            if (IsServerRunning(dataFolder))
            {
                Console.WriteLine("The server is running from this data folder; install the update from the web interface instead");
                return 1;
            }

            var update = UpdaterManager.CheckForUpdate();
            if (update == null || update.Version == UpdaterManager.SelfVersion.Version)
            {
                Console.WriteLine("You are running the latest version: {0} ({1})", UpdaterManager.SelfVersion.Displayname, UpdaterManager.SelfVersion.Version);
                return 0;
            }

            var package = update.FindPackage();
            if (package == null)
            {
                Console.WriteLine($"Failed to locate a matching package for this machine, please visit this link and select the correct package: {update.GetGenericUpdatePageUrl()}");
                return 1;
            }

            var signedManifest = UpdaterManager.LastUpdateCheckManifest;
            if (string.IsNullOrWhiteSpace(signedManifest))
            {
                Console.WriteLine("The signed manifest is not available; the update cannot be installed");
                return 1;
            }

            var updatesFolder = PendingInstallStore.GetUpdatesFolder(dataFolder);
            if (PendingInstallStore.Read(updatesFolder) != null)
            {
                Console.WriteLine("An update install is already pending; start the server to complete it");
                return 1;
            }

            var filename = Path.Combine(updatesFolder, package.GetFilename());
            Console.WriteLine("Downloading update \"{0}\" to {1} ...", update.Displayname, filename);
            long lastpg = 0;
            if (!UpdaterManager.DownloadUpdate(update, package, filename, f =>
            {
                var npg = (long)(f * 100);
                if (Math.Abs(npg - lastpg) >= 5 || (npg == 100 && lastpg != 100))
                {
                    lastpg = npg;
                    Console.WriteLine("Downloading {0}% ...", npg);
                }
            }))
            {
                Console.WriteLine("Download failed");
                return 1;
            }

            var request = new UpdateLaunchRequest(
                PackagePath: filename,
                PackageLength: package.Length,
                PackageSha256: package.SHA256,
                PackageTypeId: UpdaterManager.PackageTypeId,
                SignedManifest: signedManifest,
                TargetVersion: update.Version ?? "",
                CurrentVersion: UpdaterManager.SelfVersion.Version ?? "",
                Host: host,
                InstallDir: UpdaterManager.INSTALLATIONDIR,
                HelperSourcePath: UpdateInstallSupport.HelperPath,
                WaitPids: host.GetWaitPids(),
                TerminatePids: host.GetTerminatePids(),
                UpdatesFolder: updatesFolder,
                MsiProperties: WindowsMsiProperties.Derive(host, UpdaterManager.INSTALLATIONDIR),
                PortableMode: DataFolderManager.PORTABLE_MODE,
                FinalCheck: () => null);

            try
            {
                var result = UpdateHelperLauncher.LaunchAsync(request, Console.WriteLine, System.Threading.CancellationToken.None).Await();
                Console.WriteLine("The update helper (pid {0}) will install the package when this process exits; the log is in {1}", result.HelperPid, result.Record.HelperDirectory);
                return 0;
            }
            catch (UpdateInstallException ex)
            {
                Console.WriteLine("The update was not installed: {0}", ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// Checks if a server holds the single-instance lock in the data folder
        /// </summary>
        private static bool IsServerRunning(string dataFolder)
        {
            var lockfile = Path.Combine(dataFolder, "lock_v2");
            if (!File.Exists(lockfile))
                return false;

            try
            {
                using (File.Open(lockfile, FileMode.Open, FileAccess.Write, FileShare.None))
                    return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        private static void WriteUsage()
        {
            Console.WriteLine("Usage:{0}\t{1} [CHECK|DOWNLOAD|INSTALL|HELP]", Environment.NewLine, PackageHelper.GetExecutableName(PackageHelper.NamedExecutable.AutoUpdater));
            Console.WriteLine();
            Console.WriteLine("Environment variables:");
            Console.WriteLine();
            Console.WriteLine("{0} - Disables updates completely", string.Format(UpdaterManager.SKIPUPDATE_ENVNAME_TEMPLATE, AutoUpdateSettings.AppName));
            Console.WriteLine("{0} - Use alternate updates urls", string.Format(AutoUpdateSettings.UPDATEURL_ENVNAME_TEMPLATE, AutoUpdateSettings.AppName));
            Console.WriteLine("{0} - Choose different channel than the default {1}, valid settings: {2}", string.Format(AutoUpdateSettings.UPDATECHANNEL_ENVNAME_TEMPLATE, AutoUpdateSettings.AppName), AutoUpdater.AutoUpdateSettings.DefaultUpdateChannel, string.Join(",", Enum.GetNames(typeof(ReleaseType)).Where(x => x != ReleaseType.Unknown.ToString())));
            Console.WriteLine();
            Console.WriteLine("Updates are downloaded from: {0}", string.Join(";", AutoUpdateSettings.URLs));
            Console.WriteLine("Settings and configuration files are placed in: {0}", DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ProbeOnly));
            Console.WriteLine("This version is \"{0}\" ({1}) and is installed in: {2}", UpdaterManager.SelfVersion.Displayname, UpdaterManager.SelfVersion.Version, UpdaterManager.INSTALLATIONDIR);
            Console.WriteLine();
        }
    }
}

