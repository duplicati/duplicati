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
using System.Linq;
using Duplicati.Library.DynamicLoader;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests that the parity module is discoverable through the dynamic loader.
    /// Requires no external programs (only metadata is read).
    /// </summary>
    public class ParityLoaderTests : BasicSetupHelper
    {
        [Test]
        [Category("Parity")]
        public static void BuiltInPar2ModuleIsRegistered()
        {
            Assert.Contains("par2", ParityLoader.Keys);
        }

        [Test]
        [Category("Parity")]
        public static void GetModuleReturnsPar2Instance()
        {
            var module = ParityLoader.GetModule("par2", new Dictionary<string, string>());
            Assert.IsNotNull(module);
            Assert.AreEqual("par2", module.FilenameExtension);
            Assert.IsNotEmpty(module.DisplayName);
            Assert.IsNotEmpty(module.Description);
        }

        [Test]
        [Category("Parity")]
        public static void GetModuleReturnsNullForUnknownKey()
        {
            Assert.IsNull(ParityLoader.GetModule("not-a-parity-module", new Dictionary<string, string>()));
        }

        [Test]
        [Category("Parity")]
        public static void SupportedCommandsExposePar2Options()
        {
            var commands = ParityLoader.GetSupportedCommands("par2");
            Assert.IsNotNull(commands);
            Assert.IsTrue(commands.Any(c => c.Name == "par2-program-path"));
        }
    }
}
