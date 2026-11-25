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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A parity file is parsed as the data volume it belongs to, so the recovery tool tried to
    /// decrypt it, and read it as a volume of an unencrypted backup.
    /// </summary>
    [Category("RecoveryTool")]
    public class RecoveryToolParityFilesTests : BasicSetupHelper
    {
        private string m_originalCurrentDirectory = "";

        // The recovery tool changes the current directory
        [SetUp]
        public void SaveCurrentDirectory()
            => m_originalCurrentDirectory = Directory.GetCurrentDirectory();

        [TearDown]
        public void RestoreCurrentDirectory()
            => Directory.SetCurrentDirectory(m_originalCurrentDirectory);

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public async Task TheParityFilesAreLeftOut(bool encrypted)
        {
            var options = new Dictionary<string, string>(TestOptions);
            if (!encrypted)
            {
                options.Remove("passphrase");
                options["no-encryption"] = "true";
            }

            for (var i = 1; i <= 2; i++)
            {
                if (i > 1)
                    await Task.Delay(TimeSpan.FromSeconds(2));
                File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), $"version {i}");
                using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                    TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
            }

            // The contents are not read, so they do not have to be made by the par2 program
            foreach (var file in Directory.GetFiles(TARGETFOLDER).Where(x => !x.Contains(".dindex.")))
                File.WriteAllBytes(file + ".par2", [.. "PAR2\0PKT"u8, .. new byte[1000]]);

            var downloaded = Path.Combine(RESTOREFOLDER, "downloaded");
            string[] passphrase = encrypted ? ["--passphrase=" + options["passphrase"]] : [];
            var (status, output) = Run(["download", "file://" + TARGETFOLDER, downloaded, .. passphrase]);
            Assert.That(status, Is.EqualTo(0), output);
            Assert.That(output, Does.Contain("4 were downloaded with 0 errors"));
            Assert.That(Directory.GetFiles(downloaded), Has.Length.EqualTo(4).And.None.Contains(".par2"));

            // The parity files can also have been copied with other tools
            foreach (var file in Directory.GetFiles(downloaded))
                File.WriteAllBytes(file + ".par2", [.. "PAR2\0PKT"u8, .. new byte[1000]]);

            foreach (var withFiles in new[] { "true", "false" })
            {
                (status, output) = Run(["index", downloaded, "--indexfile=" + Path.Combine(RESTOREFOLDER, "index.txt"), "--build-index-with-files=" + withFiles]);
                Assert.That(status, Is.EqualTo(0), output);
                Assert.That(output, Does.Not.Contain("error:").And.Not.Contain("Experienced"));
            }

            (status, output) = Run(["list", downloaded]);
            Assert.That(status, Is.EqualTo(0), output);
            Assert.That(output.Split('\n', StringSplitOptions.RemoveEmptyEntries), Has.Length.EqualTo(2), output);

            var restored = Path.Combine(RESTOREFOLDER, "restored");
            (status, output) = Run(["restore", downloaded, "1", "--targetpath=" + restored, "--indexfile=" + Path.Combine(RESTOREFOLDER, "index.txt")]);
            Assert.That(status, Is.EqualTo(0), output);
            Assert.That(File.ReadAllText(Path.Combine(restored, "file.txt")), Is.EqualTo("version 1"));
        }

        private static (int Status, string Output) Run(string[] args)
        {
            var writer = new StringWriter();
            var original = Console.Out;
            Console.SetOut(writer);
            try
            {
                return (CommandLine.RecoveryTool.Program.Main(args), writer.ToString());
            }
            finally
            {
                Console.SetOut(original);
            }
        }
    }
}
