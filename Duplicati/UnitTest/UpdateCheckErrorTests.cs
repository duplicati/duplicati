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

using System.Net;
using System.Net.Sockets;
using Duplicati.Library.AutoUpdater;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// An update check that reaches no update server returned null, the same as a check
    /// that found no update, so a client could not tell the user that the check failed.
    /// </summary>
    [TestFixture]
    [Category("UpdateCheck")]
    public class UpdateCheckErrorTests
    {
        [Test]
        public void AnUnreachableUpdateServerIsReportedAsAnError()
        {
            var url = $"http://127.0.0.1:{GetClosedPort()}/latest.manifest";

            var update = UpdaterManager.CheckForUpdate(ReleaseType.Stable, [url], out var error);

            Assert.That(update, Is.Null);
            Assert.That(error, Is.Not.Null, "A check that reached no server should say why");
        }

        [Test]
        public void NoUpdateServerIsReportedAsAnError()
        {
            var update = UpdaterManager.CheckForUpdate(ReleaseType.Stable, [], out var error);

            Assert.That(update, Is.Null);
            Assert.That(error, Is.Not.Null, "A check without a server was not done");
        }

        private static int GetClosedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
