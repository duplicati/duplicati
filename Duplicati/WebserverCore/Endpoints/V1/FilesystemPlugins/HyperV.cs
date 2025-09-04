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

public class Hyperv : IFilesystemPlugin
{
    private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<Hyperv>();
    public string RootName => "%HYPERV%";

    public IEnumerable<Dto.TreeNodeDto> GetEntries(string[] pathSegments)
    {
        if (!OperatingSystem.IsWindows())
            return [];

        var hypervUtility = new HyperVUtility();
        if (!hypervUtility.IsHyperVInstalled || !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            return [];

        try
        {
            if (pathSegments.Length == 0)
            {
                hypervUtility.QueryHyperVGuestsInfo(WindowsSnapshot.DEFAULT_WINDOWS_SNAPSHOT_QUERY_PROVIDER);
                if (!hypervUtility.Guests.Any())
                    return [];
                return
                [
                    new TreeNodeDto
                    {
                        id = RootName,
                        text = "Hyper-V Machines",
                        cls = "folder",
                        iconCls = "x-tree-icon-hyperv",
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
                hypervUtility.QueryHyperVGuestsInfo(WindowsSnapshot.DEFAULT_WINDOWS_SNAPSHOT_QUERY_PROVIDER);
                return hypervUtility.Guests.Select(x => new Dto.TreeNodeDto
                {
                    id = string.Join(Path.DirectorySeparatorChar, pathSegments.Append(x.ID.ToString())),
                    text = x.Name,
                    cls = "file",
                    iconCls = "x-tree-icon-hypervmachine",
                    check = false,
                    temporary = false,
                    leaf = true,
                    systemFile = false,
                    hidden = false,
                    symlink = false,
                    fileSize = -1,
                    resolvedpath = null
                }).ToList();
            }

            if (pathSegments.Length == 2)
            {
                hypervUtility.QueryHyperVGuestsInfo(WindowsSnapshot.DEFAULT_WINDOWS_SNAPSHOT_QUERY_PROVIDER);
                var selectedVm = hypervUtility.Guests.FirstOrDefault(x => x.ID.ToString() == pathSegments[1]);
                if (selectedVm is null)
                {
                    return Array.Empty<Dto.TreeNodeDto>();
                }

                return [
                    new Dto.TreeNodeDto
                    {
                        id = string.Join(Path.DirectorySeparatorChar, pathSegments),
                        text = selectedVm.Name,
                        cls = "file",
                        iconCls = "x-tree-icon-hypervmachine",
                        check = false,
                        temporary = false,
                        leaf = true,
                        systemFile = false,
                        hidden = false,
                        symlink = false,
                        fileSize = -1,
                        resolvedpath = null
                    }
                ];
            }
        }
        catch (Exception ex)
        {
            Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "HyperVEnumerationFailed", ex, "Failed to enumerate Hyper-V virtual machines: {0}", ex.Message);
        }

        return [];
    }
}
