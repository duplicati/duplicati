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
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Duplicati.Library.Encryption;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Volumes;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A backup that crashes while the filelist is being uploaded (out of memory or out of disk
    /// in the report) can leave a zero-byte filelist at the destination. Recreating the database
    /// then registered the empty file as a filelist volume, failed to read it, and ended with
    /// "Detected 1 volume with missing filesets", leaving a database that is marked as being
    /// under repair. Renaming the file to "*.dlist.zip.aes.BAK" did not help, as that name was
    /// still parsed as a filelist.
    /// </summary>
    public class RecreateWithEmptyFilelistTests : BasicSetupHelper
    {
        private string Target => "file://" + TARGETFOLDER;

        /// <summary>
        /// The options the user runs with: outside of unittest mode a filelist that cannot be
        /// read is logged and skipped, rather than aborting the recreate right away.
        /// </summary>
        private Dictionary<string, string> UserOptions
        {
            get
            {
                var opts = new Dictionary<string, string>(TestOptions);
#if DEBUG
                // The option only exists in DEBUG builds.
                opts["unittest-mode"] = "false";
#endif
                return opts;
            }
        }

        private string[] RemoteFilelists()
            => Directory.GetFiles(TARGETFOLDER)
                .Select(Path.GetFileName)
                .Where(x => x.Contains(".dlist."))
                .OrderBy(x => x)
                .ToArray();

        /// <summary>
        /// Makes a good backup, and then leaves what the crashed backup left behind:
        /// a zero-byte filelist that is newer than the good one, and no local database.
        /// </summary>
        /// <param name="suffix">Text appended to the name of the empty filelist</param>
        /// <param name="newest">True if the empty filelist is newer than the good one, as in the report</param>
        /// <returns>The name of the empty filelist</returns>
        private Task<string> BackupThenAddAnEmptyFilelistAsync(string suffix, bool newest = true)
            => BackupThenAddAFilelistAsync(_ => [], suffix, newest);

        /// <summary>
        /// Makes a good backup, and then adds another filelist next to the good one,
        /// and removes the local database.
        /// </summary>
        /// <param name="contents">Gives the contents of the added filelist, from the path of the good one</param>
        /// <param name="suffix">Text appended to the name of the added filelist</param>
        /// <param name="newest">True if the added filelist is newer than the good one</param>
        /// <returns>The name of the added filelist</returns>
        private async Task<string> BackupThenAddAFilelistAsync(Func<string, byte[]> contents, string suffix = "", bool newest = true)
        {
            Directory.CreateDirectory(Path.Combine(DATAFOLDER, "folder"));
            for (var i = 0; i < 5; i++)
                File.WriteAllText(Path.Combine(DATAFOLDER, "folder", $"file-{i}.txt"), $"contents {i}");

            using (var c = new Controller(Target, TestOptions, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            var good = RemoteFilelists();
            Assert.That(good.Length, Is.EqualTo(1), "Expected the backup to write a single filelist");

            // Same name as the good filelist, but with another timestamp
            var time = VolumeBase.ParseFilename(good[0]).Time.AddHours(newest ? 1 : -1);
            var addedname = Regex.Replace(good[0], @"\d{8}T\d{6}Z", Library.Utility.Utility.SerializeDateTime(time)) + suffix;
            Assert.That(addedname, Is.Not.EqualTo(good[0]));
            File.WriteAllBytes(Path.Combine(TARGETFOLDER, addedname), contents(Path.Combine(TARGETFOLDER, good[0])));

            File.Delete(DBFILE);

            return addedname;
        }

        /// <summary>
        /// Encrypts the data the way the backup does
        /// </summary>
        private byte[] Encrypt(byte[] data)
        {
            using var aes = new AESEncryption(TestOptions["passphrase"], new Dictionary<string, string>());
            using var output = new MemoryStream();
            aes.Encrypt(new MemoryStream(data), output);
            return output.ToArray();
        }

        /// <summary>
        /// Gives a filelist that starts out like the good one, with a list of
        /// files that ends in the middle of an entry
        /// </summary>
        private byte[] WithAFilelistThatEndsEarly(string goodpath)
        {
            using var aes = new AESEncryption(TestOptions["passphrase"], new Dictionary<string, string>());
            using var decrypted = new MemoryStream();
            using (var fs = File.OpenRead(goodpath))
                aes.Decrypt(fs, decrypted);
            decrypted.Position = 0;

            using var result = new MemoryStream();
            using (var source = new ZipArchive(decrypted, ZipArchiveMode.Read))
            using (var target = new ZipArchive(result, ZipArchiveMode.Create, true))
                foreach (var entry in source.Entries)
                {
                    using var data = new MemoryStream();
                    using (var es = entry.Open())
                        es.CopyTo(data);

                    var bytes = data.ToArray();
                    if (entry.FullName == "filelist.json")
                    {
                        // Cut after the second to last entry starts, so some are read before it fails
                        var text = System.Text.Encoding.UTF8.GetString(bytes);
                        var cut = text.LastIndexOf("{\"type\"", StringComparison.Ordinal);
                        Assert.That(cut, Is.GreaterThan(text.IndexOf("{\"type\"", StringComparison.Ordinal)), "Expected more than one entry in the filelist");
                        bytes = System.Text.Encoding.UTF8.GetBytes(text.Substring(0, cut + 10));
                    }

                    using var ts = target.CreateEntry(entry.FullName).Open();
                    ts.Write(bytes);
                }

            return Encrypt(result.ToArray());
        }

        /// <summary>
        /// Recreates the database and checks that the good version is there and usable.
        /// </summary>
        /// <param name="unreadable">The name of the filelist that the recreate has to skip, or null if it is not seen as a filelist</param>
        private async Task AssertRecreateRecoversTheGoodVersionAsync(string unreadable)
        {
            using (var c = new Controller(Target, UserOptions, null))
            {
                var results = await c.RepairAsync();
                Assert.That(results.Errors, Is.Empty, "The recreate reported errors");
                if (unreadable != null)
                    Assert.That(results.Warnings.Any(x => x.Contains(unreadable) && x.Contains("repair-ignore-outdated-database")), Is.True,
                        "The recreate skipped the filelist without saying so");
            }

            // The flag that is left behind by a failed recreate is what gives
            // "the database is marked as being under repair" on the next operation
            using (var c = new Controller(Target, UserOptions, null))
            {
                var results = await c.ListAsync();
                Assert.That(results.Errors, Is.Empty, "Listing the recreated database reported errors");
                Assert.That(results.Filesets.Count(), Is.EqualTo(1), "Expected only the good version to be found");
            }

            File.WriteAllText(Path.Combine(DATAFOLDER, "folder", "file-new.txt"), "new contents");

            if (unreadable != null)
            {
                // The recreate does not remove anything from the destination, so the file is
                // there as a file the database does not know, which is for a repair to remove
                Assert.That(File.Exists(Path.Combine(TARGETFOLDER, unreadable)), Is.True,
                    "The recreate removed the unreadable filelist from the destination");

                using (var c = new Controller(Target, UserOptions, null))
                    Assert.ThrowsAsync<RemoteListVerificationException>(async () => await c.BackupAsync([DATAFOLDER]));

                // A filelist that is newer than the versions in the database makes the database
                // look outdated to the repair, which is what the recreate warning gives the option for
                var repairOptions = UserOptions;
                repairOptions["repair-ignore-outdated-database"] = "true";
                using (var c = new Controller(Target, repairOptions, null))
                {
                    var results = await c.RepairAsync();
                    Assert.That(results.Errors, Is.Empty, "The repair reported errors");
                }

                Assert.That(File.Exists(Path.Combine(TARGETFOLDER, unreadable)), Is.False,
                    "The repair left the unreadable filelist at the destination");
            }

            using (var c = new Controller(Target, UserOptions, null))
            {
                var results = await c.BackupAsync([DATAFOLDER]);
                Assert.That(results.Errors, Is.Empty, "The backup after the recreate reported errors");
            }

            using (var c = new Controller(Target, UserOptions, null))
            {
                var results = await c.ListAsync();
                Assert.That(results.Filesets.Count(), Is.EqualTo(2), "Expected the good version and the new one");
            }
        }

        /// <summary>
        /// Step 1 and 3 of the report: the empty filelist is at the destination under its
        /// original name, and the recreate has to get past it.
        /// </summary>
        /// <remarks>
        /// The filelists are read oldest first, and a failure to decrypt the first one is
        /// reported as a wrong passphrase, so an empty filelist that is older than the good
        /// one fails in a different place than the one in the report.
        /// </remarks>
        [Test]
        [Category("RepairHandler")]
        [TestCase(true)]
        [TestCase(false)]
        public async Task ARecreateSucceedsWithAnEmptyFilelistAsync(bool newest)
        {
            var name = await BackupThenAddAnEmptyFilelistAsync("", newest);
            await AssertRecreateRecoversTheGoodVersionAsync(name);
        }

        /// <summary>
        /// Step 4 of the report: renaming the empty filelist to get it out of the way
        /// has to take it out of the recreate.
        /// </summary>
        [Test]
        [Category("RepairHandler")]
        public async Task ARecreateIgnoresARenamedFilelistAsync()
        {
            var name = await BackupThenAddAnEmptyFilelistAsync(".BAK");
            await AssertRecreateRecoversTheGoodVersionAsync(null);

            Assert.That(File.Exists(Path.Combine(TARGETFOLDER, name)), Is.True,
                "A file that is not part of the backup was removed from the destination");
        }

        /// <summary>
        /// An empty filelist is one way of being unreadable, and these are the others:
        /// not encrypted at all, cut short, not an archive, and an archive that turns out
        /// to be damaged after some of it has been read.
        /// </summary>
        [Test]
        [Category("RepairHandler")]
        [TestCase("garbage", true)]
        [TestCase("garbage", false)]
        [TestCase("truncated", true)]
        [TestCase("truncated", false)]
        [TestCase("not-an-archive", true)]
        [TestCase("not-an-archive", false)]
        [TestCase("filelist-ends-early", true)]
        [TestCase("filelist-ends-early", false)]
        public async Task ARecreateSucceedsWithAnUnreadableFilelistAsync(string kind, bool newest)
        {
            var garbage = Enumerable.Range(0, 2000).Select(x => (byte)(x * 7)).ToArray();
            var name = await BackupThenAddAFilelistAsync(goodpath => kind switch
            {
                "garbage" => garbage,
                "truncated" => File.ReadAllBytes(goodpath).Take((int)(new FileInfo(goodpath).Length / 2)).ToArray(),
                "not-an-archive" => Encrypt(garbage),
                "filelist-ends-early" => WithAFilelistThatEndsEarly(goodpath),
                _ => throw new ArgumentException(kind)
            }, newest: newest);

            await AssertRecreateRecoversTheGoodVersionAsync(name);
        }

        /// <summary>
        /// When no filelist can be decrypted it is the passphrase that is wrong,
        /// and that must not end with a database that has no versions in it.
        /// </summary>
        [Test]
        [Category("RepairHandler")]
        public async Task ARecreateWithTheWrongPassphraseStillFailsAsync()
        {
            await BackupThenAddAnEmptyFilelistAsync("");

            var opts = UserOptions;
            opts["passphrase"] = "not-the-passphrase";

            using var c = new Controller(Target, opts, null);
            Assert.ThrowsAsync<CryptographicException>(async () => await c.RepairAsync());
        }

        /// <summary>
        /// The reason the renamed file was picked up: anything after the compression module
        /// was accepted as the name of the encryption module.
        /// </summary>
        /// <remarks>
        /// A name like "*.dlist.zip.bak" is not covered, as it cannot be told from a volume
        /// that is encrypted with a module named "bak". That one is an unreadable filelist.
        /// </remarks>
        [Test]
        [Category("RepairHandler")]
        [TestCase("duplicati-20260927T040000Z.dlist.zip.aes.BAK")]
        [TestCase("duplicati-20260927T040000Z.dlist.zip.aes.bak")]
        [TestCase("duplicati-20260927T040000Z.dlist.zip.aes (1)")]
        [TestCase("duplicati-b0123456789abcdef0123456789abcdef.dblock.zip.aes.old")]
        [TestCase("duplicati-i0123456789abcdef0123456789abcdef.dindex.zip.aes.part")]
        public void ARenamedVolumeIsNotParsedAsAVolume(string filename)
        {
            Assert.That(VolumeBase.ParseFilename(filename), Is.Null);
        }

        /// <summary>
        /// Guards the test above against a fix that rejects the names Duplicati writes.
        /// </summary>
        [Test]
        [Category("RepairHandler")]
        [TestCase("duplicati-20260927T040000Z.dlist.zip.aes", "aes")]
        [TestCase("duplicati-20260927T040000Z.dlist.zip.gpg", "gpg")]
        [TestCase("duplicati-20260927T040000Z.dlist.zip", null)]
        [TestCase("duplicati-b0123456789abcdef0123456789abcdef.dblock.zip.aes", "aes")]
        public void AnOrdinaryVolumeIsStillParsed(string filename, string encryption)
        {
            var parsed = VolumeBase.ParseFilename(filename);
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.CompressionModule, Is.EqualTo("zip"));
            Assert.That(parsed.EncryptionModule, Is.EqualTo(encryption));
        }
    }
}
