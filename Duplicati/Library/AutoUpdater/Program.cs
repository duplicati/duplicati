// Copyright (C) 2024, The Duplicati Team
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
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Common.IO;

namespace Duplicati.Library.AutoUpdater
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            // Ignore webroot during startup verification
            Duplicati.Library.AutoUpdater.UpdaterManager.IgnoreWebrootFolder = true;
            return Duplicati.Library.AutoUpdater.UpdaterManager.RunFromMostRecent(typeof(Program).GetMethod("RealMain"), args, AutoUpdateStrategy.Never);
        }

        public static int RealMain(string[] _args)
        {
            // Enable webroot checks for the verifier
            Duplicati.Library.AutoUpdater.UpdaterManager.IgnoreWebrootFolder = false;

            var args = new List<string>(_args);
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
                case "list":
                    {
                        var versions = UpdaterManager.FindInstalledVersions();
                        var selfdir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                        if (string.Equals(Util.AppendDirSeparator(selfdir), Util.AppendDirSeparator(UpdaterManager.InstalledBaseDir)))
                            versions = versions.Union(new KeyValuePair<string, UpdateInfo>[] { new KeyValuePair<string, UpdateInfo>(selfdir, UpdaterManager.SelfVersion) });
                        Console.WriteLine(string.Join(Environment.NewLine, versions.Select(x => string.Format(" {0} {1} ({2})", (x.Value.Version == UpdaterManager.SelfVersion.Version ? "*" : "-"), x.Value.Displayname, x.Value.Version))));
                        return 0;
                    }

                case "verify":
                    {
                        var versions = UpdaterManager.FindInstalledVersions();
                        var selfdir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                        if (string.Equals(Util.AppendDirSeparator(selfdir), Util.AppendDirSeparator(UpdaterManager.InstalledBaseDir)))
                            versions = versions.Union(new KeyValuePair<string, UpdateInfo>[] { new KeyValuePair<string, UpdateInfo>(selfdir, UpdaterManager.SelfVersion) });

                        Console.WriteLine(string.Join(Environment.NewLine, versions.Select(x => string.Format(" {0} {1} ({2}): {3}", (x.Value.Version == UpdaterManager.SelfVersion.Version ? "*" : "-"), x.Value.Displayname, x.Value.Version, UpdaterManager.VerifyUnpackedFolder(x.Key, x.Value) ? "Valid" : "*** Modified ***"))));
                        return 0;
                    }
                case "check":
                    {
                        var update = UpdaterManager.CheckForUpdate();
                        if (update == null)
                            Console.WriteLine("No updates found");
                        else if (update.Version == UpdaterManager.SelfVersion.Version)
                            Console.WriteLine("You are running the latest version: {0}", update.Displayname);
                        else
                            Console.WriteLine("New version is available: {0}", update.Displayname);

                        return 0;
                    }
                case "install":
                    {
                        var update = UpdaterManager.CheckForUpdate();
                        if (update == null || update.Version == UpdaterManager.SelfVersion.Version)
                        {
                            Console.WriteLine("You are running the latest version: {0} ({1})", UpdaterManager.SelfVersion.Displayname, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
                            return 0;
                        }
                        Console.WriteLine("Downloading update \"{0}\" ...", update.Displayname);

                        long lastpg = 0;
                        UpdaterManager.DownloadAndUnpackUpdate(update, f => {
                            var npg = (long)(f*100);
                            if (Math.Abs(npg - lastpg) >= 5 || (npg == 100 && lastpg != 100))
                            {
                                lastpg = npg;
                                Console.WriteLine("Downloading {0}% ...", npg);
                            }
                        });

                        Console.WriteLine("New version \"{0}\" installed!", update.Displayname);
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
            Console.WriteLine("Usage:{0}\t{1}{2} [LIST|VERIFY|CHECK|INSTALL|HELP]", Environment.NewLine, Duplicati.Library.Utility.Utility.IsMono ? "mono " : "", System.IO.Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location));
            Console.WriteLine();
            Console.WriteLine("Environment variables:");
            Console.WriteLine();
            Console.WriteLine("{0} - Disables updates completely", string.Format(UpdaterManager.SKIPUPDATE_ENVNAME_TEMPLATE, AutoUpdateSettings.AppName));
            Console.WriteLine("{0} - Choose how to handle updates, valid settings: {1}", string.Format(UpdaterManager.UPDATE_STRATEGY_ENVNAME_TEMPLATE, AutoUpdateSettings.AppName), string.Join(", ", Enum.GetNames(typeof(AutoUpdateStrategy))));
            Console.WriteLine("{0} - Use alternate updates urls", string.Format(AutoUpdateSettings.UPDATEURL_ENVNAME_TEMPLATE, AutoUpdateSettings.AppName));
            Console.WriteLine("{0} - Choose different channel than the default {1}, valid settings: {2}", string.Format(AutoUpdateSettings.UPDATECHANNEL_ENVNAME_TEMPLATE, AutoUpdateSettings.AppName), AutoUpdater.AutoUpdateSettings.DefaultUpdateChannel, string.Join(",", Enum.GetNames(typeof(ReleaseType)).Where( x => x != ReleaseType.Unknown.ToString())));
            Console.WriteLine();
            Console.WriteLine("Updates are downloaded from: {0}", string.Join(";", AutoUpdateSettings.URLs));
            Console.WriteLine("Updates are installed in: {0}", UpdaterManager.INSTALLDIR);
            Console.WriteLine("The base version is \"{0}\" ({1}) and is installed in: {2}", UpdaterManager.BaseVersion.Displayname, UpdaterManager.BaseVersion.Version, UpdaterManager.InstalledBaseDir);
            Console.WriteLine("This version is \"{0}\" ({1}) and is installed in: {2}", UpdaterManager.SelfVersion.Displayname, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version, System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));
            Console.WriteLine();
        }
    }
}

