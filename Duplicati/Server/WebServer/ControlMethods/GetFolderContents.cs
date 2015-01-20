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
using System.IO;

namespace Duplicati.Server.WebServer
{
    partial class ControlHandler
    {
        private void GetFolderContents(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
        {
            HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
            if (input["path"] == null || input["path"].Value == null)
            {
                ReportError(response, bw, "The path parameter was not set");
                return;
            }

            bool skipFiles = Library.Utility.Utility.ParseBool(input["onlyfolders"].Value, false);

            var path = input["path"].Value;
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
                ReportError(response, bw, "The path parameter must start with a forward-slash");
                return;
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
                        text = di.RootDirectory.FullName.Replace('\\', ' ') + "(" + di.DriveType + ")",
                        iconCls = "x-tree-icon-drive"
                    };
                }
                else
                {
                    res = ListFolderAsNodes(path, skipFiles);                        
                }

                if ((path.Equals("/") || path.Equals("")) && specialtoken == null) 
                {
                    // Prepend special folders
                    res = SpecialFolders.Nodes.Union(res);
                }

                if (specialtoken != null)
                {
                    res = from n in res
                        select new Serializable.TreeNode() {
                        id = specialtoken + n.id.Substring(specialpath.Length),
                        text = n.text,
                        iconCls = n.iconCls,
                        leaf = n.leaf,
                        resolvedpath = n.id
                    };
                }

                bw.OutputOK(res);
            }
            catch (Exception ex)
            {
                ReportError(response, bw, "Failed to process the path: " + ex.Message);
            }
        }       
    }
}

