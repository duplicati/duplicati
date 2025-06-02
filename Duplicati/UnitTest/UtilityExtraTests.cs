using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Duplicati.Library.Main;
using Duplicati.Library.Utility;

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
                { "file-hash-algorithm", HashFactory.MD5 }
            });
            var values = new Dictionary<string, string> { { "a", "b" } };

            var metahash = Utility.WrapMetadata(values, options);

            var json = System.Text.Json.JsonSerializer.Serialize(values);
            using var hasher = HashFactory.CreateHasher(HashFactory.MD5);
            var expectedHash = Convert.ToBase64String(hasher.ComputeHash(Encoding.UTF8.GetBytes(json)));

            Assert.AreEqual(expectedHash, metahash.FileHash);
            Assert.AreEqual(json, Encoding.UTF8.GetString(metahash.Blob));
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
