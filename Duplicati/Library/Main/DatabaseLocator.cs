//  Copyright (C) 2011, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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
using Newtonsoft.Json.Serialization;

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
            if (!string.IsNullOrEmpty(options.Dbpath))
                return options.Dbpath;
         
            var folder = System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Duplicati");
            if (!System.IO.Directory.Exists(folder))
                System.IO.Directory.CreateDirectory(folder);
                
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
            string password = uri.Password;
            string prefix = options.Prefix;
            
            if (username == null || password == null)
            {
                var sopts = DynamicLoader.BackendLoader.GetSupportedCommands(backend);
                var ropts = new Dictionary<string, string>(options.RawOptions);
                foreach(var k in uri.QueryParameters.AllKeys)
                    ropts[k] = uri.QueryParameters[k];
                
                foreach(var o in sopts)
                {
                    if (username == null && o.Aliases != null && o.Aliases.Contains("auth-username", StringComparer.InvariantCultureIgnoreCase) && ropts.ContainsKey(o.Name))
                        username = ropts[o.Name];
                    if (password == null && o.Aliases != null && o.Aliases.Contains("auth-password", StringComparer.InvariantCultureIgnoreCase) && ropts.ContainsKey(o.Name))
                        password = ropts[o.Name];
                }
                
                foreach(var o in sopts)
                {
                    if (username == null && o.Name.Equals("auth-username", StringComparison.InvariantCultureIgnoreCase) && ropts.ContainsKey("auth-username"))
                        username = ropts["auth-username"];
                    if (password == null && o.Name.Equals("auth-password", StringComparison.InvariantCultureIgnoreCase) && ropts.ContainsKey("auth-password"))
                        password = ropts["auth-password"];
                }
            }
            
            if (password != null)
                password = Library.Utility.Utility.ByteArrayAsHexString(System.Security.Cryptography.SHA256.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(password + "!" + uri.Scheme + "!" + uri.HostAndPath)));
                
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
                throw new Exception(string.Format("Multiple sources found for: {0}", backend));
            
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
                    throw new Exception(String.Format("Multiple sources found for \"{0}\", try supplying --{1}", backend, "auth-username"));
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
                    throw new Exception("Unable to find a unique name for the database, please use --fh-dbpath");
                
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
            var backupname = "";
            var rnd = new Random();
            for(var i = 0; i < 10; i++)
                backupname += (char)rnd.Next('A', 'Z' + 1);
                
            return backupname;
        }
    }
}

