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
/// A delete or rename must fail when the file cannot be opened, instead of reporting success
/// and leaving the file unchanged.
/// No network: the file store is a proxy.
/// </summary>
public class SMBShareConnectionOpenFailureTests : BasicSetupHelper
{
    /// <summary>
    /// A file store that refuses to open any file with a status, and counts how often a handle is closed
    /// </summary>
    public class FileStore : DispatchProxy
    {
        public NTStatus OpenStatus;
        public int Closed;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case nameof(ISMBFileStore.CreateFile):
                    args![0] = null;
                    args[1] = FileStatus.FILE_DOES_NOT_EXIST;
                    return OpenStatus;
                case nameof(ISMBFileStore.CloseFile):
                    Interlocked.Increment(ref Closed);
                    return NTStatus.STATUS_SUCCESS;
                default:
                    throw new NotImplementedException(targetMethod.Name);
            }
        }
    }

    private static (SMBShareConnection Connection, FileStore Store) Create(NTStatus openStatus)
    {
        var proxy = DispatchProxy.Create<ISMBFileStore, FileStore>();
        var store = (FileStore)(object)proxy;
        store.OpenStatus = openStatus;
        var connection = new SMBShareConnection(new SMB2Client(), proxy,
            new SMBConnectionParameters("server", SMBTransportType.DirectTCPTransport, "share", "folder", null, null, null, null, null, false),
            new TimeoutOptionsHelper.Timeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)));
        return (connection, store);
    }

    [Test]
    [Category("Utility")]
    public void DeleteFailsWhenTheFileCannotBeOpened()
    {
        var (connection, store) = Create(NTStatus.STATUS_ACCESS_DENIED);
        var ex = Assert.ThrowsAsync<UserInformationException>(() => connection.DeleteAsync("file", CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain(nameof(NTStatus.STATUS_ACCESS_DENIED)));
        Assert.That(store.Closed, Is.EqualTo(0), "No handle was opened, so none should be closed");
    }

    [Test]
    [Category("Utility")]
    public void DeleteReportsAMissingFile()
    {
        var (connection, _) = Create(NTStatus.STATUS_OBJECT_NAME_NOT_FOUND);
        Assert.ThrowsAsync<FileMissingException>(() => connection.DeleteAsync("file", CancellationToken.None));
    }

    [Test]
    [Category("Utility")]
    public void RenameFailsWhenTheFileCannotBeOpened()
    {
        var (connection, store) = Create(NTStatus.STATUS_ACCESS_DENIED);
        var ex = Assert.ThrowsAsync<UserInformationException>(() => connection.RenameAsync("file", "other", CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain(nameof(NTStatus.STATUS_ACCESS_DENIED)));
        Assert.That(store.Closed, Is.EqualTo(0), "No handle was opened, so none should be closed");
    }

    [Test]
    [Category("Utility")]
    public void RenameReportsAMissingFile()
    {
        var (connection, _) = Create(NTStatus.STATUS_OBJECT_NAME_NOT_FOUND);
        Assert.ThrowsAsync<FileMissingException>(() => connection.RenameAsync("file", "other", CancellationToken.None));
    }
}
