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

namespace Duplicati.Library.AutoUpdater
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            return Duplicati.Library.AutoUpdater.UpdaterManager.RunFromMostRecent(typeof(Program).GetMethod("RealMain"), args, AutoUpdateStrategy.Never);
        }

        public static int RealMain(string[] _args)
        {
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
                        if (string.Equals(Library.Utility.Utility.AppendDirSeparator(selfdir), Library.Utility.Utility.AppendDirSeparator(UpdaterManager.InstalledBaseDir)))
                            versions = versions.Union(new KeyValuePair<string, UpdateInfo>[] { new KeyValuePair<string, UpdateInfo>(selfdir, UpdaterManager.SelfVersion) });
                        Console.WriteLine(string.Join(Environment.NewLine, versions.Select(x => string.Format(" {0} {1} ({2})", (x.Value.Version == UpdaterManager.SelfVersion.Version ? "*" : "-"), x.Value.Displayname, x.Value.Version))));
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
            Console.WriteLine("Usage:{0}\t{1}{2} [LIST|CHECK|INSTALL|HELP]", Environment.NewLine, Duplicati.Library.Utility.Utility.IsMono ? "mono " : "", System.IO.Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location));
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

