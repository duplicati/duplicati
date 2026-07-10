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
using Duplicati.Library.Main;
using Duplicati.Library.Main.Volumes;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests the parity companion filename generation and parsing added to VolumeBase.
    /// These are pure logic and require no external programs.
    /// </summary>
    public class ParityFilenameTests : BasicSetupHelper
    {
        private const string Guid = "1234567890abcdef";
        private static readonly DateTime Timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        [Test]
        [Category("Parity")]
        public static void GenerateParityFilenameAppendsExtension()
        {
            var dataname = "duplicati-b" + Guid + ".dblock.zip.aes";
            Assert.AreEqual(dataname + ".par2", VolumeBase.GenerateParityFilename(dataname, "par2"));
        }

        [Test]
        [Category("Parity")]
        public static void ParsesEncryptedBlockParityWithoutSwallowingEncryption()
        {
            // The encrypted data volume name ends with ".aes"; the parity suffix must not
            // be captured as part of the (greedy) encryption group.
            var dataname = VolumeBase.GenerateFilename(RemoteVolumeType.Blocks, "duplicati", Guid, Timestamp, "zip", "aes");
            var parityname = VolumeBase.GenerateParityFilename(dataname, "par2");

            var parsed = VolumeBase.ParseFilename(parityname);
            Assert.IsNotNull(parsed);
            Assert.IsTrue(parsed.IsParity);
            Assert.AreEqual("par2", parsed.ParityModule);
            Assert.AreEqual(RemoteVolumeType.Blocks, parsed.FileType);
            Assert.AreEqual("duplicati", parsed.Prefix);
            Assert.AreEqual(Guid, parsed.Guid);
            Assert.AreEqual("zip", parsed.CompressionModule);
            Assert.AreEqual("aes", parsed.EncryptionModule); // not "aes.par2"
        }

        [Test]
        [Category("Parity")]
        public static void ParsesUnencryptedFilesetParity()
        {
            var dataname = VolumeBase.GenerateFilename(RemoteVolumeType.Files, "duplicati", Guid, Timestamp, "zip", null);
            var parityname = VolumeBase.GenerateParityFilename(dataname, "par2");

            var parsed = VolumeBase.ParseFilename(parityname);
            Assert.IsNotNull(parsed);
            Assert.IsTrue(parsed.IsParity);
            Assert.AreEqual(RemoteVolumeType.Files, parsed.FileType);
            Assert.AreEqual("zip", parsed.CompressionModule);
            Assert.IsNull(parsed.EncryptionModule);
        }

        [Test]
        [Category("Parity")]
        public static void ParsesNormalVolumeAsNonParity()
        {
            var dataname = VolumeBase.GenerateFilename(RemoteVolumeType.Blocks, "duplicati", Guid, Timestamp, "zip", "aes");

            var parsed = VolumeBase.ParseFilename(dataname);
            Assert.IsNotNull(parsed);
            Assert.IsFalse(parsed.IsParity);
            Assert.IsNull(parsed.ParityModule);
            Assert.AreEqual("aes", parsed.EncryptionModule);
        }

        [Test]
        [Category("Parity")]
        public static void ParityNameRoundTripsToOwningVolume()
        {
            var dataname = VolumeBase.GenerateFilename(RemoteVolumeType.Blocks, "duplicati", Guid, Timestamp, "zip", "aes");
            var parityname = VolumeBase.GenerateParityFilename(dataname, "par2");

            var dataParsed = VolumeBase.ParseFilename(dataname);
            var parityParsed = VolumeBase.ParseFilename(parityname);

            // The parity file carries the same identifying information as its data volume
            Assert.AreEqual(dataParsed.FileType, parityParsed.FileType);
            Assert.AreEqual(dataParsed.Prefix, parityParsed.Prefix);
            Assert.AreEqual(dataParsed.Guid, parityParsed.Guid);
            Assert.AreEqual(dataParsed.CompressionModule, parityParsed.CompressionModule);
            Assert.AreEqual(dataParsed.EncryptionModule, parityParsed.EncryptionModule);
        }

        [Test]
        [Category("Parity")]
        public static void NonVolumeNameReturnsNull()
        {
            Assert.IsNull(VolumeBase.ParseFilename("not-a-volume.par2"));
            Assert.IsNull(VolumeBase.ParseFilename("random.txt"));
        }
    }
}
