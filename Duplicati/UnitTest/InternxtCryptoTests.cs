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
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Duplicati.Library.Backend.Internxt;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Known-answer tests for the Internxt crypto. The expected values were
/// produced with node from a transcription of the reference JavaScript
/// (@internxt/cli CryptoService and @internxt/inxt-js crypto), so a pass
/// means the C# port agrees with the official clients, not with itself.
/// </summary>
[TestFixture]
public class InternxtCryptoTests
{
    /// <summary>
    /// The vectors shared with <see cref="InternxtBackendTests"/>
    /// </summary>
    public static class Vectors
    {
        public static readonly byte[] Salt8 = Convert.FromHexString("0102030405060708");
        public const string SaltHex = "a1b2c3d4e5f60718293a4b5c6d7e8f90";
        public const string EncryptedSalt = "53616c7465645f5f01020304050607084cfce9e79e463649676aeab0e168da5d0194fefe69d2e61f9549ee49913084e5f05b65ff42e4b9cc9bac9dc9939c64aa";
        public const string Password = "correct horse battery staple";
        public const string PasswordHash = "572d082b9caa5e447a711c553802ffaab01e8aaf40cec21b0a969b396dd3788c";
        public const string EncryptedPasswordHash = "53616c7465645f5f0102030405060708030d0a19469f1a5ea6e2bac60bcbd42ecdf8143e7a510bef60cd4acde96cf6909b2e4cb6cc0ba58d7fcd02e141a5428ac07f4e8a96bddc42dc9eb5b8628f54d2e96ea53b850d23cf997e383061a925e2";
        public const string Mnemonic = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
        public const string EncryptedMnemonic = "53616c7465645f5f010203040506070892a646f008ba3c370083e6ccb46621b4e5d99c73fd0f59fe07248a9f05fc200e989c5ee6c0b66c525d9b1d18396a294c9914de2f36285887789d92bffd63c8855b74fc57320dac980106cd0e49a8cf312ac4014f15518e8a0e1636dfb5608979";
        public const string SeedHex = "5eb00bbddcf069084889a8ab9155568165f5c453ccb85e70811aaed6f6da5fc19a5ac40b389cd370d086206dec8aa6c43daea6690f20ad3d8d48b2d2ce9e38e4";
        public const string BucketId = "5f1a2b3c4d5e6f708192a3b4";
        public const string IndexHex = "00112233445566778899aabbccddeeff102132435465768798a9bacbdcedfe0f";
        public const string FileKeyHex = "603063994b435dd6fa69dfa778c599c40108dc1e136da55898fdc99f665fda46";
        public const string IvHex = "00112233445566778899aabbccddeeff";
        public const string PlainText = "Duplicati Internxt backend known-answer vector, 48 bytes!!";
        public const string CipherTextHex = "34beed83d61681b2b5753d1c511c3c1cb8f63a2aad73b7d8a3f3e859ff890b33ca08c373bf6220d779e847a44eac8df6bbc6e91b1fa30017efe4";
        public const string ShardHash = "0c9ba1d3216fed6f8fccd1d945b3eef6eda0075e";
        public const string UserId = "1234567890";
        public const string BridgeUser = "bridge@example.invalid";
        public const string BridgePasswordHex = "c775e7b757ede630cd0aa1113bd102661ab38829ca52a6422ab782862f268646";
    }

    [Test]
    [Category("Backend")]
    public void EncryptTextMatchesCryptoJs()
    {
        Assert.AreEqual(Vectors.EncryptedSalt, InternxtCrypto.EncryptText(Vectors.SaltHex, InternxtCrypto.AppCryptoSecret, Vectors.Salt8));
        Assert.AreEqual(Vectors.EncryptedPasswordHash, InternxtCrypto.EncryptText(Vectors.PasswordHash, InternxtCrypto.AppCryptoSecret, Vectors.Salt8));
        Assert.AreEqual(Vectors.EncryptedMnemonic, InternxtCrypto.EncryptText(Vectors.Mnemonic, Vectors.Password, Vectors.Salt8));
    }

    [Test]
    [Category("Backend")]
    public void DecryptTextMatchesCryptoJs()
    {
        Assert.AreEqual(Vectors.SaltHex, InternxtCrypto.DecryptText(Vectors.EncryptedSalt, InternxtCrypto.AppCryptoSecret));
        Assert.AreEqual(Vectors.Mnemonic, InternxtCrypto.DecryptText(Vectors.EncryptedMnemonic, Vectors.Password));
    }

    [Test]
    [Category("Backend")]
    public void DecryptTextWithWrongPasswordThrows()
    {
        Assert.Throws<CryptographicException>(() => InternxtCrypto.DecryptText(Vectors.EncryptedMnemonic, "not the password"));
        Assert.Throws<CryptographicException>(() => InternxtCrypto.DecryptText("00112233", Vectors.Password));
    }

    [Test]
    [Category("Backend")]
    public void PasswordHashMatchesPbkdf2Sha1()
    {
        Assert.AreEqual(Vectors.PasswordHash, InternxtCrypto.PasswordHash(Vectors.Password, Vectors.SaltHex));
    }

    [Test]
    [Category("Backend")]
    public void EncryptPasswordHashRoundTrips()
    {
        // The salt is random, so compare after unwrapping
        var wrapped = InternxtCrypto.EncryptPasswordHash(Vectors.Password, Vectors.EncryptedSalt);
        Assert.AreEqual(Vectors.PasswordHash, InternxtCrypto.DecryptText(wrapped, InternxtCrypto.AppCryptoSecret));
    }

    [Test]
    [Category("Backend")]
    public void Bip39SeedMatchesReferenceVector()
    {
        Assert.AreEqual(Vectors.SeedHex, Convert.ToHexString(InternxtCrypto.Bip39Seed(Vectors.Mnemonic)).ToLowerInvariant());
    }

    [Test]
    [Category("Backend")]
    public void FileKeyDerivationMatchesInxtJs()
    {
        var (key, iv) = InternxtCrypto.DeriveFileKey(Convert.FromHexString(Vectors.SeedHex), Vectors.BucketId, Convert.FromHexString(Vectors.IndexHex));
        Assert.AreEqual(Vectors.FileKeyHex, Convert.ToHexString(key).ToLowerInvariant());
        Assert.AreEqual(Vectors.IvHex, Convert.ToHexString(iv).ToLowerInvariant());
    }

    [Test]
    [Category("Backend")]
    public async Task AesCtrStreamMatchesNode()
    {
        var key = Convert.FromHexString(Vectors.FileKeyHex);
        var iv = Convert.FromHexString(Vectors.IvHex);
        var plain = Encoding.UTF8.GetBytes(Vectors.PlainText);

        using var encrypted = new AesCtrStream(new MemoryStream(plain), key, iv, false);
        using var ms = new MemoryStream();
        await encrypted.CopyToAsync(ms);
        Assert.AreEqual(Vectors.CipherTextHex, Convert.ToHexString(ms.ToArray()).ToLowerInvariant());

        // CTR is symmetric
        using var decrypted = new AesCtrStream(new MemoryStream(ms.ToArray()), key, iv, false);
        using var reader = new StreamReader(decrypted, Encoding.UTF8);
        Assert.AreEqual(Vectors.PlainText, await reader.ReadToEndAsync());
    }

    [Test]
    [Category("Backend")]
    public void AesCtrStreamContinuesTheCounterAtAnOffset()
    {
        var key = Convert.FromHexString(Vectors.FileKeyHex);
        var iv = Convert.FromHexString(Vectors.IvHex);
        var plain = Encoding.UTF8.GetBytes(Vectors.PlainText);
        var expected = Convert.FromHexString(Vectors.CipherTextHex);

        // Block aligned and unaligned starting points, read with odd buffer sizes
        foreach (var offset in new[] { 16, 32, 7, 33 })
        {
            using var encrypted = new AesCtrStream(new MemoryStream(plain, offset, plain.Length - offset), key, iv, offset, false);
            var output = new byte[plain.Length - offset];
            var pos = 0;
            while (pos < output.Length)
            {
                var n = encrypted.Read(output, pos, Math.Min(5, output.Length - pos));
                Assert.Greater(n, 0);
                pos += n;
            }
            Assert.AreEqual(Convert.ToHexString(expected.AsSpan(offset)), Convert.ToHexString(output), $"offset {offset}");
        }
    }

    [Test]
    [Category("Backend")]
    public async Task ShardHashIsRipemd160OfSha256()
    {
        using var hashing = new Sha256ReadStream(new MemoryStream(Convert.FromHexString(Vectors.CipherTextHex)), false);
        await hashing.CopyToAsync(Stream.Null);
        Assert.AreEqual(Vectors.ShardHash, InternxtCrypto.ShardHash(hashing.GetHash()));
    }

    [Test]
    [Category("Backend")]
    public void NetworkBasicAuthHashesTheUserId()
    {
        var header = InternxtCrypto.NetworkBasicAuth(Vectors.BridgeUser, Vectors.UserId);
        Assert.IsTrue(header.StartsWith("Basic "));
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..]));
        Assert.AreEqual($"{Vectors.BridgeUser}:{Vectors.BridgePasswordHex}", decoded);
    }

    [Test]
    [Category("Backend")]
    public void NamesSplitAtTheLastDot()
    {
        Assert.AreEqual(("duplicati-b0123.dblock.zip", "aes"), InternxtClient.SplitName("duplicati-b0123.dblock.zip.aes"));
        Assert.AreEqual(("noextension", ""), InternxtClient.SplitName("noextension"));
        Assert.AreEqual((".hidden", ""), InternxtClient.SplitName(".hidden"));
        Assert.AreEqual(("trailing.", ""), InternxtClient.SplitName("trailing."));
        Assert.AreEqual("duplicati-b0123.dblock.zip.aes", InternxtClient.JoinName("duplicati-b0123.dblock.zip", "aes"));
        Assert.AreEqual("noextension", InternxtClient.JoinName("noextension", null));
        Assert.AreEqual("noextension", InternxtClient.JoinName("noextension", ""));
    }

    /// <summary>
    /// RFC 6238 appendix B vectors for HMAC-SHA1 with the 20-byte ASCII secret
    /// "12345678901234567890", truncated from 8 to 6 digits
    /// </summary>
    [Test]
    [Category("Backend")]
    public void TotpMatchesRfc6238Vectors()
    {
        const string secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        Assert.AreEqual("287082", InternxtTotp.Compute(secret, DateTimeOffset.FromUnixTimeSeconds(59)));
        Assert.AreEqual("081804", InternxtTotp.Compute(secret, DateTimeOffset.FromUnixTimeSeconds(1111111109)));
        Assert.AreEqual("050471", InternxtTotp.Compute(secret, DateTimeOffset.FromUnixTimeSeconds(1111111111)));
        Assert.AreEqual("005924", InternxtTotp.Compute(secret, DateTimeOffset.FromUnixTimeSeconds(1234567890)));
        Assert.AreEqual("279037", InternxtTotp.Compute(secret, DateTimeOffset.FromUnixTimeSeconds(2000000000)));
        Assert.AreEqual("353130", InternxtTotp.Compute(secret, DateTimeOffset.FromUnixTimeSeconds(20000000000)));
    }

    [Test]
    [Category("Backend")]
    public void TotpSecretAcceptsAuthenticatorFormatting()
    {
        var reference = InternxtTotp.Compute("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", DateTimeOffset.FromUnixTimeSeconds(59));
        Assert.AreEqual(reference, InternxtTotp.Compute("gezd gnbv gy3t qojq gezd gnbv gy3t qojq", DateTimeOffset.FromUnixTimeSeconds(59)));
        Assert.AreEqual(reference, InternxtTotp.Compute("GEZD-GNBV-GY3T-QOJQ-GEZD-GNBV-GY3T-QOJQ====", DateTimeOffset.FromUnixTimeSeconds(59)));
        Assert.Throws<FormatException>(() => InternxtTotp.Compute("not base32 1890", DateTimeOffset.UtcNow));
        Assert.Throws<FormatException>(() => InternxtTotp.Compute("   ", DateTimeOffset.UtcNow));
    }

    [Test]
    [Category("Backend")]
    public void MnemonicShapeCheck()
    {
        Assert.IsTrue(InternxtCrypto.LooksLikeMnemonic(Vectors.Mnemonic));
        Assert.IsFalse(InternxtCrypto.LooksLikeMnemonic("only three words here"));
        Assert.IsFalse(InternxtCrypto.LooksLikeMnemonic("\u0012\u0003garbage " + Vectors.Mnemonic));
    }
}
