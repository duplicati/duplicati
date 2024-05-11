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
using Newtonsoft.Json.Serialization;
using Duplicati.Library.Common;

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
        
        public static string GetDatabasePath(string backend, Options options, bool autoCreate = true, bool anyUsername = false)
        {
            if (options == null)
                options = new Options(new Dictionary<string, string>());

            if (!string.IsNullOrEmpty(options.Dbpath))
                return options.Dbpath;

			//Normal mode uses the systems "(Local) Application Data" folder
			// %LOCALAPPDATA% on Windows, ~/.config on Linux

			// Special handling for Windows:
			//   - Older versions use %APPDATA%
			//   - but new versions use %LOCALAPPDATA%
			//
			//  If we find a new version, lets use that
			//    otherwise use the older location
			//

			var folder = System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Duplicati");

			if (Platform.IsClientWindows)
			{
				var newlocation = System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Duplicati");

				var prevfile = System.IO.Path.Combine(folder, "dbconfig.json");
				var curfile = System.IO.Path.Combine(newlocation, "dbconfig.json");

				// If the new file exists, we use that
				// If the new file does not exist, and the old file exists we use the old
				// Otherwise we use the new location
				if (System.IO.File.Exists(curfile) || !System.IO.File.Exists(prevfile))
					folder = newlocation;
			}

            var file = System.IO.Path.Combine(folder, "dbconfig.json");
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
                foreach(var k in uri.QueryParameters.AllKeys)
                    ropts[k] = uri.QueryParameters[k];
                
                if (sopts != null)
                {
                    foreach(var o in sopts)
                    {
                        if (username == null && o.Aliases != null && o.Aliases.Contains("auth-username", StringComparer.OrdinalIgnoreCase) && ropts.ContainsKey(o.Name))
                            username = ropts[o.Name];
                    }
                
                    foreach(var o in sopts)
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
                    foreach(var c in System.IO.Path.GetInvalidFileNameChars())
                        backupname = backupname.Replace(c.ToString(), "");
                }
                
                var newpath = System.IO.Path.Combine(folder, backupname + ".sqlite");
                int max_tries = 100;
                while (System.IO.File.Exists(newpath) && max_tries-- > 0)
                    newpath = System.IO.Path.Combine(folder, GenerateRandomName());
                
                if (System.IO.File.Exists(newpath))
                    throw new Duplicati.Library.Interface.UserInformationException("Unable to find a unique name for the database, please use --dbpath", "CannotCreateRandomName");
                
                //Create a new one, add it to the list, and save it
                configs.Add(new BackendEntry() {
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
            var folder = System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Duplicati");
            if (!System.IO.Directory.Exists(folder))
                return false;

            var file = System.IO.Path.Combine(folder, "dbconfig.json");
            if (!System.IO.File.Exists(file))
                return false;

            return Newtonsoft.Json.JsonConvert.DeserializeObject<List<BackendEntry>>(System.IO.File.ReadAllText(file, System.Text.Encoding.UTF8)).Any(x => string.Equals(path, x.Databasepath, Library.Utility.Utility.ClientFilenameStringComparison));
        }
    }
}

