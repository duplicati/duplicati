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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database.Local;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A backup with parity enabled leaves a parity file next to each data volume, named
    /// "*.dlist.zip.aes.par2". The operations that look at the destination without a local
    /// database took these files for data volumes: a recreate tried to decrypt them and ended
    /// with "Detected 8 volumes with missing filesets", and a restore on another machine failed.
    /// </summary>
    /// <remarks>
    /// The parity files are not read here, so the par2 program is not needed to make them.
    /// </remarks>
    [Category("Parity")]
    public class RecreateWithParityFilesTests : BasicSetupHelper
    {
        private string Target => "file://" + TARGETFOLDER;

        private const string PARITY_SUFFIX = ".par2";

        private Dictionary<string, string> Options(bool encrypted, params (string Key, string Value)[] extra)
        {
            var opts = new Dictionary<string, string>(TestOptions);
#if DEBUG
            // Behave as for the user. The option only exists in DEBUG builds.
            opts["unittest-mode"] = "false";
#endif
            if (!encrypted)
            {
                opts.Remove("passphrase");
                opts["no-encryption"] = "true";
            }

            foreach (var (key, value) in extra)
                opts[key] = value;

            return opts;
        }

        private static bool IsParity(string path)
            => path.EndsWith(PARITY_SUFFIX, StringComparison.Ordinal);

        private string[] RemoteFiles(bool parity)
            => Directory.GetFiles(TARGETFOLDER)
                .Where(x => IsParity(x) == parity)
                .Select(x => Path.GetFileName(x)!)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

        /// <summary>
        /// Makes two versions, where each version changes the contents of a file and adds another,
        /// and adds the parity files for the dblock and dlist files
        /// </summary>
        private async Task MakeTwoVersionsWithParityFilesAsync(bool encrypted)
        {
            for (var i = 1; i <= 2; i++)
            {
                File.WriteAllText(Path.Combine(DATAFOLDER, "changed.txt"), $"version {i}");
                File.WriteAllText(Path.Combine(DATAFOLDER, $"added-{i}.txt"), $"added in {i}");

                // The versions are told apart by a time with a resolution of a second
                if (i > 1)
                    await Task.Delay(TimeSpan.FromSeconds(2));

                using (var c = new Controller(Target, Options(encrypted), null))
                    TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
            }

            foreach (var name in RemoteFiles(parity: false).Where(x => !x.Contains(".dindex.")))
                File.WriteAllBytes(Path.Combine(TARGETFOLDER, name + PARITY_SUFFIX), [.. "PAR2\0PKT"u8, .. new byte[1000]]);

            Assert.That(RemoteFiles(parity: true), Has.Length.EqualTo(4), "Expected two dblock and two dlist files");
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public async Task ARecreateIgnoresTheParityFilesAsync(bool encrypted)
        {
            await MakeTwoVersionsWithParityFilesAsync(encrypted);
            var volumes = RemoteFiles(parity: false);
            var parity = RemoteFiles(parity: true);
            File.Delete(DBFILE);

            using (var c = new Controller(Target, Options(encrypted), null))
                TestUtils.AssertResults(await c.RepairAsync());

            // Parity files are not tracked, so a row for one would make it a missing file
            await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(DBFILE, "test", true, null, CancellationToken.None))
            {
                var recorded = await db.GetRemoteVolumesAsync(CancellationToken.None).Select(x => x.Name).ToListAsync();
                Assert.That(recorded, Is.EquivalentTo(volumes));
            }

            using (var c = new Controller(Target, Options(encrypted), null))
            {
                var results = await c.ListAsync();
                TestUtils.AssertResults(results);
                Assert.That(results.Filesets.Count(), Is.EqualTo(2));
            }

            // The recreated database can be used, and the parity files are still there
            File.WriteAllText(Path.Combine(DATAFOLDER, "added-3.txt"), "added in 3");
            using (var c = new Controller(Target, Options(encrypted), null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
            Assert.That(RemoteFiles(parity: true), Is.EqualTo(parity));
        }

        /// <summary>
        /// The version numbers have to be the same with and without the parity files
        /// </summary>
        [Test]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        public async Task ARestoreWithoutADatabaseIgnoresTheParityFilesAsync(bool encrypted, int version)
        {
            await MakeTwoVersionsWithParityFilesAsync(encrypted);

            using (var c = new Controller(Target, Options(encrypted, ("no-local-db", "true"), ("version", version.ToString()), ("restore-path", RESTOREFOLDER)), null))
                TestUtils.AssertResults(await c.RestoreAsync(null));

            Assert.That(File.ReadAllText(Path.Combine(RESTOREFOLDER, "changed.txt")), Is.EqualTo($"version {2 - version}"));
            Assert.That(File.Exists(Path.Combine(RESTOREFOLDER, "added-2.txt")), Is.EqualTo(version == 0));
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public async Task TheVersionsAreListedWithoutADatabaseAsync(bool encrypted)
        {
            await MakeTwoVersionsWithParityFilesAsync(encrypted);

            using (var c = new Controller(Target, Options(encrypted, ("no-local-db", "true")), null))
            {
                var results = await c.ListAsync();
                TestUtils.AssertResults(results);
                Assert.That(results.Filesets.Select(x => x.Version), Is.EqualTo(new[] { 0L, 1L }));
            }
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public async Task TheVersionsAreComparedWithoutADatabaseAsync(bool encrypted)
        {
            await MakeTwoVersionsWithParityFilesAsync(encrypted);

            using (var c = new Controller(Target, Options(encrypted, ("no-local-db", "true")), null))
            {
                var results = await c.ListChangesAsync(null, null);
                TestUtils.AssertResults(results);
                Assert.That((results.AddedFiles, results.ModifiedFiles, results.DeletedFiles), Is.EqualTo((1L, 1L, 0L)));
            }
        }
    }
}
