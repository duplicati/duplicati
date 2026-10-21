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

using System.Collections.Generic;
using System.IO;
using Duplicati.Library.Main;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests for the arguments the Agent passes on to the webserver it hosts
    /// </summary>
    [TestFixture]
    public class AgentServerArgumentsTests
    {
        private static Agent.Program.CommandLineArguments CreateConfig(bool pingPongKeepalive)
            => new(
                AgentRegistrationUrl: "",
                AgentSettingsFile: new FileInfo("agent.json"),
                AgentSettingsFilePassphrase: null,
                AgentRegisterOnly: false,
                WebserviceInterface: "loopback",
                WebservicePort: "8210",
                WebservicePassword: "password",
                SettingsEncryptionKey: null,
                WindowsEventLog: "",
                DisableDbEncryption: false,
                DisableDefaultSecretProvider: false,
                DisableSecretProvider: SecretProviderHelper.DisableSecretProviderMode.None,
                WebserviceResetJwtConfig: true,
                WebserviceAllowedHostnames: "127.0.0.1",
                WebserviceApiOnly: true,
                WebserviceDisableSigninTokens: true,
                PingPongKeepalive: pingPongKeepalive,
                DisablePreSharedKey: false,
                KeepWebservicePassword: false,
                SecretProvider: null,
                SecretProviderCache: SecretProviderHelper.CachingLevel.None,
                SecretProviderPattern: SecretProviderHelper.DEFAULT_PATTERN,
                AllowInsecureDatafolder: false,
                ReducedReporting: null
            );

        [Test]
        [Category("Agent")]
        [TestCase(true)]
        [TestCase(false)]
        public void PingPongKeepaliveIsPassedToServer(bool pingPongKeepalive)
        {
            var args = Agent.Program.BuildServerArguments(CreateConfig(pingPongKeepalive), null);

            // Parse the arguments the same way the server does
            var options = FilterCollector.ExtractOptions(new List<string>(args)).Item1;

            Assert.IsTrue(options.ContainsKey("ping-pong-keepalive"), "The option was not passed on to the server");
            Assert.AreEqual(pingPongKeepalive, Library.Utility.Utility.ParseBoolOption(options, "ping-pong-keepalive"));
        }
    }
}
