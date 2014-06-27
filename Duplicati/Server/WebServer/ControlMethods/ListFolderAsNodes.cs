//  Copyright (C) 2014, Kenneth Skovhede

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
using System.IO;

namespace Duplicati.Server.WebServer
{
    partial class ControlHandler
    {
        private static IEnumerable<Serializable.TreeNode> ListFolderAsNodes(string entrypath, bool skipFiles)
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
                    //var isSymlink = (attr & FileAttributes.ReparsePoint) != 0;
                    var isFolder = (attr & FileAttributes.Directory) != 0;
                    var isFile = !isFolder;
                    //var isHidden = (attr & FileAttributes.Hidden) != 0;

                    var accesible = isFile || canAccess(s);
                    var isLeaf = isFile || !accesible || isEmptyFolder(s);

                    var rawid = isFolder ? Library.Utility.Utility.AppendDirSeparator(s) : s;
                    if (!skipFiles || isFolder)
                        tn = new Serializable.TreeNode()
                    {
                        id = rawid,
                        text = systemIO.PathGetFileName(s),
                        iconCls = isFolder ? (accesible ? "x-tree-icon-parent" : "x-tree-icon-locked") : "x-tree-icon-leaf",
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
    }
}

