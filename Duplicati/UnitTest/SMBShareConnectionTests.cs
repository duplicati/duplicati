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
using System.Collections.Generic;
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
/// A listing that fails must report why it failed, even when closing the directory handle
/// afterwards fails too. SMBLibrary throws "Not enough credits" from CloseFile when the
/// listing request timed out and its credits have not come back (forum topic 22594).
/// No network: the file store is a proxy.
/// </summary>
public class SMBShareConnectionTests : BasicSetupHelper
{
    private static readonly SMBConnectionParameters Parameters =
        new("server", SMBTransportType.DirectTCPTransport, "share", "folder", null, null, null, null, null, false);

    /// <summary>
    /// A file store whose directory listing fails with a status, and whose
    /// CloseFile throws as SMBLibrary does when no credits are left
    /// </summary>
    public class FileStore : DispatchProxy
    {
        public NTStatus QueryStatus;
        public int Closed;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case nameof(ISMBFileStore.CreateFile):
                    args![0] = new object();
                    args[1] = FileStatus.FILE_OPENED;
                    return NTStatus.STATUS_SUCCESS;
                case nameof(ISMBFileStore.QueryDirectory):
                    args![0] = new List<QueryDirectoryFileInformation>();
                    return QueryStatus;
                case nameof(ISMBFileStore.CloseFile):
                    Interlocked.Increment(ref Closed);
                    throw new Exception("Not enough credits");
                default:
                    throw new NotImplementedException(targetMethod.Name);
            }
        }
    }

    [Test]
    [Category("Utility")]
    public void ListReportsTheQueryStatusWhenCloseFails()
    {
        var proxy = DispatchProxy.Create<ISMBFileStore, FileStore>();
        var store = (FileStore)(object)proxy;
        store.QueryStatus = NTStatus.STATUS_IO_TIMEOUT;
        var connection = new SMBShareConnection(new SMB2Client(), proxy, Parameters,
            new TimeoutOptionsHelper.Timeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)));

        var ex = Assert.ThrowsAsync<UserInformationException>(() => connection.ListAsync("folder", CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain(nameof(NTStatus.STATUS_IO_TIMEOUT)));
        Assert.That(store.Closed, Is.EqualTo(1), "The directory handle must still be closed");
    }
}
