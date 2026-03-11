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
using System.Linq;

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
        private IDiskImageHelper _diskHelper = null!;
        private string _diskImagePath = "";
        private string _diskIdentifier = "";
        private IRawDisk _rawDisk = null!;

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

        #region IRawDisk Unaligned Read Tests

        [Test]
        public async Task Test_RawDisk_ReadUnalignedOffset_ReturnsCorrectData()
        {
            // First write aligned data, then read at an unaligned offset
            var sectorSize = _rawDisk.SectorSize;

            // Write known pattern at sector 1
            var writeBuffer = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                writeBuffer[i] = (byte)(i & 0xFF);

            await _rawDisk.WriteSectorsAsync(1, writeBuffer, CancellationToken.None);

            // Read at offset = sector_size + 1 (unaligned)
            // The implementation should handle this by padding to sector boundaries
            var offset = sectorSize + 1;
            var length = sectorSize - 2;

            using var stream = await _rawDisk.ReadBytesAsync(offset, length, CancellationToken.None);
            var readBuffer = new byte[length];
            var bytesRead = await stream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            // Should return the data we wrote at sector 1, starting from byte offset 1
            Assert.AreEqual(length, bytesRead, "Should have read the requested length.");
            for (int i = 0; i < length; i++)
                Assert.AreEqual((byte)((i + 1) & 0xFF), readBuffer[i], $"Byte at position {i} should match.");
        }

        [Test]
        public async Task Test_RawDisk_ReadUnalignedLength_ReturnsCorrectData()
        {
            // Test reading with a length that's not a multiple of sector size
            var sectorSize = _rawDisk.SectorSize;

            // Write known pattern at sector 0
            var writeBuffer = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                writeBuffer[i] = (byte)(i & 0xFF);

            await _rawDisk.WriteSectorsAsync(0, writeBuffer, CancellationToken.None);

            // Read with unaligned length (sectorSize - 1)
            var length = sectorSize - 1;
            using var stream = await _rawDisk.ReadBytesAsync(0, length, CancellationToken.None);
            var readBuffer = new byte[length];
            var bytesRead = await stream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(length, bytesRead, "Should have read the requested length.");
            for (int i = 0; i < length; i++)
                Assert.AreEqual((byte)(i & 0xFF), readBuffer[i], $"Byte at position {i} should match.");
        }

        [Test]
        public async Task Test_RawDisk_ReadShortLength_ReturnsCorrectData()
        {
            // Read at offset 0 with length = sector_size - 1 (short read)
            var sectorSize = _rawDisk.SectorSize;

            // Write known pattern at sector 0
            var writeBuffer = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                writeBuffer[i] = (byte)(i & 0xFF);

            await _rawDisk.WriteSectorsAsync(0, writeBuffer, CancellationToken.None);

            // Read short length
            var length = sectorSize - 1;
            using var stream = await _rawDisk.ReadBytesAsync(0, length, CancellationToken.None);
            var readBuffer = new byte[length];
            var bytesRead = await stream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(length, bytesRead, "Should have read the requested short length.");
            for (int i = 0; i < length; i++)
                Assert.AreEqual((byte)(i & 0xFF), readBuffer[i], $"Byte at position {i} should match.");
        }

        [Test]
        public async Task Test_RawDisk_ReadStraddlingSectors_ReturnsCorrectData()
        {
            // Read at offset = sector_size / 2 with length = sector_size (straddles two sectors)
            var sectorSize = _rawDisk.SectorSize;

            // Write different patterns at sectors 0 and 1
            var sector0Data = new byte[sectorSize];
            var sector1Data = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
            {
                sector0Data[i] = (byte)(i | 0xF0);  // Pattern 0xF0-0xFF
                sector1Data[i] = (byte)(i | 0x0F); // Pattern 0x0F-0x1E
            }

            await _rawDisk.WriteSectorsAsync(0, sector0Data, CancellationToken.None);
            await _rawDisk.WriteSectorsAsync(1, sector1Data, CancellationToken.None);

            // Read at half-sector offset with full sector length
            var offset = sectorSize / 2;
            using var stream = await _rawDisk.ReadBytesAsync(offset, sectorSize, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await stream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize, bytesRead, "Should have read one sector.");

            // First half should be from end of sector 0
            for (int i = 0; i < sectorSize / 2; i++)
            {
                var expected = (byte)((sectorSize / 2 + i) | 0xF0);
                Assert.AreEqual(expected, readBuffer[i], $"Byte at position {i} should match sector 0 data.");
            }

            // Second half should be from start of sector 1
            for (int i = sectorSize / 2; i < sectorSize; i++)
            {
                var expected = (byte)((i - sectorSize / 2) | 0x0F);
                Assert.AreEqual(expected, readBuffer[i], $"Byte at position {i} should match sector 1 data.");
            }
        }

        [Test]
        public async Task Test_RawDisk_ReadNearEndOfDisk_ReturnsCorrectData()
        {
            // Read at the very end of the disk where remaining bytes < sector_size
            var sectorSize = _rawDisk.SectorSize;
            var diskSize = _rawDisk.Size;

            // Calculate the last sector and write data there
            var lastSector = (diskSize / sectorSize) - 1;
            var writeBuffer = new byte[sectorSize];
            new Random().NextBytes(writeBuffer);

            await _rawDisk.WriteSectorsAsync(lastSector, writeBuffer, CancellationToken.None);

            // Read from near the end
            var offset = diskSize - sectorSize;
            using var stream = await _rawDisk.ReadBytesAsync(offset, sectorSize, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await stream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize, bytesRead, "Should have read a full sector.");
            Assert.AreEqual(writeBuffer, readBuffer, "Data should match what was written.");
        }

        [Test]
        public async Task Test_RawDisk_ReadUnalignedOffsetWithMemory_ReturnsCorrectData()
        {
            // Test the Memory-based ReadBytesAsync with unaligned offset
            var sectorSize = _rawDisk.SectorSize;

            // Write known pattern at sector 1
            var writeBuffer = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                writeBuffer[i] = (byte)((i * 2) & 0xFF);

            await _rawDisk.WriteSectorsAsync(1, writeBuffer, CancellationToken.None);

            // Read at unaligned offset
            var offset = sectorSize + 4;
            var length = sectorSize - 8;
            var readBuffer = new byte[length];
            var bytesRead = await _rawDisk.ReadBytesAsync(offset, readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(length, bytesRead, "Should have read the requested length.");
            for (int i = 0; i < length; i++)
                Assert.AreEqual((byte)(((i + 4) * 2) & 0xFF), readBuffer[i], $"Byte at position {i} should match.");
        }

        [Test]
        public async Task Test_RawDisk_ReadUnalignedLengthWithMemory_ReturnsCorrectData()
        {
            // Test the Memory-based ReadBytesAsync with unaligned length
            var sectorSize = _rawDisk.SectorSize;

            // Write known pattern at sector 0
            var writeBuffer = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                writeBuffer[i] = (byte)((i + 100) & 0xFF);

            await _rawDisk.WriteSectorsAsync(0, writeBuffer, CancellationToken.None);

            // Read with unaligned length
            var length = sectorSize - 5;
            var readBuffer = new byte[length];
            var bytesRead = await _rawDisk.ReadBytesAsync(0, readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(length, bytesRead, "Should have read the requested length.");
            for (int i = 0; i < length; i++)
                Assert.AreEqual((byte)((i + 100) & 0xFF), readBuffer[i], $"Byte at position {i} should match.");
        }

        #endregion

    }
}
