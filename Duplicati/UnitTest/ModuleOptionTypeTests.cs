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

using System.Linq;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Verifies that module options are declared with an <see cref="CommandLineArgument.ArgumentType"/>
    /// matching the values they actually accept. A wrong type is not just cosmetic: the option
    /// validator rejects otherwise valid values, and the web UI renders the wrong editor for them.
    /// Only metadata is read, so no external programs are required.
    /// </summary>
    public class ModuleOptionTypeTests : BasicSetupHelper
    {
        private static ICommandLineArgument GetWebModuleOption(string moduleKey, string optionName)
        {
            var module = WebLoader.Modules.FirstOrDefault(x => x.Key == moduleKey);
            Assert.IsNotNull(module, $"The web module {moduleKey} should be registered");

            var option = module!.SupportedCommands?.FirstOrDefault(x => x.Name == optionName);
            Assert.IsNotNull(option, $"The module {moduleKey} should support the option {optionName}");
            return option!;
        }

        private static ICommandLineArgument GetGenericModuleOption(string moduleKey, string optionName)
        {
            var module = GenericLoader.Modules.FirstOrDefault(x => x.Key == moduleKey);
            Assert.IsNotNull(module, $"The generic module {moduleKey} should be registered");

            var option = module!.SupportedCommands?.FirstOrDefault(x => x.Name == optionName);
            Assert.IsNotNull(option, $"The module {moduleKey} should support the option {optionName}");
            return option!;
        }

        [Test]
        [Category("ModuleOptionType")]
        public static void SshKeygenUsernameIsAStringOption()
        {
            // The value is a username appended to the public key, defaulting to
            // "backup-user@<machinename>", so it must not be declared as an integer.
            var option = GetWebModuleOption("ssh-keygen", "key-username");

            Assert.AreEqual(CommandLineArgument.ArgumentType.String, option.Type);
        }

        [Test]
        [Category("ModuleOptionType")]
        public static void SshKeygenKeyLengthRemainsAnIntegerOption()
        {
            // Guards the neighbouring option, which is genuinely an integer.
            var option = GetWebModuleOption("ssh-keygen", "key-bits");

            Assert.AreEqual(CommandLineArgument.ArgumentType.Integer, option.Type);
        }

        [Test]
        [Category("ModuleOptionType")]
        public static void SendHttpRetryDelayIsATimespanOption()
        {
            // The value is parsed with ParseTimespanOption and defaults to "1s",
            // so it must not be declared as an integer.
            var option = GetGenericModuleOption("sendhttp", "send-http-retry-delay");

            Assert.AreEqual(CommandLineArgument.ArgumentType.Timespan, option.Type);
        }

        [Test]
        [Category("ModuleOptionType")]
        public static void SendHttpRetriesRemainsAnIntegerOption()
        {
            // Guards the neighbouring option, which is genuinely an integer.
            var option = GetGenericModuleOption("sendhttp", "send-http-retries");

            Assert.AreEqual(CommandLineArgument.ArgumentType.Integer, option.Type);
        }
    }
}
