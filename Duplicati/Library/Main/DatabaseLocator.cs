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

namespace Duplicati.Library.Main
{
    public static class DatabaseLocator
    {
        public class BackendEntry
        {
            public string Type;
            public string Server;
            public string Path;
            public string Prefix;
            public string Username;
            //public string Passwordhash;
            public int Port;
            public string Databasepath;
            public string ParameterFile;
        }

        /// <summary>
        /// The filename of the file with database configurations
        /// </summary>
        private const string CONFIG_FILE = "dbconfig.json";

        /// <summary>
        /// Finds a default storage folder, using the operating system specific locations.
        /// The targetfilename is used to detect locations that are used in previous versions.
        /// If the targetfilename is found in an old location, but not the current, the old location is used.
        /// If running with DEBUG defined, the storage folder is placed in the same folder as the executable
        /// </summary>
        /// <param name="targetfilename">The filename to look for</param>
        /// <param name="appName">The name of the application</param>
        /// <returns>The default storage folder</returns>
        public static string GetDefaultStorageFolderWithDebugSupport(string targetfilename, string appName = "Duplicati")
        {
#if DEBUG
            return System.IO.Path.GetDirectoryName(typeof(DatabaseLocator).Assembly.Location) ?? string.Empty;
#else
            return GetDefaultStorageFolder(targetfilename, appName);
#endif
        }

        /// <summary>
        /// Finds a default storage folder, using the operating system specific locations.
        /// The targetfilename is used to detect locations that are used in previous versions.
        /// If the targetfilename is found in an old location, but not the current, the old location is used.
        /// </summary>
        /// <param name="targetfilename">The filename to look for</param>
        /// <param name="appName">The name of the application</param>
        /// <returns>The default storage folder</returns>
        public static string GetDefaultStorageFolder(string targetfilename, string appName = "Duplicati")
        {
            //Normal mode uses the systems "(Local) Application Data" folder
            // %LOCALAPPDATA% on Windows, ~/.config on Linux
            var folder = System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName);

            if (OperatingSystem.IsWindows())
            {
                // Special handling for Windows:
                //   - Older versions use %APPDATA%
                //   - but new versions use %LOCALAPPDATA%
                var newlocation = System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName);

                var prevfile = System.IO.Path.Combine(folder, targetfilename);
                var curfile = System.IO.Path.Combine(newlocation, targetfilename);

                // If the new file exists, we use that
                // If the new file does not exist, and the old file exists we use the old
                // Otherwise we use the new location
                if (System.IO.File.Exists(curfile) || !System.IO.File.Exists(prevfile))
                    folder = newlocation;
            }

            if (OperatingSystem.IsMacOS())
            {
                // Special handling for MacOS:
                //   - Older versions use ~/.config/
                //   - but new versions use ~/Library/Application\ Support/
                var configfolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", appName);

                var prevfile = System.IO.Path.Combine(configfolder, targetfilename);
                var curfile = System.IO.Path.Combine(folder, targetfilename);

                // If the old file exists, and not the new file, we use the old
                // Otherwise we use the new location
                if (!System.IO.File.Exists(curfile) && System.IO.File.Exists(prevfile))
                    folder = configfolder;
            }

            return folder;
        }

        public static string GetDatabasePath(string backend, Options options, bool autoCreate = true, bool anyUsername = false)
        {
            if (options == null)
                options = new Options(new Dictionary<string, string>());

            if (!string.IsNullOrEmpty(options.Dbpath))
                return options.Dbpath;

            var folder = GetDefaultStorageFolderWithDebugSupport(CONFIG_FILE);

            var file = System.IO.Path.Combine(folder, CONFIG_FILE);
            List<BackendEntry> configs;
            if (!System.IO.File.Exists(file))
                configs = new List<BackendEntry>();
            else
                configs = Newtonsoft.Json.JsonConvert.DeserializeObject<List<BackendEntry>>(System.IO.File.ReadAllText(file, System.Text.Encoding.UTF8));

            var uri = new Library.Utility.Uri(backend);
            string server = uri.Host;
            string path = uri.Path;
            string type = uri.Scheme;
            int port = uri.Port;
            string username = uri.Username;
            string prefix = options.Prefix;

            if (username == null || uri.Password == null)
            {
                var sopts = DynamicLoader.BackendLoader.GetSupportedCommands(backend);
                var ropts = new Dictionary<string, string>(options.RawOptions);
                foreach (var k in uri.QueryParameters.AllKeys)
                    ropts[k] = uri.QueryParameters[k];

                if (sopts != null)
                {
                    foreach (var o in sopts)
                    {
                        if (username == null && o.Aliases != null && o.Aliases.Contains("auth-username", StringComparer.OrdinalIgnoreCase) && ropts.ContainsKey(o.Name))
                            username = ropts[o.Name];
                    }

                    foreach (var o in sopts)
                    {
                        if (username == null && o.Name.Equals("auth-username", StringComparison.OrdinalIgnoreCase) && ropts.ContainsKey("auth-username"))
                            username = ropts["auth-username"];
                    }
                }
            }

            //Now find the one that matches :)
            var matches = (from n in configs
                           where
                               n.Type == type &&
                               //n.Passwordhash == password && 
                               n.Username == username &&
                               n.Port == port &&
                               n.Server == server &&
                               n.Path == path &&
                               n.Prefix == prefix
                           select n).ToList();

            if (matches.Count > 1)
                throw new Duplicati.Library.Interface.UserInformationException(string.Format("Multiple sources found for: {0}", backend), "MultipleLocalDatabaseSourcesFound");

            // Re-select
            if (matches.Count == 0 && anyUsername && string.IsNullOrEmpty(username))
            {
                matches = (from n in configs
                           where
                               n.Type == type &&
                               n.Port == port &&
                               n.Server == server &&
                               n.Path == path &&
                               n.Prefix == prefix
                           select n).ToList();

                if (matches.Count > 1)
                    throw new Duplicati.Library.Interface.UserInformationException(String.Format("Multiple sources found for \"{0}\", try supplying --{1}", backend, "auth-username"), "MultipleLocalDatabaseSourcesFound");
            }

            if (matches.Count == 0 && !autoCreate)
                return null;

            if (matches.Count == 0)
            {
                var backupname = options.BackupName;
                if (string.IsNullOrEmpty(backupname) || backupname == Options.DefaultBackupName)
                    backupname = GenerateRandomName();
                else
                {
                    foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                        backupname = backupname.Replace(c.ToString(), "");
                }

                var newpath = System.IO.Path.Combine(folder, backupname + ".sqlite");
                int max_tries = 100;
                while (System.IO.File.Exists(newpath) && max_tries-- > 0)
                    newpath = System.IO.Path.Combine(folder, GenerateRandomName());

                if (System.IO.File.Exists(newpath))
                    throw new Duplicati.Library.Interface.UserInformationException("Unable to find a unique name for the database, please use --dbpath", "CannotCreateRandomName");

                //Create a new one, add it to the list, and save it
                configs.Add(new BackendEntry()
                {
                    Type = type,
                    Server = server,
                    Path = path,
                    Prefix = prefix,
                    Username = username,
                    //Passwordhash = password,
                    Port = port,
                    Databasepath = newpath,
                    ParameterFile = null
                });

                if (!System.IO.Directory.Exists(folder))
                    System.IO.Directory.CreateDirectory(folder);

                var settings = new Newtonsoft.Json.JsonSerializerSettings();
                settings.Formatting = Newtonsoft.Json.Formatting.Indented;
                System.IO.File.WriteAllText(file, Newtonsoft.Json.JsonConvert.SerializeObject(configs, settings), System.Text.Encoding.UTF8);

                return newpath;
            }
            else
            {
                return matches[0].Databasepath;
            }

        }

        public static string GenerateRandomName()
        {
            var rnd = new Random();

            System.Text.StringBuilder backupName = new System.Text.StringBuilder();
            for (var i = 0; i < 10; i++)
                backupName.Append((char)rnd.Next('A', 'Z' + 1));

            return backupName.ToString();
        }

        public static bool IsDatabasePathInUse(string path)
        {
            var file = System.IO.Path.Combine(GetDefaultStorageFolderWithDebugSupport(CONFIG_FILE), CONFIG_FILE);
            if (!System.IO.File.Exists(file))
                return false;

            return Newtonsoft.Json.JsonConvert.DeserializeObject<List<BackendEntry>>(System.IO.File.ReadAllText(file, System.Text.Encoding.UTF8)).Any(x => string.Equals(path, x.Databasepath, Library.Utility.Utility.ClientFilenameStringComparison));
        }
    }
}

