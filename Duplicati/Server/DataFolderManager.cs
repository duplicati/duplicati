using Duplicati.Library.Common;
using Duplicati.Library.Common.IO;
using System;
using System.Collections.Generic;

namespace Duplicati.Server
{
    partial class DataFolderManager
    {
        private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<DataFolderManager>();

        private readonly string DataFolder;

        public DataFolderManager(Dictionary<string, string> commandlineOptions)
        {
            var serverDataFolder = Environment.GetEnvironmentVariable(Program.DATAFOLDER_ENV_NAME);
            if (commandlineOptions.ContainsKey("server-datafolder"))
                serverDataFolder = commandlineOptions["server-datafolder"];

            if (string.IsNullOrEmpty(serverDataFolder))
            {
#if DEBUG
                //debug mode uses a lock file located in the app folder
                DataFolder = Program.StartupPath;
#else
                bool portableMode = commandlineOptions.ContainsKey("portable-mode") ? Library.Utility.Utility.ParseBool(commandlineOptions["portable-mode"], true) : false;

                if (portableMode)
                {
                    //Portable mode uses a data folder in the application home dir
                    DataFolder = System.IO.Path.Combine(Program.StartupPath, "data");
                    System.IO.Directory.SetCurrentDirectory(Program.StartupPath);
                }
                else
                {
                    //Normal release mode uses the systems "(Local) Application Data" folder
                    // %LOCALAPPDATA% on Windows, ~/.config on Linux

                    // Special handling for Windows:
                    //   - Older versions use %APPDATA%
                    //   - but new versions use %LOCALAPPDATA%
                    //
                    //  If we find a new version, lets use that
                    //    otherwise use the older location
                    //

                    serverDataFolder = System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Library.AutoUpdater.AutoUpdateSettings.AppName);
                    if (Platform.IsClientWindows)
                    {
                        var localappdata = System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Library.AutoUpdater.AutoUpdateSettings.AppName);

                        var prefile = System.IO.Path.Combine(serverDataFolder, "Duplicati-server.sqlite");
                        var curfile = System.IO.Path.Combine(localappdata, "Duplicati-server.sqlite");

                        // If the new file exists, we use that
                        // If the new file does not exist, and the old file exists we use the old
                        // Otherwise we use the new location
                        if (System.IO.File.Exists(curfile) || !System.IO.File.Exists(prefile))
                            serverDataFolder = localappdata;
                    }

                    DataFolder = serverDataFolder;
                }
#endif
            }
            else
                DataFolder = Util.AppendDirSeparator(Environment.ExpandEnvironmentVariables(serverDataFolder).Trim('"'));

        }

        public string GetDataFolder()
        {
            if (Platform.IsClientWindows)
            {
                WindowsFolder.EnsureDataFolderDirectory(DataFolder);
            }
            else
            {
                if (!Library.Common.IO.SystemIO.IO_OS.DirectoryExists(DataFolder))
                {
                    Library.Common.IO.SystemIO.IO_OS.DirectoryCreate(DataFolder);
                }
            }

            return DataFolder;
        }
    }
}
