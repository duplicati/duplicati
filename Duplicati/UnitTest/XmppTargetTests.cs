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

using Duplicati.Library.Modules.Builtin;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The value of send-xmpp-username is a jid rather than a url, and it is split with
    /// the url parser. The splitting was inline in SendMessage, right before the client
    /// connects, so it could not be checked without a server. These tests pin what it
    /// produces.
    /// </summary>
    [TestFixture]
    [Category("XmppTarget")]
    public class XmppTargetTests
    {
        [Test]
        public void APlainJidGetsTheDefaultPortAndResource()
        {
            var target = SendJabberMessage.ParseXmppTarget("user@server.example.com");

            Assert.AreEqual("server.example.com", target.Host);
            Assert.AreEqual("user", target.Username);
            Assert.IsNull(target.Password);
            Assert.AreEqual(5222, target.Port);
            Assert.AreEqual("Duplicati", target.Resource, "A jid without a resource gets a default one");
        }

        [Test]
        public void APasswordInTheJidIsPickedUp()
        {
            var target = SendJabberMessage.ParseXmppTarget("user:secret@server.example.com");

            Assert.AreEqual("user", target.Username);
            Assert.AreEqual("secret", target.Password);
        }

        [Test]
        public void TheResourceComesFromThePath()
        {
            var target = SendJabberMessage.ParseXmppTarget("user@server.example.com/laptop");

            Assert.AreEqual("server.example.com", target.Host);
            Assert.AreEqual("laptop", target.Resource);
        }

        [Test]
        public void AnExplicitPortIsUsedAsGiven()
        {
            var target = SendJabberMessage.ParseXmppTarget("user@server.example.com:5333");

            Assert.AreEqual(5333, target.Port);
        }

        [Test]
        public void TheHttpsSchemeSelectsTheTlsPort()
        {
            var target = SendJabberMessage.ParseXmppTarget("https://user@server.example.com");

            Assert.AreEqual(5223, target.Port);
        }

        [Test]
        public void TheHttpSchemeSelectsThePlainPort()
        {
            var target = SendJabberMessage.ParseXmppTarget("http://user@server.example.com");

            Assert.AreEqual(5222, target.Port);
        }

        [Test]
        public void AnUnrelatedSchemeSelectsThePlainPort()
        {
            var target = SendJabberMessage.ParseXmppTarget("xmpps://user@server.example.com");

            Assert.AreEqual(5222, target.Port, "Only 'https' selects the TLS port");
        }

        [Test]
        public void AnUppercaseHttpsSchemeSelectsThePlainPort()
        {
            // The scheme is compared as written, so an uppercase HTTPS:// does not select
            // the TLS port. That looks unintended, but it is what happens today.
            var target = SendJabberMessage.ParseXmppTarget("HTTPS://user@server.example.com");

            Assert.AreEqual(5222, target.Port);
        }

        [Test]
        public void AnEscapedUserPartIsDecoded()
        {
            var target = SendJabberMessage.ParseXmppTarget("us%65r@server.example.com");

            Assert.AreEqual("user", target.Username);
        }
    }
}
