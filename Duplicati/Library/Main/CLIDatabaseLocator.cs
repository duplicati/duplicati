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
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Common.IO;

#nullable enable

namespace Duplicati.Library.Main
{
    public static class CLIDatabaseLocator
    {
        /// <summary>
        /// The log tag for this class
        /// </summary>
        public static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(CLIDatabaseLocator));

        /// <summary>
        /// The entry for a backend configuration, used for serialization
        /// </summary>
        public class BackendEntry
        {
            /// <summary>
            /// The type of the backend
            /// </summary>
            public required string Type;
            /// <summary>
            /// The server/hostname of the backend
            /// </summary>
            public required string Server;
            /// <summary>
            /// The path of the backend
            /// </summary>
            public required string Path;
            /// <summary>
            /// The prefix of the backup
            /// </summary>
            public required string Prefix;
            /// <summary>
            /// The username of the backend
            /// </summary>
            public required string? Username;
            /// <summary>
            /// The port of the backend
            /// </summary>
            public required int Port;
            /// <summary>
            /// The path to the database
            /// </summary>
            public required string Databasepath;
            /// <summary>
            /// The path to the parameter file
            /// </summary>
            public required string? ParameterFile;
        }

        /// <summary>
        /// The filename of the file with database configurations
        /// </summary>
        private const string CONFIG_FILE = "dbconfig.json";

        /// <summary>
        /// Gets the database path for a given backend url, using the JSON configuration file lookup.
        /// If <paramref name="autoCreate"/> is <c>true</c>, a new database is created if none is found.
        /// Otherwise, <c>null</c> is returned if no database is found.
        /// </summary>
        /// <param name="backend">The backend url</param>
        /// <param name="options">The options to use</param>
        /// <param name="autoCreate">If <c>true</c>, a new database is created if none is found</param>
        /// <param name="anyUsername">If <c>true</c>, any username is accepted</param>
        /// <returns>The database path or null</returns>
        public static string? GetDatabasePathForCLI(string backend, Options? options, bool autoCreate = true, bool anyUsername = false)
        {
            options ??= new Options(new Dictionary<string, string>());

            if (!string.IsNullOrEmpty(options.Dbpath))
                return options.Dbpath;

            // Ideally, this should use DataFolderManager.DATAFOLDER, but we cannot due to backwards compatibility
            var folder = AutoUpdater.DataFolderLocator.GetDefaultStorageFolder(CONFIG_FILE, true);
            // Emit a warning if the database is stored in the Windows folder
            if (Util.IsPathUnderWindowsFolder(folder))
                Logging.Log.WriteWarningMessage(LOGTAG, "DatabaseInWindowsFolder", null, "The database config is stored in the Windows folder, this is not recommended as it will be deleted on Windows upgrades.");


            var file = System.IO.Path.Combine(folder, CONFIG_FILE);
            List<BackendEntry> configs;
            if (!System.IO.File.Exists(file))
                configs = new List<BackendEntry>();
            else
                configs = Newtonsoft.Json.JsonConvert.DeserializeObject<List<BackendEntry>>(System.IO.File.ReadAllText(file, System.Text.Encoding.UTF8))
                    ?? new List<BackendEntry>();

            var uri = new Library.Utility.Uri(backend);
            var server = uri.Host;
            var path = uri.Path;
            var type = uri.Scheme;
            var port = uri.Port;
            var username = uri.Username;
            var prefix = options.Prefix;

            if (username == null || uri.Password == null)
            {
                var sopts = DynamicLoader.BackendLoader.GetSupportedCommands(backend);
                var ropts = new Dictionary<string, string?>(options.RawOptions);
                foreach (var k in uri.QueryParameters.AllKeys)
                    if (k != null)
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

        /// <summary>
        /// Generates a random name for a database
        /// </summary>
        /// <returns>A random name</returns>
        public static string GenerateRandomName()
        {
            var rnd = new Random();

            var backupName = new System.Text.StringBuilder();
            for (var i = 0; i < 10; i++)
                backupName.Append((char)rnd.Next('A', 'Z' + 1));

            return backupName.ToString();
        }

        /// <summary>
        /// Checks if a database path is in use by any backup.
        /// If the file does not exist, it is assumed to be free.
        /// If the file exists, it is checked if it is in use by any backup.
        /// </summary>
        /// <param name="path">The path to check</param>
        /// <returns><c>true</c> if the path is in use, <c>false</c> otherwise</returns>
        public static bool IsDatabasePathInUse(string path)
        {
            // Ideally, this should use DataFolderManager.DATAFOLDER, but we cannot due to backwards compatibility
            var file = System.IO.Path.Combine(AutoUpdater.DataFolderLocator.GetDefaultStorageFolder(CONFIG_FILE, false), CONFIG_FILE);
            if (!System.IO.File.Exists(file))
                return false;

            return (Newtonsoft.Json.JsonConvert.DeserializeObject<List<BackendEntry>>(System.IO.File.ReadAllText(file, System.Text.Encoding.UTF8)) ?? [])
                .Any(x => string.Equals(path, x.Databasepath, Library.Utility.Utility.ClientFilenameStringComparison));
        }
    }
}

