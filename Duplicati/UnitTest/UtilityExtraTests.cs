using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Duplicati.Library.Main;
using System.IO;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using NUnit.Framework.Legacy;

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
    }
}
