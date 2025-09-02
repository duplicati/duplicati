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
#if DEBUG
using Duplicati.Library.Common.IO;
using Duplicati.WebserverCore.Exceptions;

namespace Duplicati.WebserverCore.Endpoints.V1.FilesystemPlugins;

public class TestPlugin : IFilesystemPlugin
{
    private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<Hyperv>();
    public string RootName => "%TESTPLUGIN%";
    private sealed record MachineEntry(string ID, string Name, string[] Paths);
    private static readonly MachineEntry[] Machines = [
        new MachineEntry("abc", "Item 1", ["C:\\Test", "D:\\test"]),
        new MachineEntry("def", "Item 2", ["E:\\Test1", "F:\\Test2\\"])
    ];

    public IEnumerable<Dto.TreeNodeDto> GetEntries(string[] pathSegments)
    {
        try
        {
            if (pathSegments == null || pathSegments.Length == 0)
            {
                return [new Dto.TreeNodeDto()
                {
                    id =  RootName,
                    text = "Test Plugin Items",
                    cls = "folder",
                    iconCls = "x-tree-icon-test",
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

                return Machines.Select(x => new Dto.TreeNodeDto()
                {
                    id = Util.AppendDirSeparator(string.Join(Path.DirectorySeparatorChar, pathSegments.Append(x.ID))),
                    text = x.Name,
                    cls = "folder",
                    iconCls = "x-tree-icon-test",
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
                var machineId = pathSegments[1];
                var foundVMs = Machines.Where(x => x.ID == machineId).ToList();
                if (foundVMs.Count != 1)
                    throw new NotFoundException(string.Format($"Cannot find VM with ID {machineId}"));
                return foundVMs[0].Paths.Select(x => new Dto.TreeNodeDto()
                {
                    text = x,
                    id = string.Join(Path.DirectorySeparatorChar, pathSegments.Append(x)),
                    cls = "filer",
                    iconCls = "x-tree-icon-testmachine",
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
            Library.Logging.Log.WriteWarningMessage(LOGTAG, "HyperVEnumerationFailed", ex, "Failed to enumerate Hyper-V virtual machines: {0}", ex.Message);
        }

        return [];
    }
}
#endif
