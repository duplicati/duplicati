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
using Duplicati.Library.Common.IO;
using Duplicati.Library.Common;

namespace Duplicati.Server
{
    public static class SpecialFolders
    {
        public static readonly Serializable.TreeNode[] Nodes;
        private static readonly Dictionary<string, string> PathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> DisplayMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string ExpandEnvironmentVariables(string path)
        {
            foreach(var n in Nodes)
                if (path.StartsWith(n.id, StringComparison.Ordinal))
                    path = path.Replace(n.id, n.resolvedpath);
            return Environment.ExpandEnvironmentVariables(path);
        }

        public static string ExpandEnvironmentVariablesRegexp(string path)
        {
            // The double expand is to use both the special folder names,
            // which are not in the environment, as well as allow expansion
            // of values found in the environment

            return
                Library.Utility.Utility.ExpandEnvironmentVariablesRegexp(path, name =>
                {
                    var res = string.Empty;
                    if (name != null && !PathMap.TryGetValue(name, out res))
                        res = Environment.GetEnvironmentVariable(name);

                    return res;
                });
        }

        public static string TranslateToPath(string str) 
        {
            string res;
            if (PathMap.TryGetValue(str, out res))
                return res;
            
            return null;
        }

        public static string TranslateToDisplayString(string str) 
        {
            string res;
            if (DisplayMap.TryGetValue(str, out res))
                return res;
            
            return null;
        }

        private static void TryAdd(List<Serializable.TreeNode> lst, System.Environment.SpecialFolder folder, string id, string display)
        {
            try
            {
                TryAdd(lst, System.Environment.GetFolderPath(folder), id, display);
            }
            catch
            {
            }
        }

        private static void TryAdd(List<Serializable.TreeNode> lst, string folder, string id, string display)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(folder) && System.IO.Path.IsPathRooted(folder) && System.IO.Directory.Exists(folder))
                {
                    if (!PathMap.ContainsKey(id))
                    {
                        lst.Add(new Serializable.TreeNode()
                        {
                            id = id,
                            text = display,
                            leaf = false,
                            iconCls = "x-tree-icon-special",
                            resolvedpath = folder
                        });

                        PathMap[id] = folder;
                        DisplayMap[id] = display;
                    }
                }
            }
            catch
            {
            }
        }
        
        static SpecialFolders()
        {
            var lst = new List<Serializable.TreeNode>();
            
            if (Platform.IsClientWindows)
            {
                TryAdd(lst, Environment.SpecialFolder.MyDocuments, "%MY_DOCUMENTS%", "My Documents");
                TryAdd(lst, Environment.SpecialFolder.MyMusic, "%MY_MUSIC%", "My Music");
                TryAdd(lst, Environment.SpecialFolder.MyPictures, "%MY_PICTURES%", "My Pictures");
                TryAdd(lst, Environment.SpecialFolder.MyVideos, "%MY_VIDEOS%", "My Videos");
                TryAdd(lst, Environment.SpecialFolder.DesktopDirectory, "%DESKTOP%", "Desktop");
                TryAdd(lst, Environment.SpecialFolder.ApplicationData, "%APPDATA%", "Application Data");
                TryAdd(lst, Environment.SpecialFolder.UserProfile, "%HOME%", "Home");

                try
                {
                    // In case the UserProfile member points to junk
                    TryAdd(lst, System.IO.Path.Combine(Environment.GetEnvironmentVariable("HOMEDRIVE"), Environment.GetEnvironmentVariable("HOMEPATH")), "%HOME%", "Home");
                }
                catch
                {
                }

            }
            else
            {
                TryAdd(lst, Environment.SpecialFolder.MyDocuments, "%MY_DOCUMENTS%", "My Documents");
                TryAdd(lst, Environment.SpecialFolder.MyMusic, "%MY_MUSIC%", "My Music");
                TryAdd(lst, Environment.SpecialFolder.MyPictures, "%MY_PICTURES%", "My Pictures");
                TryAdd(lst, Environment.SpecialFolder.DesktopDirectory, "%DESKTOP%", "Desktop");
                TryAdd(lst, Environment.GetEnvironmentVariable("HOME"), "%HOME%", "Home");
                TryAdd(lst, Environment.SpecialFolder.Personal, "%HOME%", "Home");
            }

            Nodes = lst.ToArray();
        }

        internal static Dictionary<string, string> GetSourceNames(Serialization.Interface.IBackup backup)
        {
            if (backup.Sources == null || backup.Sources.Length == 0)
                return new Dictionary<string, string>();

            var sources = backup.Sources.Distinct().Select(x =>
            {
                var sp = SpecialFolders.TranslateToDisplayString(x);
                if (sp != null)
                    return new KeyValuePair<string, string>(x, sp);

                x = SpecialFolders.ExpandEnvironmentVariables(x);
                try
                {
                    var nx = x;
                    if (nx.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal))
                        nx = nx.Substring(0, nx.Length - 1);
                    var n = SystemIO.IO_OS.PathGetFileName(nx);
                    if (!string.IsNullOrWhiteSpace(n))
                        return new KeyValuePair<string, string>(x, n);
                }
                catch
                {
                }

                if (x.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal) && x.Length > 1)
                    return new KeyValuePair<string, string>(x, x.Substring(0, x.Length - 1).Substring(x.Substring(0, x.Length - 1).LastIndexOf("/", StringComparison.Ordinal) + 1));
                else
                    return new KeyValuePair<string, string>(x, x);

            });

            // Handle duplicates
            var result = new Dictionary<string, string>();
            foreach(var x in sources)
                result[x.Key] = x.Value;

            return result;
        }
    }
}

