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
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Nothing reads --synchronous-upload. It is still offered in the help and
    /// still accepted, so setting it looked like it did something, and the only
    /// way to find out otherwise was to read Options.cs.
    /// </summary>
    public class DeprecatedUploadOptionTests : BasicSetupHelper
    {
        private async Task<IBackupResults> BackupWith(Dictionary<string, string> extra)
        {
            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, "file"), new byte[] { 0, 1, 2 });

            var options = new Dictionary<string, string>(this.TestOptions);
            foreach (var (key, value) in extra)
                options[key] = value;

            using var c = new Controller("file://" + this.TARGETFOLDER, options, null);
            return await c.BackupAsync(new[] { this.DATAFOLDER });
        }

        [Test]
        [Category("Options")]
        public async Task SettingSynchronousUploadSaysItDoesNothingAsync()
        {
            var results = await BackupWith(new Dictionary<string, string> { ["synchronous-upload"] = "true" });

            Assert.AreEqual(0, results.Errors.Count());

            var warnings = results.Warnings.Where(x => x.Contains("synchronous-upload")).ToList();
            Assert.AreEqual(1, warnings.Count, string.Join("; ", results.Warnings));
            Assert.IsTrue(warnings[0].Contains("no longer used"), warnings[0]);
        }

        [Test]
        [Category("Options")]
        public async Task TheOptionIsStillAcceptedAsync()
        {
            // Removing it outright would fail every existing configuration that
            // sets it, so it stays offered and stays valid
            var supported = new Options(new Dictionary<string, string?>()).SupportedCommands;
            var argument = supported.FirstOrDefault(x => x.Name == "synchronous-upload");

            Assert.IsNotNull(argument, "the option must remain, so old configurations still load");
            Assert.IsTrue(argument!.Deprecated);

            var results = await BackupWith(new Dictionary<string, string> { ["synchronous-upload"] = "true" });
            Assert.AreEqual(0, results.Errors.Count());
        }

        [Test]
        [Category("Options")]
        public async Task AnOptionThatStillWorksIsNotWarnedAboutAsync()
        {
            // asynchronous-upload-limit reads the same way and is still used, by
            // BackendManager.Handler, so it must not be caught by this
            var results = await BackupWith(new Dictionary<string, string> { ["asynchronous-upload-limit"] = "2" });

            Assert.AreEqual(0, results.Errors.Count());
            Assert.AreEqual(0, results.Warnings.Count(x => x.Contains("no longer used")),
                string.Join("; ", results.Warnings));
        }
    }
}
