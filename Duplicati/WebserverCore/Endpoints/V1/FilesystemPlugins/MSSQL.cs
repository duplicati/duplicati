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
            if (pathSegments == null || pathSegments.Length == 0)
            {
                mssqlUtility.QueryDBsInfo(WindowsSnapshot.DEFAULT_WINDOWS_SNAPSHOT_QUERY_PROVIDER);
                if (!mssqlUtility.DBs.Any())
                    return [];

                return [new Dto.TreeNodeDto()
                {
                    id = Util.AppendDirSeparator(RootName),
                    text = "Microsoft SQL Databases",
                    cls = "mssql",
                    iconCls = "x-tree-icon-mssql",
                    check = false,
                    temporary = false,
                    leaf = false,
                    systemFile = false,
                    hidden = false,
                    symlink = false,
                    fileSize = -1,
                    resolvedpath = null
                }];
            }
            else if (pathSegments.Length == 1)
            {
                mssqlUtility.QueryDBsInfo(WindowsSnapshot.DEFAULT_WINDOWS_SNAPSHOT_QUERY_PROVIDER);

                return mssqlUtility.DBs.Select(x => new Dto.TreeNodeDto()
                {
                    id = Util.AppendDirSeparator(string.Join(Path.DirectorySeparatorChar, pathSegments.Append(x.ID.ToString()))),
                    text = x.Name,
                    cls = "hyperv",
                    iconCls = "x-tree-icon-mssql",
                    check = false,
                    temporary = false,
                    leaf = false,
                    systemFile = false,
                    hidden = false,
                    symlink = false,
                    fileSize = -1,
                    resolvedpath = null
                }).ToList();
            }
            else
            {
                var databaseId = pathSegments[1];
                var foundDBs = mssqlUtility.DBs.FindAll(x => x.ID.Equals(databaseId, StringComparison.OrdinalIgnoreCase));

                if (foundDBs.Count != 1)
                    throw new NotFoundException($"Cannot find DB with ID {databaseId}.");

                return foundDBs[0].DataPaths.Select(x => new Dto.TreeNodeDto()
                {
                    text = x,
                    id = string.Join(Path.DirectorySeparatorChar, pathSegments.Append(x)),
                    cls = "folder",
                    iconCls = "x-tree-icon-mssqldb",
                    check = false,
                    leaf = true,
                    hidden = false,
                    systemFile = false,
                    temporary = false,
                    symlink = false,
                    fileSize = -1,
                    resolvedpath = x
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            Library.Logging.Log.WriteWarningMessage(LOGTAG, "MSSQLEnumerationFailed", ex, "Failed to enumerate MSSQL databases: {0}", ex.Message);
        }

        return [];
    }
}