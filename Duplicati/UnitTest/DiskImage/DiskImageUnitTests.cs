// Copyright (C) 2025, The Duplicati Team
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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.UnitTest.DiskImage;
using Duplicati.Proprietary.DiskImage;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.Filesystem;
using Duplicati.Proprietary.DiskImage.General;
using Duplicati.Proprietary.DiskImage.Partition;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

#nullable enable

namespace Duplicati.UnitTest.DiskImage
{
    /// <summary>
    /// Unit tests for the DiskImage module's internal components.
    /// These tests focus on individual component testing with a strong emphasis
    /// on unaligned reads/writes and cross-platform compatibility.
    /// </summary>
    [TestFixture]
    [Category("DiskImageUnit")]
    [Platform("Win,MacOsX,Linux")]
    public class DiskImageUnitTests : BasicSetupHelper
    {
        private IDiskImageHelper? _diskHelper;
        private string? _diskImagePath;
        private string? _diskIdentifier;
        private IRawDisk? _rawDisk;

        private const long MiB = 1024 * 1024;

        /// <summary>
        /// Sets up the test environment before each test.
        /// Creates a 50 MiB disk image with a single FAT32 partition.
        /// </summary>
        [SetUp]
        public async Task SetUp()
        {
            base.BasicHelperSetUp();

            // Create the appropriate disk image helper for the current platform
            _diskHelper = DiskImage.DiskImageHelperFactory.Create();

            // Check for admin privileges
            if (!_diskHelper.HasRequiredPrivileges())
            {
                Assert.Ignore("DiskImage tests require administrator privileges");
            }

            // Create temp disk image path
            var extension = OperatingSystem.IsWindows() ? "vhdx"
                : OperatingSystem.IsLinux() ? "img"
                : "dmg";
            _diskImagePath = Path.Combine(DATAFOLDER, $"duplicati_unit_test_{Guid.NewGuid()}.{extension}");

            // Create a 50 MiB disk image
            _diskIdentifier = _diskHelper.CreateDisk(_diskImagePath, 50 * MiB);

            // Initialize with a single FAT32 partition (cross-platform compatible)
            _diskHelper.InitializeDisk(_diskIdentifier, PartitionTableType.GPT, [(FileSystemType.FAT32, 0)]);

            // Unmount any partitions that were mounted during InitializeDisk
            _diskHelper.Unmount(_diskIdentifier);

            // Create and initialize the raw disk interface
            if (OperatingSystem.IsWindows())
            {
                _rawDisk = new Duplicati.Proprietary.DiskImage.Disk.Windows(_diskIdentifier);
            }
            else if (OperatingSystem.IsLinux())
            {
                _rawDisk = new Duplicati.Proprietary.DiskImage.Disk.Linux(_diskIdentifier);
            }
            else if (OperatingSystem.IsMacOS())
            {
                _rawDisk = new Duplicati.Proprietary.DiskImage.Disk.Mac(_diskIdentifier);
            }
            else
            {
                throw new PlatformNotSupportedException("Unsupported operating system.");
            }

            if (!await _rawDisk.InitializeAsync(true, CancellationToken.None))
            {
                throw new InvalidOperationException($"Failed to initialize raw disk: {_diskIdentifier}");
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_diskHelper is not null && _diskIdentifier is not null)
                _diskHelper.Unmount(_diskIdentifier);

            if (_diskImagePath != null && File.Exists(_diskImagePath))
            {
                File.Delete(_diskImagePath);
            }
        }

        #region IRawDisk Sector-Aligned Tests

        [Test]
        public async Task Test_RawDisk_ReadSector_ReturnsNonEmptyData()
        {
            var sectorSize = _rawDisk.SectorSize;
            using var stream = await _rawDisk.ReadSectorsAsync(0, 1, CancellationToken.None);
            var buffer = new byte[sectorSize];
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(sectorSize, bytesRead, "Should have read 1 sector.");
            bool hasData = buffer.Any(x => x != 0);
            Assert.IsTrue(hasData, "Sector 0 should contain data.");
        }

        [Test]
        public async Task Test_RawDisk_ReadBytes_ReturnsData()
        {
            var sectorSize = _rawDisk.SectorSize;
            using var stream = await _rawDisk.ReadBytesAsync(sectorSize, sectorSize, CancellationToken.None);
            Assert.AreEqual(sectorSize, stream.Length, "Should have read the correct amount of bytes.");
        }

        [Test]
        public async Task Test_RawDisk_ReadBytesAsync_CallerProvidedBuffer()
        {
            var sectorSize = _rawDisk.SectorSize;
            var buffer = new byte[sectorSize];
            var bytesRead = await _rawDisk.ReadBytesAsync(0, buffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(sectorSize, bytesRead, "Should have read the correct amount of bytes.");
        }

        [Test]
        public async Task Test_RawDisk_WriteSectors_DataMatches()
        {
            var sectorSize = _rawDisk.SectorSize;
            var writeBuffer = new byte[sectorSize];
            new Random().NextBytes(writeBuffer);
            await _rawDisk.WriteSectorsAsync(1, writeBuffer, CancellationToken.None);
            using var readStream = await _rawDisk.ReadSectorsAsync(1, 1, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(sectorSize, bytesRead, "Should have read the correct amount of bytes.");
            Assert.AreEqual(writeBuffer, readBuffer, "Data should match.");
        }

        [Test]
        public async Task Test_RawDisk_WriteBytes_DataMatches()
        {
            var sectorSize = _rawDisk.SectorSize;
            var writeBuffer = new byte[sectorSize];
            new Random().NextBytes(writeBuffer);
            await _rawDisk.WriteBytesAsync(sectorSize, writeBuffer, CancellationToken.None);
            using var readStream = await _rawDisk.ReadBytesAsync(sectorSize, sectorSize, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(sectorSize, bytesRead, "Should have read the correct amount of bytes.");
            Assert.AreEqual(writeBuffer, readBuffer, "Data should match.");
        }

        [Test]
        public async Task Test_RawDisk_WriteBytes_Memory()
        {
            var sectorSize = _rawDisk.SectorSize;
            var writeBuffer = new byte[sectorSize];
            new Random().NextBytes(writeBuffer);
            await _rawDisk.WriteBytesAsync(sectorSize, writeBuffer.AsMemory(), CancellationToken.None);
            using var readStream = await _rawDisk.ReadBytesAsync(sectorSize, sectorSize, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(sectorSize, bytesRead, "Should have read the correct amount of bytes.");
            Assert.AreEqual(writeBuffer, readBuffer, "Data should match.");
        }

        #endregion

    }

}