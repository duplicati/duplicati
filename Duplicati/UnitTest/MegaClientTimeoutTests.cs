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
using CG.Web.MegaApiClient;
using Duplicati.Library.Backend.Mega;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A MEGA call that timed out keeps running on the client, so the backend must not send the
/// next request through that client but log in again with a new one.
/// No network: the client is a proxy.
/// </summary>
public class MegaClientTimeoutTests : BasicSetupHelper
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    public class Node : DispatchProxy
    {
        public NodeType Type;
        public string Id = "";
        public string? ParentId;
        public string? Name;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod!.Name switch
            {
                "get_Type" => Type,
                "get_Id" => Id,
                "get_ParentId" => ParentId,
                "get_Name" => Name,
                _ => throw new NotImplementedException(targetMethod.Name)
            };

        public static INode Create(NodeType type, string id, string? parentId, string? name)
        {
            var proxy = DispatchProxy.Create<INode, Node>();
            var node = (Node)(object)proxy;
            (node.Type, node.Id, node.ParentId, node.Name) = (type, id, parentId, name);
            return proxy;
        }
    }

    /// <summary>
    /// A client whose node listing either never returns, or returns the root and the backend folder
    /// </summary>
    public class Client : DispatchProxy
    {
        public bool Hangs;
        public int Listed;
        public readonly ManualResetEventSlim Release = new(false);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IMegaApiClient.GetNodes) && targetMethod.GetParameters().Length == 0)
            {
                Interlocked.Increment(ref Listed);
                if (Hangs)
                    Release.Wait();
                return new[] { Node.Create(NodeType.Root, "root", null, null), Node.Create(NodeType.Directory, "folder", "root", "folder") };
            }
            throw new NotImplementedException(targetMethod.Name);
        }

        public static Client Create(bool hangs, out IMegaApiClient proxy)
        {
            proxy = DispatchProxy.Create<IMegaApiClient, Client>();
            var client = (Client)(object)proxy;
            client.Hangs = hangs;
            return client;
        }
    }

    [Test]
    [Category("Utility")]
    public void TimedOutClientIsReplaced()
    {
        var stuck = Client.Create(true, out var stuckProxy);
        var working = Client.Create(false, out var workingProxy);
        var clients = new Queue<IMegaApiClient>(new[] { stuckProxy, workingProxy });

        using var backend = new MegaBackend("mega://folder", new Dictionary<string, string?>
        {
            ["auth-username"] = "user",
            ["auth-password"] = "password",
            ["list-timeout"] = "1s",
        })
        {
            ClientFactory = _ => Task.FromResult(clients.Dequeue())
        };
        try
        {
            var first = backend.ListAsync(CancellationToken.None).ToListAsync().AsTask();
            Assert.That(Task.WhenAny(first, Task.Delay(Limit)).Result, Is.SameAs(first), "The listing must stop at the list timeout");
            Assert.ThrowsAsync<TimeoutException>(() => first);

            var second = backend.ListAsync(CancellationToken.None).ToListAsync().AsTask();
            Assert.That(Task.WhenAny(second, Task.Delay(Limit)).Result, Is.SameAs(second), "The next listing must not wait on the client that timed out");
            Assert.That(second.Result, Is.Empty);
            Assert.That(stuck.Listed, Is.EqualTo(1), "The client that timed out must not be used again");
            Assert.That(working.Listed, Is.EqualTo(1));
        }
        finally
        {
            stuck.Release.Set();
        }
    }
}
