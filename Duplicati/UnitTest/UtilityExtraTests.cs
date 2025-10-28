using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Interface;
using System.IO;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using NUnit.Framework.Legacy;
using TempFile = Duplicati.Library.Utility.TempFile;

#nullable enable

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class UtilityExtraTests
    {
        [Test]
        [Category("Utility")]
        public void WrapMetadataReturnsExpectedValues()
        {
            var options = new Options(new Dictionary<string, string?>
            {
                { "file-hash-algorithm", Library.Utility.HashFactory.MD5 }
            });
            var values = new Dictionary<string, string> { { "a", "b" } };

            var metahash = Utility.WrapMetadata(values, options);

            var json = System.Text.Json.JsonSerializer.Serialize(values);

            using var ms = new MemoryStream();
            ms.Write(Encoding.UTF8.GetPreamble());
            ms.Write(Encoding.UTF8.GetBytes(json));
            ms.Position = 0; // Reset position for reading

            using var hasher = Library.Utility.HashFactory.CreateHasher(Library.Utility.HashFactory.MD5);
            var expectedHash = Convert.ToBase64String(hasher.ComputeHash(ms));

            Assert.AreEqual(expectedHash, metahash.FileHash);
            var decodedString = Encoding.UTF8.GetString(metahash.Blob.AsSpan(Encoding.UTF8.GetPreamble().Length));
            Assert.AreEqual(json, decodedString);
            CollectionAssert.AreEquivalent(values, metahash.Values);
        }

        [Test]
        [Category("Utility")]
        public void WrapMetadataWithInvalidAlgorithmThrows()
        {
            var options = new Options(new Dictionary<string, string?>
            {
                { "file-hash-algorithm", "invalid" }
            });
            Assert.Throws<ArgumentException>(() => Utility.WrapMetadata(new Dictionary<string, string>(), options));
        }

        [Test]
        [Category("Utility")]
        public async Task UpdateOptionsFromDbPopulatesMissingValues()
        {
            using var tempDb = new TempFile();
            await using var db = await LocalDatabase.CreateLocalDatabaseAsync(tempDb, "test", true, null, CancellationToken.None);
            await db.SetDbOptions(new Dictionary<string, string>
            {
                { "blocksize", "16384" },
                { "blockhash", Library.Utility.HashFactory.MD5 },
                { "filehash", "SHA512" }
            }, CancellationToken.None);

            var options = new Options(new Dictionary<string, string?>());

            await Utility.UpdateOptionsFromDb(db, options, CancellationToken.None);

            Assert.That(options.RawOptions.TryGetValue("blocksize", out var blocksize), Is.True);
            Assert.That(blocksize, Is.EqualTo("16384b"));
            Assert.That(options.RawOptions.TryGetValue("block-hash-algorithm", out var blockHash), Is.True);
            Assert.That(blockHash, Is.EqualTo(Library.Utility.HashFactory.MD5));
            Assert.That(options.RawOptions.TryGetValue("file-hash-algorithm", out var fileHash), Is.True);
            Assert.That(fileHash, Is.EqualTo("SHA512"));
        }

        [Test]
        [Category("Utility")]
        public async Task UpdateOptionsFromDbRespectsExistingValues()
        {
            using var tempDb = new TempFile();
            await using var db = await LocalDatabase.CreateLocalDatabaseAsync(tempDb, "test", true, null, CancellationToken.None);
            await db.SetDbOptions(new Dictionary<string, string>
            {
                { "blocksize", "4096" },
                { "blockhash", Library.Utility.HashFactory.SHA1 },
                { "filehash", "SHA256" }
            }, CancellationToken.None);

            var options = new Options(new Dictionary<string, string?>
            {
                { "blocksize", "32kb" },
                { "block-hash-algorithm", Library.Utility.HashFactory.SHA512 },
                { "file-hash-algorithm", "RIPEMD160" }
            });

            await Utility.UpdateOptionsFromDb(db, options, CancellationToken.None);

            Assert.That(options.RawOptions["blocksize"], Is.EqualTo("32kb"));
            Assert.That(options.RawOptions["block-hash-algorithm"], Is.EqualTo(Library.Utility.HashFactory.SHA512));
            Assert.That(options.RawOptions["file-hash-algorithm"], Is.EqualTo("RIPEMD160"));
        }

        [Test]
        [Category("Utility")]
        public async Task ContainsOptionsForVerificationDetectsSensitiveEntries()
        {
            using var tempDb = new TempFile();
            await using var db = await LocalDatabase.CreateLocalDatabaseAsync(tempDb, "test", true, null, CancellationToken.None);
            await db.SetDbOptions(new Dictionary<string, string>
            {
                { "filehash", "SHA1" }
            }, CancellationToken.None);

            var result = await Utility.ContainsOptionsForVerification(db, CancellationToken.None);

            Assert.That(result, Is.True);
        }

        [Test]
        [Category("Utility")]
        public async Task ContainsOptionsForVerificationReturnsFalseWhenOptionsAbsent()
        {
            using var tempDb = new TempFile();
            await using var db = await LocalDatabase.CreateLocalDatabaseAsync(tempDb, "test", true, null, CancellationToken.None);
            await db.SetDbOptions(new Dictionary<string, string>(), CancellationToken.None);

            var result = await Utility.ContainsOptionsForVerification(db, CancellationToken.None);

            Assert.That(result, Is.False);
        }

        [Test]
        [Category("Utility")]
        public async Task VerifyOptionsThrowsWhenAddingPassphraseWithoutPermission()
        {
            using var tempDb = new TempFile();
            await using var db = await LocalDatabase.CreateLocalDatabaseAsync(tempDb, "test", true, null, CancellationToken.None);
            await db.SetDbOptions(new Dictionary<string, string>
            {
                { "blocksize", "16384" },
                { "blockhash", Library.Utility.HashFactory.SHA256 },
                { "filehash", Library.Utility.HashFactory.SHA256 },
                { "passphrase", "no-encryption" },
                { "passphrase-salt", "existing-salt" }
            }, CancellationToken.None);

            var options = new Options(new Dictionary<string, string?>
            {
                { "blocksize", "16kb" },
                { "block-hash-algorithm", Library.Utility.HashFactory.SHA256 },
                { "file-hash-algorithm", Library.Utility.HashFactory.SHA256 },
                { "passphrase", "secret" }
            });

            var ex = Assert.ThrowsAsync<UserInformationException>(async () =>
                await Utility.VerifyOptionsAndUpdateDatabase(db, options, CancellationToken.None));

            Assert.That(ex?.Message, Does.Contain("add a passphrase"));

            var storedOptions = await db.GetDbOptions(CancellationToken.None);
            Assert.That(storedOptions["passphrase"], Is.EqualTo("no-encryption"));
        }
    }
}
