// Copyright (C) 2026, The Duplicati Team
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

#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Backend.SMB;
using Duplicati.Library.Backend.SMB.Model;
using Duplicati.Library.Utility.Options;
using NUnit.Framework;
using SMBLibrary;
using SMBLibrary.Client;

namespace Duplicati.UnitTest;

/// <summary>
/// A Windows share lists "." and ".." in every folder, also in the destination folder itself.
/// Sync warns about every listed name it cannot use, so the backend must leave them out.
/// No network: the file store is a proxy.
/// </summary>
public class SMBListDotEntriesTests
{
    /// <summary>
    /// A file store whose directory listing holds ".", "..", a file and a folder
    /// </summary>
    public class FileStore : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case nameof(ISMBFileStore.CreateFile):
                    args![0] = new object();
                    args[1] = FileStatus.FILE_OPENED;
                    return NTStatus.STATUS_SUCCESS;
                case nameof(ISMBFileStore.QueryDirectory):
                    args![0] = new List<QueryDirectoryFileInformation>
                    {
                        new FileDirectoryInformation { FileName = ".", FileAttributes = FileAttributes.Directory },
                        new FileDirectoryInformation { FileName = "..", FileAttributes = FileAttributes.Directory },
                        new FileDirectoryInformation { FileName = "file.txt" },
                        new FileDirectoryInformation { FileName = "sub", FileAttributes = FileAttributes.Directory },
                    };
                    return NTStatus.STATUS_NO_MORE_FILES;
                case nameof(ISMBFileStore.CloseFile):
                case nameof(ISMBFileStore.Disconnect):
                    return NTStatus.STATUS_SUCCESS;
                default:
                    throw new System.NotImplementedException(targetMethod.Name);
            }
        }
    }

    private static SMBBackend CreateBackend()
    {
        var store = DispatchProxy.Create<ISMBFileStore, FileStore>();
        var connection = new SMBShareConnection(new SMB2Client(), store,
            new SMBConnectionParameters("server", SMBTransportType.DirectTCPTransport, "share", "folder", null, null, null, null, null, false),
            new TimeoutOptionsHelper.Timeouts(System.TimeSpan.FromSeconds(30), System.TimeSpan.FromSeconds(30), System.TimeSpan.FromSeconds(30), System.TimeSpan.FromMinutes(10)));
        return new SMBBackend("smb://server/share/folder", new Dictionary<string, string?>())
        {
            ConnectionFactory = _ => Task.FromResult(connection)
        };
    }

    [Test]
    [Category("Utility")]
    public async Task ListingTheDestinationFolderLeavesOutDotEntries()
    {
        using var backend = CreateBackend();
        var names = await backend.ListAsync(CancellationToken.None).Select(x => x.Name).ToListAsync();
        Assert.That(names, Is.EqualTo(new[] { "file.txt", "sub" }));
    }

    [Test]
    [Category("Utility")]
    public async Task ListingASubfolderLeavesOutDotEntries()
    {
        using var backend = CreateBackend();
        var names = await backend.ListAsync("sub", CancellationToken.None).Select(x => x.Name).ToListAsync();
        Assert.That(names, Is.EqualTo(new[] { "file.txt", "sub" }));
    }
}
