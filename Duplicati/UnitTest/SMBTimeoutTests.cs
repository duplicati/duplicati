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
/// A synchronous call wrapped in a timeout must stop being waited for when the timeout passes,
/// and an SMB connection whose call was given up on must not be used again, as SMBLibrary
/// allows only one request at a time and is still waiting for the server on another thread.
/// No network: the file store is a proxy.
/// </summary>
public class SMBTimeoutTests : BasicSetupHelper
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>
    /// A file store whose directory listing either never returns, or returns one file
    /// </summary>
    public class FileStore : DispatchProxy
    {
        public bool Hangs;
        public int Opened;
        public readonly ManualResetEventSlim Release = new(false);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case nameof(ISMBFileStore.CreateFile):
                    Interlocked.Increment(ref Opened);
                    args![0] = new object();
                    args[1] = FileStatus.FILE_OPENED;
                    return NTStatus.STATUS_SUCCESS;
                case nameof(ISMBFileStore.QueryDirectory):
                    if (Hangs)
                        Release.Wait();
                    args![0] = new List<QueryDirectoryFileInformation> { new FileDirectoryInformation { FileName = "file" } };
                    return NTStatus.STATUS_NO_MORE_FILES;
                case nameof(ISMBFileStore.CloseFile):
                case nameof(ISMBFileStore.Disconnect):
                    return NTStatus.STATUS_SUCCESS;
                default:
                    throw new NotImplementedException(targetMethod.Name);
            }
        }

        public static FileStore Create(bool hangs, out ISMBFileStore proxy)
        {
            proxy = DispatchProxy.Create<ISMBFileStore, FileStore>();
            var store = (FileStore)(object)proxy;
            store.Hangs = hangs;
            return store;
        }
    }

    private static SMBShareConnection Connect(ISMBFileStore store)
        => new SMBShareConnection(new SMB2Client(), store,
            new SMBConnectionParameters("server", SMBTransportType.DirectTCPTransport, "share", "folder", null, null, null, null, null, false),
            new TimeoutOptionsHelper.Timeouts(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

    private static void AssertTimesOut(Task task)
    {
        Assert.That(Task.WhenAny(task, Task.Delay(Limit)).Result, Is.SameAs(task), "The call that does not return must stop being waited for");
        Assert.ThrowsAsync<TimeoutException>(() => task);
    }

    [Test]
    [Category("Utility")]
    public void WithTimeoutStopsWaitingForAnAction()
    {
        using var release = new ManualResetEventSlim(false);
        try
        {
            AssertTimesOut(Library.Utility.Utility.WithTimeout(TimeSpan.FromSeconds(1), CancellationToken.None, _ => release.Wait()));
        }
        finally
        {
            release.Set();
        }
    }

    [Test]
    [Category("Utility")]
    public void WithTimeoutStopsWaitingForAFunction()
    {
        using var release = new ManualResetEventSlim(false);
        try
        {
            AssertTimesOut(Library.Utility.Utility.WithTimeout(TimeSpan.FromSeconds(1), CancellationToken.None, _ => { release.Wait(); return 1; }));
        }
        finally
        {
            release.Set();
        }
    }

    [Test]
    [Category("Utility")]
    public void TimedOutConnectionIsNotUsedAgain()
    {
        var store = FileStore.Create(true, out var proxy);
        var connection = Connect(proxy);
        try
        {
            AssertTimesOut(connection.ListAsync("folder", CancellationToken.None));
            Assert.That(connection.IsBroken, Is.True);

            Assert.ThrowsAsync<Library.Interface.UserInformationException>(() => connection.ListAsync("folder", CancellationToken.None));
            Assert.That(store.Opened, Is.EqualTo(1), "The connection must not send another request");
        }
        finally
        {
            store.Release.Set();
        }
    }

    [Test]
    [Category("Utility")]
    public void BackendReplacesATimedOutConnection()
    {
        var stuck = FileStore.Create(true, out var stuckProxy);
        FileStore.Create(false, out var workingProxy);
        var connections = new Queue<SMBShareConnection>(new[] { Connect(stuckProxy), Connect(workingProxy) });

        using var backend = new SMBBackend("smb://server/share/folder", new Dictionary<string, string?>())
        {
            ConnectionFactory = _ => Task.FromResult(connections.Dequeue())
        };
        try
        {
            AssertTimesOut(backend.ListAsync(CancellationToken.None).ToListAsync().AsTask());

            var files = backend.ListAsync(CancellationToken.None).ToListAsync().AsTask().Result;
            Assert.That(files.Select(x => x.Name), Is.EqualTo(new[] { "file" }));
            Assert.That(connections, Is.Empty, "A new connection must be made after the timeout");
        }
        finally
        {
            stuck.Release.Set();
        }
    }
}
