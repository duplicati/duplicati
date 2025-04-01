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
using System.IO;
using System.Linq;

namespace Duplicati.Library.AutoUpdater
{
    public static class Program
    {
        public static int Main(string[] _args)
        {
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
                default:
                    Console.WriteLine("Unknown command: \"{0}\"", args[0]);
                    Console.WriteLine();
                    Console.WriteLine("Try the command \"{0}\" instead", "help");
                    return 100;
            }
        }

        private static void WriteUsage()
        {
            Console.WriteLine("Usage:{0}\t{1} [CHECK|DOWNLOAD|HELP]", Environment.NewLine, PackageHelper.GetExecutableName(PackageHelper.NamedExecutable.AutoUpdater));
            Console.WriteLine();
            Console.WriteLine("Environment variables:");
            Console.WriteLine();
            Console.WriteLine("{0} - Disables updates completely", string.Format(UpdaterManager.SKIPUPDATE_ENVNAME_TEMPLATE, AutoUpdateSettings.AppName));
            Console.WriteLine("{0} - Use alternate updates urls", string.Format(AutoUpdateSettings.UPDATEURL_ENVNAME_TEMPLATE, AutoUpdateSettings.AppName));
            Console.WriteLine("{0} - Choose different channel than the default {1}, valid settings: {2}", string.Format(AutoUpdateSettings.UPDATECHANNEL_ENVNAME_TEMPLATE, AutoUpdateSettings.AppName), AutoUpdater.AutoUpdateSettings.DefaultUpdateChannel, string.Join(",", Enum.GetNames(typeof(ReleaseType)).Where(x => x != ReleaseType.Unknown.ToString())));
            Console.WriteLine();
            Console.WriteLine("Updates are downloaded from: {0}", string.Join(";", AutoUpdateSettings.URLs));
            Console.WriteLine("Settings and configuration files are placed in: {0}", DataFolderManager.DATAFOLDER);
            Console.WriteLine("This version is \"{0}\" ({1}) and is installed in: {2}", UpdaterManager.SelfVersion.Displayname, UpdaterManager.SelfVersion.Version, UpdaterManager.INSTALLATIONDIR);
            Console.WriteLine();
        }
    }
}

