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
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class Filesystem : IRESTMethodGET, IRESTMethodPOST, IRESTMethodDocumented
    {
        public void GET(string key, RequestInfo info)
        {
            var parts = (key ?? "").Split(new char[] { '/' });
            var path = Duplicati.Library.Utility.Uri.UrlDecode((parts.Length == 2 ? parts.FirstOrDefault() : key ?? ""));
            var command = parts.Length == 2 ? parts.Last() : null;
            if (string.IsNullOrEmpty(path))
                path = info.Request.QueryString["path"].Value;
            
            Process(command, path, info);
        }

        private void Process(string command, string path, RequestInfo info)
        {
            if (string.IsNullOrEmpty(path))
            {
                info.ReportClientError("No path parameter was found");
                return;
            }

            bool skipFiles = Library.Utility.Utility.ParseBool(info.Request.QueryString["onlyfolders"].Value, false);
            bool showHidden = Library.Utility.Utility.ParseBool(info.Request.QueryString["showhidden"].Value, false);

            string specialpath = null;
            string specialtoken = null;

            if (path.StartsWith("%"))
            {
                var ix = path.IndexOf("%", 1);
                if (ix > 0)
                {
                    var tk = path.Substring(0, ix + 1);
                    var node = SpecialFolders.Nodes.Where(x => x.id.Equals(tk, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
                    if (node != null)
                    {
                        specialpath = node.resolvedpath;
                        specialtoken = node.id;
                    }
                }
            }

            path = SpecialFolders.ExpandEnvironmentVariables(path);

            if (Duplicati.Library.Utility.Utility.IsClientLinux && !path.StartsWith("/"))
            {
                info.ReportClientError("The path parameter must start with a forward-slash");
                return;
            }

            if (!string.IsNullOrWhiteSpace(command))
            {
                if ("validate".Equals(command, StringComparison.InvariantCultureIgnoreCase))
                {
                    try
                    {
                        if (System.IO.Path.IsPathRooted(path) && (System.IO.Directory.Exists(path) || System.IO.File.Exists(path)))
                        {
                            info.OutputOK();
                            return;
                        }
                    }
                    catch
                    {
                    }

                    info.ReportServerError("File or folder not found");
                    return;
                }
                else
                {
                    info.ReportClientError(string.Format("No such operation found: {0}", command));
                    return;
                }
            }

            try
            {
                if (path != "" && path != "/")
                    path = Duplicati.Library.Utility.Utility.AppendDirSeparator(path);

                IEnumerable<Serializable.TreeNode> res;

                if (!Library.Utility.Utility.IsClientLinux && (path.Equals("/") || path.Equals("")))
                {
                    res = 
                        from di in System.IO.DriveInfo.GetDrives()
                            where di.DriveType == DriveType.Fixed || di.DriveType == DriveType.Network || di.DriveType == DriveType.Removable
                        select new Serializable.TreeNode()
                    {
                        id = di.RootDirectory.FullName,
                        text = 
                            (
                                string.IsNullOrWhiteSpace(di.VolumeLabel) ? 
                                di.RootDirectory.FullName.Replace('\\', ' ') : 
                                di.VolumeLabel + " - " + di.RootDirectory.FullName.Replace('\\', ' ')
                            ) + "(" + di.DriveType + ")",
                        iconCls = "x-tree-icon-drive"
                    };
                }
                else
                {
                    res = ListFolderAsNodes(path, skipFiles, showHidden);                        
                }

                if ((path.Equals("/") || path.Equals("")) && specialtoken == null) 
                {
                    // Prepend special folders
                    res = SpecialFolders.Nodes.Union(res);
                }

                if (specialtoken != null)
                {
                    res = res.Select(x => { 
                        x.resolvedpath = x.id;
                        x.id = specialtoken + x.id.Substring(specialpath.Length);
                        return x; 
                    });
                }

                info.OutputOK(res);
            }
            catch (Exception ex)
            {
                info.ReportClientError("Failed to process the path: " + ex.Message);
            }
        }

        private static IEnumerable<Serializable.TreeNode> ListFolderAsNodes(string entrypath, bool skipFiles, bool showHidden)
        {
            //Helper function for finding out if a folder has sub elements
            Func<string, bool> hasSubElements = (p) => skipFiles ? Directory.EnumerateDirectories(p).Any() : Directory.EnumerateFileSystemEntries(p).Any();

            //Helper function for dealing with exceptions when accessing off-limits folders
            Func<string, bool> isEmptyFolder = (p) =>
            {
                try { return !hasSubElements(p); }
                catch { }
                return true;
            };

            //Helper function for dealing with exceptions when accessing off-limits folders
            Func<string, bool> canAccess = (p) =>
            {
                try { hasSubElements(p); return true; }
                catch { }
                return false;
            }; 

            var systemIO = Library.Utility.Utility.IsClientLinux
                ? (Duplicati.Library.Snapshots.ISystemIO)new Duplicati.Library.Snapshots.SystemIOLinux() 
                : (Duplicati.Library.Snapshots.ISystemIO)new Duplicati.Library.Snapshots.SystemIOWindows();

            foreach (var s in System.IO.Directory.EnumerateFileSystemEntries(entrypath))
            {
                Serializable.TreeNode tn = null;
                try
                {
                    var attr = systemIO.GetFileAttributes(s);
                    var isSymlink = (attr & FileAttributes.ReparsePoint) != 0;
                    var isFolder = (attr & FileAttributes.Directory) != 0;
                    var isFile = !isFolder;
                    var isHidden = (attr & FileAttributes.Hidden) != 0;

                    var accesible = isFile || canAccess(s);
                    var isLeaf = isFile || !accesible || isEmptyFolder(s);

                    var rawid = isFolder ? Library.Utility.Utility.AppendDirSeparator(s) : s;
                    if (skipFiles && !isFolder)
                        continue;

                    if (!showHidden && isHidden)
                        continue;

                    tn = new Serializable.TreeNode()
                    {
                        id = rawid,
                        text = systemIO.PathGetFileName(s),
                        hidden = isHidden,
                        symlink = isSymlink,
                        iconCls = isFolder ? (accesible ? (isSymlink ? "x-tree-icon-symlink" : "x-tree-icon-parent") : "x-tree-icon-locked") : "x-tree-icon-leaf",
                        leaf = isLeaf
                    };
                }
                catch
                {
                }

                if (tn != null)
                    yield return tn;
            }
        }

        public void POST(string key, RequestInfo info)
        {
            Process(key, info.Request.Form["path"].Value, info);
        }

        public string Description { get { return "Enumerates the server filesystem"; } }

        public IEnumerable<KeyValuePair<string, Type>> Types
        {
            get
            {
                return new KeyValuePair<string, Type>[] {
                    new KeyValuePair<string, Type>(HttpServer.Method.Get, typeof(string[])),
                };
            }
        }
    }
}

