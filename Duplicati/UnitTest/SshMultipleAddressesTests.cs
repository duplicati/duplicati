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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A server name with several addresses failed when the first address did not work,
/// instead of trying the next one (#4238)
/// </summary>
[TestFixture]
public class SshMultipleAddressesTests
{
    /// <summary>
    /// Accepts connections and answers each with an ssh banner, so that a client
    /// that reaches it fails only later, in the key exchange
    /// </summary>
    private sealed class BannerServer : IDisposable
    {
        private readonly TcpListener m_listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource m_stop = new();
        private int m_connections;

        public BannerServer()
        {
            m_listener.Start();
            _ = Task.Run(AcceptAsync);
        }

        public int Port => ((IPEndPoint)m_listener.LocalEndpoint).Port;
        public int Connections => Volatile.Read(ref m_connections);

        private async Task AcceptAsync()
        {
            try
            {
                while (!m_stop.IsCancellationRequested)
                {
                    using var client = await m_listener.AcceptTcpClientAsync(m_stop.Token).ConfigureAwait(false);
                    Interlocked.Increment(ref m_connections);
                    var banner = Encoding.ASCII.GetBytes("SSH-2.0-test\r\n");
                    await client.GetStream().WriteAsync(banner, m_stop.Token).ConfigureAwait(false);
                }
            }
            catch (Exception) when (m_stop.IsCancellationRequested)
            {
            }
        }

        public void Dispose()
        {
            m_stop.Cancel();
            m_listener.Stop();
        }
    }

    /// <summary>
    /// A port on the IPv6 loopback that nothing listens on, so connecting to it is refused
    /// </summary>
    private static int FreeIPv6Port()
    {
        using var probe = new TcpListener(IPAddress.IPv6Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static SSHv2 CreateBackend(string host, int port)
        => new($"ssh://{host}:{port}/folder", new Dictionary<string, string?>
        {
            ["auth-username"] = "user",
            ["auth-password"] = "password",
            ["ssh-accept-any-fingerprints"] = "true",
            ["short-timeout"] = "20s",
        });

    [Test]
    [Category("Targeted")]
    public async Task TheNextAddressIsTriedWhenTheFirstCannotBeReachedAsync()
    {
        if (!Socket.OSSupportsIPv6)
            Assert.Ignore("The test needs an IPv6 loopback for the address that does not work");

        using var server = new BannerServer();

        // The port is chosen on the IPv4 loopback, so a free one on the IPv6 loopback is
        // only likely to match; pick the server port and make sure ::1 does not listen on it
        using (var check = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp))
        {
            try
            {
                await check.ConnectAsync(IPAddress.IPv6Loopback, server.Port);
                Assert.Ignore("Something listens on the IPv6 loopback at the test port");
            }
            catch (SocketException)
            {
            }
        }

        using var backend = CreateBackend("multi.test", server.Port);
        var resolved = new List<string>();
        backend.HostResolver = (name, _) =>
        {
            resolved.Add(name);
            return Task.FromResult(new[] { IPAddress.IPv6Loopback, IPAddress.Loopback });
        };

        // The banner server ends the connection after the banner, so the connection fails, but only
        // after it reached the second address
        Assert.CatchAsync<Exception>(() => backend.CreateConnection(CancellationToken.None));

        Assert.That(resolved, Is.EqualTo(new[] { "multi.test" }));
        Assert.That(server.Connections, Is.EqualTo(1), "The second address was not tried");
    }

    [Test]
    [Category("Targeted")]
    public void ANameWithOneAddressIsLeftToTheConnection()
    {
        var port = FreeIPv6Port();
        using var backend = CreateBackend("single.test", port);
        backend.HostResolver = (_, _) => Task.FromResult(new[] { IPAddress.IPv6Loopback });

        // SSH.NET gets the name itself and cannot resolve it, as before the change
        var ex = Assert.CatchAsync<Exception>(() => backend.CreateConnection(CancellationToken.None));
        Assert.That(ex, Is.InstanceOf<SocketException>().Or.InnerException.InstanceOf<SocketException>(), ex?.ToString());
    }
}
