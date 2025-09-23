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

using System.Security.Principal;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Snapshots;
using Duplicati.Library.Snapshots.Windows;
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Exceptions;

namespace Duplicati.WebserverCore.Endpoints.V1.FilesystemPlugins;

public class MSSQL : IFilesystemPlugin
{
    private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<MSSQL>();
    public string RootName => "%MSSQL%";

    public IEnumerable<Dto.TreeNodeDto> GetEntries(string[] pathSegments)
    {
        if (!OperatingSystem.IsWindows())
            return [];

        var mssqlUtility = new MSSQLUtility();
        if (!mssqlUtility.IsMSSQLInstalled || !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            return [];

        try
        {
            mssqlUtility.QueryDBsInfo(WindowsSnapshot.DEFAULT_WINDOWS_SNAPSHOT_QUERY_PROVIDER);
            if (pathSegments.Length == 0)
            {
                if (mssqlUtility.DBs.Count == 0)
                    return [];

                return
                [
                    new Dto.TreeNodeDto()
                    {
                        id = RootName,
                        text = "Microsoft SQL Servers",
                        cls = "folder",
                        iconCls = "x-tree-icon-mssql",
                        check = false,
                        temporary = false,
                        leaf = false,
                        systemFile = false,
                        hidden = false,
                        symlink = false,
                        fileSize = -1,
                        resolvedpath = null
                    }
                ];
            }

            if (pathSegments.Length == 1)
            {
                var serverNames = mssqlUtility.DBs.Select(x => x.ID.Replace(Path.DirectorySeparatorChar + x.Name, string.Empty)).Distinct();

                var servers = serverNames.Select(x => new Dto.TreeNodeDto
                {
                    id = string.Join(Path.DirectorySeparatorChar, RootName, x) + Path.DirectorySeparatorChar,
                    text = x,
                    cls = "folder",
                    iconCls = "x-tree-icon-mssql",
                    check = false,
                    temporary = false,
                    leaf = false,
                    systemFile = false,
                    hidden = false,
                    symlink = false,
                    fileSize = -1,
                    resolvedpath = null
                });
                return servers;
            }

            var serverToDatabases = mssqlUtility.DBs.ToLookup(db => db.ID.Replace(Path.DirectorySeparatorChar + db.Name, string.Empty), db => db);
            var selectedServer = serverToDatabases[string.Join(Path.DirectorySeparatorChar, pathSegments.Skip(1))];
            var databases = selectedServer.Select(x => new Dto.TreeNodeDto()
            {
                text = x.Name,
                id = string.Join(Path.DirectorySeparatorChar, pathSegments.Append(x.Name)),
                cls = "file",
                iconCls = "x-tree-icon-mssqldb",
                check = false,
                leaf = true,
                hidden = false,
                systemFile = false,
                temporary = false,
                symlink = false,
                fileSize = -1,
                resolvedpath = null
            }).ToList();
            return databases;
        }
        catch (Exception ex)
        {
            Library.Logging.Log.WriteWarningMessage(LOGTAG, "MSSQLEnumerationFailed", ex, "Failed to enumerate MSSQL databases: {0}", ex.Message);
        }

        return [];
    }
}
