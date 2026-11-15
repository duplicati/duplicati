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
using System.Security.Authentication;
using Duplicati.Library.Backend;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Covers which FTP replies make the backend switch to Bouncy Castle when the TLS implementation is Auto.
    /// The replies are the ones vsftpd 3.0.5 and FileZilla Server sent in manual tests.
    /// </summary>
    [TestFixture]
    public class FtpTlsFallbackTests
    {
        [TestCase("522", "SSL connection failed: session reuse required", SslProtocols.Tls13)]
        [TestCase("522", "SSL connection failed: session reuse required", SslProtocols.Tls12)]
        [TestCase("425", "Unable to build data connection: TLS session of data connection not resumed.", SslProtocols.Tls13)]
        [TestCase("426", "Failure reading network stream.", SslProtocols.Tls13)]
        public void SwitchesForRefusalsBouncyCastleAvoids(string code, string message, SslProtocols protocol)
        {
            Assert.IsTrue(FTP.IsRefusalBouncyCastleAvoids(code, message, protocol));
        }

        [TestCase("426", "Failure reading network stream.", SslProtocols.Tls12)]
        [TestCase("426", "Failure reading network stream.", SslProtocols.None)]
        [TestCase("425", "Failed to establish connection.", SslProtocols.Tls13)]
        [TestCase("553", "Could not create file.", SslProtocols.Tls13)]
        [TestCase("226", "Transfer complete.", SslProtocols.Tls13)]
        public void KeepsTheSystemTlsForOtherReplies(string code, string message, SslProtocols protocol)
        {
            Assert.IsFalse(FTP.IsRefusalBouncyCastleAvoids(code, message, protocol));
        }
    }
}
