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
    /// A url scheme is case insensitive (RFC 3986), so the same url must resolve to the
    /// same backend however the scheme is written. Reads module metadata only; no
    /// network and no external programs.
    /// </summary>
    public class BackendLoaderSchemeTests : BasicSetupHelper
    {
        private static string CommandNames(IReadOnlyList<Library.Interface.ICommandLineArgument> commands)
            => commands == null ? "<null>" : string.Join(",", commands.Select(x => x.Name).OrderBy(x => x));

        // 'file' is deliberately not used here: the relaxed parser has a dedicated
        // file:// branch that already normalizes the scheme to lower case, so it would
        // pass without the loader being case insensitive at all.
        private const string HOST_URL = "ssh://example.com/folder";
        private const string HOST_URL_WITH_CREDENTIALS = "ssh://user:pw@example.com/folder";

        [TestCase("SSH")]
        [TestCase("SsH")]
        [Category("Utility")]
        public void SupportedCommandsAreTheSameWhateverTheSchemeCase(string scheme)
        {
            var lower = BackendLoader.GetSupportedCommands(HOST_URL);
            Assert.IsNotNull(lower, "The ssh backend must be resolvable");
            Assert.Greater(lower.Count, 0, "The ssh backend must report commands");

            var mixed = BackendLoader.GetSupportedCommands(scheme + "://example.com/folder");
            Assert.AreEqual(CommandNames(lower), CommandNames(mixed),
                $"'{scheme}://' must resolve to the same backend as 'ssh://'");
        }

        [Test]
        [Category("Utility")]
        public void GetBackendIgnoresTheSchemeCase()
        {
            var lower = BackendLoader.GetBackend(HOST_URL_WITH_CREDENTIALS, new Dictionary<string, string>());
            Assert.IsNotNull(lower, "The ssh backend must be resolvable");

            var upper = BackendLoader.GetBackend("SSH://user:pw@example.com/folder", new Dictionary<string, string>());
            Assert.IsNotNull(upper, "'SSH://' must resolve to a backend");
            Assert.AreEqual(lower.GetType(), upper.GetType());
        }

        [Test]
        [Category("Utility")]
        public void TheTrailingSFallbackIgnoresTheSchemeCase()
        {
            // 'ftps' is not a backend of its own: the loader strips the trailing 's' and
            // turns on use-ssl. That fallback has to work for an uppercase scheme too.
            var lower = BackendLoader.GetSupportedCommands("ftps://example.com/folder");
            Assert.IsNotNull(lower, "'ftps://' must resolve through the trailing-s fallback");
            Assert.Greater(lower.Count, 0);

            var upper = BackendLoader.GetSupportedCommands("FTPS://example.com/folder");
            Assert.AreEqual(CommandNames(lower), CommandNames(upper),
                "'FTPS://' must resolve the same way as 'ftps://'");
        }

        [Test]
        [Category("Utility")]
        public void EncryptionModulesAreFoundWhateverTheKeyCase()
        {
            // The loaders share one module dictionary, so the same rule applies to the
            // modules that are looked up by file extension.
            var lower = EncryptionLoader.GetSupportedCommands("aes");
            Assert.IsNotNull(lower, "The aes module must be resolvable");

            var upper = EncryptionLoader.GetSupportedCommands("AES");
            Assert.AreEqual(CommandNames(lower), CommandNames(upper));
        }
    }
}
