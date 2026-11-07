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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The recovery restore keeps up to --max-open-archives archives open. When the system allows
    /// fewer open files than that, the restore failed for every file from then on (issue #4044).
    /// </summary>
    public class RecoveryToolOpenFilesTests : BasicSetupHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RLimit
        {
            public ulong Current;
            public ulong Maximum;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int getrlimit(int resource, out RLimit limit);

        [DllImport("libc", SetLastError = true)]
        private static extern int setrlimit(int resource, ref RLimit limit);

        private string originalCurrentDirectory;

        [SetUp]
        public void SetUp()
        {
            this.originalCurrentDirectory = Directory.GetCurrentDirectory();
        }

        [TearDown]
        public void TearDown()
        {
            if (this.originalCurrentDirectory != null)
                Directory.SetCurrentDirectory(this.originalCurrentDirectory);
        }

        [Test]
        [Category("RecoveryTool")]
        public async Task RestoreFinishesWhenTheSystemAllowsFewerOpenFilesAsync()
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
                Assert.Ignore("The limit on open files is set with setrlimit");

            var source = Path.Combine(this.DATAFOLDER, "source");
            Directory.CreateDirectory(source);
            var random = new Random(4044);
            var contents = new Dictionary<string, byte[]>();
            for (var n = 0; n < 120; n++)
            {
                var data = new byte[40 * 1024];
                random.NextBytes(data);
                contents[$"f{n:000}.bin"] = data;
                File.WriteAllBytes(Path.Combine(source, $"f{n:000}.bin"), data);
            }

            // Small volumes give more archives than the open files allowed below
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["no-encryption"] = "true",
                ["blocksize"] = "10kb",
                ["dblock-size"] = "25kb",
            };
            options.Remove("passphrase");
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync([source]));
            Assert.That(Directory.GetFiles(this.TARGETFOLDER, "*.dblock.*").Length, Is.GreaterThan(100));

            var downloadFolder = Path.Combine(this.RESTOREFOLDER, "downloaded");
            var restoreFolder = Path.Combine(this.RESTOREFOLDER, "restored");
            Directory.CreateDirectory(downloadFolder);
            Directory.CreateDirectory(restoreFolder);
            Assert.That(CommandLine.RecoveryTool.Program.Main(["download", "file://" + this.TARGETFOLDER, downloadFolder]), Is.EqualTo(0));
            Assert.That(CommandLine.RecoveryTool.Program.Main(["index", downloadFolder]), Is.EqualTo(0));

            // Allow only a few more open files than are open now
            var resource = OperatingSystem.IsLinux() ? 7 : 8; // RLIMIT_NOFILE
            Assert.That(getrlimit(resource, out var original), Is.EqualTo(0));
            var openNow = Directory.GetFileSystemEntries(OperatingSystem.IsLinux() ? "/proc/self/fd" : "/dev/fd").Length;
            var lowered = new RLimit { Current = (ulong)openNow + 40, Maximum = original.Maximum };

            int status;
            Assert.That(setrlimit(resource, ref lowered), Is.EqualTo(0));
            try
            {
                status = CommandLine.RecoveryTool.Program.Main(["restore", downloadFolder, $"--targetpath={restoreFolder}"]);
            }
            finally
            {
                setrlimit(resource, ref original);
            }

            Assert.That(status, Is.EqualTo(0));
            foreach (var (name, data) in contents)
                Assert.That(File.ReadAllBytes(Path.Combine(restoreFolder, name)), Is.EqualTo(data), name);
        }
    }
}
