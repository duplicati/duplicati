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
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.IO;

namespace Duplicati.Server
{
    public static class SpecialFolders
    {
        public static readonly Serializable.TreeNode[] Nodes;
        private static readonly Dictionary<string, string> PathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> DisplayMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string ExpandEnvironmentVariables(string path)
        {
            foreach (var n in Nodes)
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

            if (OperatingSystem.IsWindows())
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
                    // PInvoke to get the download folder
                    TryAdd(lst, SHGetFolder.DownloadFolder, "%MY_DOWNLOADS%", "Downloads");
                }
                catch
                {
                }

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
                var homedir = Environment.GetEnvironmentVariable("HOME");
                if (string.IsNullOrWhiteSpace(homedir))
                    homedir = System.Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                TryAdd(lst, Environment.SpecialFolder.MyDocuments, "%MY_DOCUMENTS%", "Documents");
                TryAdd(lst, Environment.SpecialFolder.MyMusic, "%MY_MUSIC%", "Music");
                TryAdd(lst, Environment.SpecialFolder.MyPictures, "%MY_PICTURES%", "Pictures");
                TryAdd(lst, Environment.SpecialFolder.DesktopDirectory, "%DESKTOP%", "Desktop");
                TryAdd(lst, Environment.GetEnvironmentVariable("HOME"), "%HOME%", "Home");
                TryAdd(lst, Environment.SpecialFolder.UserProfile, "%HOME%", "Home");
                TryAdd(lst, Path.Combine(homedir, "Downloads"), "%MY_DOWNLOADS%", "Downloads");
            }

            Nodes = lst.ToArray();
        }

        public static Dictionary<string, string> GetSourceNames(Serialization.Interface.IBackup backup)
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
            foreach (var x in sources)
                result[x.Key] = x.Value;

            return result;
        }

        /// <summary>
        /// Wrapper for SHGetKnownFolderPath to get the download folder on Windows
        /// </summary>
        [SupportedOSPlatform("windows")]
        private static class SHGetFolder
        {
            /// <summary>
            /// Gets the download folder path
            /// </summary>
            public static string DownloadFolder => GetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"));

            /// <summary>
            /// SHGetKnownFolderPath function to get the folder path
            /// </summary>
            /// <param name="rfid">The folder GUID</param>
            /// <param name="dwFlags">Get folder flags</param>
            /// <param name="hToken">The access token</param>
            /// <param name="ppszPath">The folder path</param>
            /// <returns>The HRESULT error code</returns>
            [DllImport("Shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern int SHGetKnownFolderPath(
                  [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
                  uint dwFlags,
                  IntPtr hToken,
                  out IntPtr ppszPath);

            /// <summary>
            /// Gets the folder path using SHGetKnownFolderPath
            /// </summary>
            /// <param name="folderGuid">The folder GUID</param>
            /// <returns>The folder path</returns>
            private static string GetKnownFolderPath(Guid folderGuid)
            {
                var result = SHGetKnownFolderPath(folderGuid, 0, IntPtr.Zero, out var outPath);
                if (result != 0)
                    Marshal.ThrowExceptionForHR(result); // Throws an exception for the HRESULT error code

                var path = Marshal.PtrToStringUni(outPath);
                Marshal.FreeCoTaskMem(outPath); // Free the memory allocated by SHGetKnownFolderPath
                return path;
            }
        }
    }
}

