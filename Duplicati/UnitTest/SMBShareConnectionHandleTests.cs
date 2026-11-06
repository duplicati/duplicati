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

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Duplicati.Library.Backend.SMB;
using Duplicati.Library.Backend.SMB.Model;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility.Options;
using NUnit.Framework;
using SMBLibrary;
using SMBLibrary.Client;

namespace Duplicati.UnitTest;

/// <summary>
/// A file opened for an operation must be closed when the operation fails halfway, or the
/// handle stays open on the server for as long as the shared connection lives.
/// No network: the file store is a proxy.
/// </summary>
public class SMBShareConnectionHandleTests : BasicSetupHelper
{
    /// <summary>
    /// A file store that opens any file, refuses to read it or change its information,
    /// and counts how often a handle is closed
    /// </summary>
    public class FileStore : DispatchProxy
    {
        public int Closed;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case nameof(ISMBFileStore.CreateFile):
                    args![0] = new object();
                    args[1] = FileStatus.FILE_OPENED;
                    return NTStatus.STATUS_SUCCESS;
                case nameof(ISMBFileStore.ReadFile):
                    args![0] = Array.Empty<byte>();
                    return NTStatus.STATUS_ACCESS_DENIED;
                case nameof(ISMBFileStore.SetFileInformation):
                    return NTStatus.STATUS_ACCESS_DENIED;
                case nameof(ISMBFileStore.CloseFile):
                    Interlocked.Increment(ref Closed);
                    return NTStatus.STATUS_SUCCESS;
                default:
                    throw new NotImplementedException(targetMethod.Name);
            }
        }
    }

    private static (SMBShareConnection Connection, FileStore Store) Create()
    {
        var proxy = DispatchProxy.Create<ISMBFileStore, FileStore>();
        var connection = new SMBShareConnection(new SMB2Client(), proxy,
            new SMBConnectionParameters("server", SMBTransportType.DirectTCPTransport, "share", "folder", null, null, null, null, null, false),
            new TimeoutOptionsHelper.Timeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)));
        return (connection, (FileStore)(object)proxy);
    }

    [Test]
    [Category("Utility")]
    public void DeleteClosesTheFileWhenItFails()
    {
        var (connection, store) = Create();
        Assert.ThrowsAsync<UserInformationException>(() => connection.DeleteAsync("file", CancellationToken.None));
        Assert.That(store.Closed, Is.EqualTo(1));
    }

    [Test]
    [Category("Utility")]
    public void RenameClosesTheFileWhenItFails()
    {
        var (connection, store) = Create();
        Assert.ThrowsAsync<UserInformationException>(() => connection.RenameAsync("file", "other", CancellationToken.None));
        Assert.That(store.Closed, Is.EqualTo(1));
    }

    [Test]
    [Category("Utility")]
    public void GetClosesTheFileWhenItFails()
    {
        var (connection, store) = Create();
        Assert.ThrowsAsync<UserInformationException>(() => connection.GetAsync("file", new MemoryStream(), CancellationToken.None));
        Assert.That(store.Closed, Is.EqualTo(1));
    }
}
