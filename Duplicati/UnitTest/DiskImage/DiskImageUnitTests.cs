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

        #region IRawDisk Unaligned Write Tests

        [Test]
        public async Task Test_RawDisk_WriteUnalignedOffset_DataMatches()
        {
            // Write at an offset that is NOT sector-aligned
            var sectorSize = _rawDisk.SectorSize;

            // First, write known patterns to two consecutive sectors
            var sector1Data = new byte[sectorSize];
            var sector2Data = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
            {
                sector1Data[i] = (byte)(i | 0xA0);
                sector2Data[i] = (byte)(i | 0xB0);
            }

            await _rawDisk.WriteSectorsAsync(1, sector1Data, CancellationToken.None);
            await _rawDisk.WriteSectorsAsync(2, sector2Data, CancellationToken.None);

            // Now write at unaligned offset (sector_size + 5)
            var unalignedOffset = sectorSize + 5;
            var writeData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            var bytesWritten = await _rawDisk.WriteBytesAsync(unalignedOffset, writeData, CancellationToken.None);

            Assert.AreEqual(writeData.Length, bytesWritten, "Should have written all bytes.");

            // Read back both sectors and verify only the intended bytes were changed
            using var readStream = await _rawDisk.ReadSectorsAsync(1, 2, CancellationToken.None);
            var readBuffer = new byte[sectorSize * 2];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize * 2, bytesRead, "Should have read two sectors.");

            // Verify sector 1 data (first 5 bytes should be unchanged, then our written data)
            for (int i = 0; i < 5; i++)
                Assert.AreEqual((byte)(i | 0xA0), readBuffer[i], $"Byte at position {i} in sector 1 should be unchanged.");
            for (int i = 0; i < writeData.Length; i++)
                Assert.AreEqual(writeData[i], readBuffer[5 + i], $"Written byte at position {5 + i} should match.");
            for (int i = 5 + writeData.Length; i < sectorSize; i++)
                Assert.AreEqual((byte)(i | 0xA0), readBuffer[i], $"Byte at position {i} in sector 1 should be unchanged.");

            // Verify sector 2 data (should be completely unchanged)
            for (int i = 0; i < sectorSize; i++)
                Assert.AreEqual((byte)(i | 0xB0), readBuffer[sectorSize + i], $"Byte at position {i} in sector 2 should be unchanged.");
        }

        [Test]
        public async Task Test_RawDisk_WriteUnalignedLength_DataMatches()
        {
            // Write data whose length is NOT a multiple of sector size
            var sectorSize = _rawDisk.SectorSize;

            // Write known pattern to sector 3
            var sectorData = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                sectorData[i] = (byte)(i | 0xC0);

            await _rawDisk.WriteSectorsAsync(3, sectorData, CancellationToken.None);

            // Write unaligned length (sectorSize - 10)
            var writeLength = sectorSize - 10;
            var writeData = new byte[writeLength];
            for (int i = 0; i < writeLength; i++)
                writeData[i] = (byte)((i + 50) & 0xFF);

            var bytesWritten = await _rawDisk.WriteBytesAsync(3 * sectorSize, writeData, CancellationToken.None);

            Assert.AreEqual(writeData.Length, bytesWritten, "Should have written all bytes.");

            // Read back and verify
            using var readStream = await _rawDisk.ReadSectorsAsync(3, 1, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize, bytesRead, "Should have read one sector.");

            // First part should be our written data
            for (int i = 0; i < writeLength; i++)
                Assert.AreEqual((byte)((i + 50) & 0xFF), readBuffer[i], $"Byte at position {i} should match written data.");

            // Remaining bytes should be unchanged (the padding logic preserves existing data)
            for (int i = writeLength; i < sectorSize; i++)
                Assert.AreEqual((byte)(i | 0xC0), readBuffer[i], $"Byte at position {i} should be unchanged.");
        }

        [Test]
        public async Task Test_RawDisk_WriteSingleByte_VerifySingleByteChanged()
        {
            // Write a single byte at a sector-aligned offset, read back full sector, verify only that byte changed
            var sectorSize = _rawDisk.SectorSize;

            // Write known pattern to sector 5
            var sectorData = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                sectorData[i] = (byte)(i & 0xFF);

            await _rawDisk.WriteSectorsAsync(5, sectorData, CancellationToken.None);

            // Write a single byte at offset 5 * sectorSize + 100
            var byteOffset = 5 * sectorSize + 100;
            var singleByte = new byte[] { 0xAB };
            var bytesWritten = await _rawDisk.WriteBytesAsync(byteOffset, singleByte, CancellationToken.None);

            Assert.AreEqual(1, bytesWritten, "Should have written 1 byte.");

            // Read back the full sector
            using var readStream = await _rawDisk.ReadSectorsAsync(5, 1, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize, bytesRead, "Should have read one sector.");

            // Verify all bytes except position 100 are unchanged
            for (int i = 0; i < sectorSize; i++)
                if (i == 100)
                    Assert.AreEqual(0xAB, readBuffer[i], "Byte at position 100 should be the written value.");
                else
                    Assert.AreEqual((byte)(i & 0xFF), readBuffer[i], $"Byte at position {i} should be unchanged.");
        }

        [Test]
        public async Task Test_RawDisk_WriteSpanningSectorBoundary_DataMatches()
        {
            // Write data that spans a sector boundary (e.g. offset = sector_size - 4, length = 8)
            var sectorSize = _rawDisk.SectorSize;

            // Write known patterns to sectors 6 and 7
            var sector6Data = new byte[sectorSize];
            var sector7Data = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
            {
                sector6Data[i] = (byte)(i | 0xD0);
                sector7Data[i] = (byte)(i | 0xE0);
            }

            await _rawDisk.WriteSectorsAsync(6, sector6Data, CancellationToken.None);
            await _rawDisk.WriteSectorsAsync(7, sector7Data, CancellationToken.None);

            // Write spanning sector boundary: offset = sector_size - 4, length = 8
            var spanOffset = 6 * sectorSize + sectorSize - 4;
            var spanData = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
            var bytesWritten = await _rawDisk.WriteBytesAsync(spanOffset, spanData, CancellationToken.None);

            Assert.AreEqual(spanData.Length, bytesWritten, "Should have written all bytes.");

            // Read back both sectors
            using var readStream = await _rawDisk.ReadSectorsAsync(6, 2, CancellationToken.None);
            var readBuffer = new byte[sectorSize * 2];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize * 2, bytesRead, "Should have read two sectors.");

            // Verify sector 6: last 4 bytes should be the first 4 of our written data
            for (int i = 0; i < sectorSize - 4; i++)
                Assert.AreEqual((byte)(i | 0xD0), readBuffer[i], $"Byte at position {i} in sector 6 should be unchanged.");
            for (int i = 0; i < 4; i++)
                Assert.AreEqual(spanData[i], readBuffer[sectorSize - 4 + i], $"Span byte at position {i} in sector 6 should match.");

            // Verify sector 7: first 4 bytes should be the last 4 of our written data
            for (int i = 0; i < 4; i++)
                Assert.AreEqual(spanData[4 + i], readBuffer[sectorSize + i], $"Span byte at position {4 + i} in sector 7 should match.");
            for (int i = 4; i < sectorSize; i++)
                Assert.AreEqual((byte)(i | 0xE0), readBuffer[sectorSize + i], $"Byte at position {i} in sector 7 should be unchanged.");
        }

        [Test]
        public async Task Test_RawDisk_WriteAtLastSector_NoOverflow()
        {
            // Write at the last valid sector, verify no overflow
            var sectorSize = _rawDisk.SectorSize;
            var diskSize = _rawDisk.Size;

            // Calculate the last sector
            var lastSector = (diskSize / sectorSize) - 1;

            // Write a full sector at the last valid sector
            var writeData = new byte[sectorSize];
            new Random(42).NextBytes(writeData);

            var bytesWritten = await _rawDisk.WriteSectorsAsync(lastSector, writeData, CancellationToken.None);
            Assert.AreEqual(sectorSize, bytesWritten, "Should have written a full sector.");

            // Read it back and verify
            using var readStream = await _rawDisk.ReadSectorsAsync(lastSector, 1, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize, bytesRead, "Should have read one sector.");
            Assert.AreEqual(writeData, readBuffer, "Data should match.");
        }

        [Test]
        public async Task Test_RawDisk_WriteUnalignedWithMemory_DataMatches()
        {
            // Test WriteBytesAsync with Memory<byte> for unaligned writes
            var sectorSize = _rawDisk.SectorSize;

            // Write known pattern to sector 8
            var sectorData = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                sectorData[i] = (byte)(i | 0xF0);

            await _rawDisk.WriteSectorsAsync(8, sectorData, CancellationToken.None);

            // Write at unaligned offset using Memory<byte>
            var unalignedOffset = 8 * sectorSize + 10;
            var writeData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE };
            var bytesWritten = await _rawDisk.WriteBytesAsync(unalignedOffset, writeData.AsMemory(), CancellationToken.None);

            Assert.AreEqual(writeData.Length, bytesWritten, "Should have written all bytes.");

            // Read back and verify
            using var readStream = await _rawDisk.ReadSectorsAsync(8, 1, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize, bytesRead, "Should have read one sector.");

            // Verify data before written region
            for (int i = 0; i < 10; i++)
                Assert.AreEqual((byte)(i | 0xF0), readBuffer[i], $"Byte at position {i} should be unchanged.");

            // Verify written data
            for (int i = 0; i < writeData.Length; i++)
                Assert.AreEqual(writeData[i], readBuffer[10 + i], $"Written byte at position {10 + i} should match.");

            // Verify data after written region
            for (int i = 10 + writeData.Length; i < sectorSize; i++)
                Assert.AreEqual((byte)(i | 0xF0), readBuffer[i], $"Byte at position {i} should be unchanged.");
        }

        #endregion

        #region PartitionTableFactory detection tests

        [Test]
        public async Task Test_PartitionTableFactory_GPTBytes_DetectsGPT()
        {
            // Create raw bytes representing a GPT disk
            var sectorSize = 512;
            var diskBytes = new byte[sectorSize * 4]; // 4 sectors: MBR + GPT header + 2 for partition entries

            // MBR (sector 0) - Protective MBR with GPT signature
            // Boot signature at offset 510-511: 0x55, 0xAA (little-endian 0xAA55)
            diskBytes[510] = 0x55;
            diskBytes[511] = 0xAA;

            // Partition type at offset 450: 0xEE (GPT protective)
            diskBytes[450] = 0xEE;

            // GPT header (sector 1) - "EFI PART" signature
            // Signature: "EFI PART" in little-endian = 0x5452415020494645
            var gptSignature = "EFI PART"u8.ToArray(); // "EFI PART"
            Buffer.BlockCopy(gptSignature, 0, diskBytes, sectorSize, 8);

            // Create partition table from bytes
            var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

            Assert.IsNotNull(partitionTable, "Partition table should be detected.");
            Assert.AreEqual(PartitionTableType.GPT, partitionTable!.TableType, "Should detect GPT partition table.");
        }

        [Test]
        public async Task Test_PartitionTableFactory_MBRBytes_DetectsMBR()
        {
            // Create raw bytes representing an MBR disk
            var sectorSize = 512;
            var diskBytes = new byte[sectorSize];

            // MBR boot signature at offset 510-511: 0x55, 0xAA
            diskBytes[510] = 0x55;
            diskBytes[511] = 0xAA;

            // Partition type at offset 450: NOT 0xEE (use a normal type like 0x83 for Linux)
            diskBytes[450] = 0x83;

            // Create partition table from bytes
            var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

            Assert.IsNotNull(partitionTable, "Partition table should be detected.");
            Assert.AreEqual(PartitionTableType.MBR, partitionTable!.TableType, "Should detect MBR partition table.");
        }

        [Test]
        public async Task Test_PartitionTableFactory_InvalidBytes_ReturnsUnknown()
        {
            // Create raw bytes with invalid/zeroed data (no valid boot signature)
            var sectorSize = 512;
            var diskBytes = new byte[sectorSize];

            // All zeros - no boot signature at offset 510-511

            // Create partition table from bytes
            var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

            Assert.IsNotNull(partitionTable, "Partition table should be created even for unknown type.");
            Assert.AreEqual(PartitionTableType.Unknown, partitionTable!.TableType, "Should detect Unknown partition table for invalid bytes.");
        }

        [Test]
        public async Task Test_PartitionTableFactory_ProtectiveMBRType_DetectsGPT()
        {
            // Test the protective MBR path - valid MBR boot signature with type 0xEE
            var sectorSize = 512;
            var diskBytes = new byte[sectorSize * 4];

            // MBR with valid boot signature
            diskBytes[510] = 0x55;
            diskBytes[511] = 0xAA;

            // Protective MBR type at offset 450: 0xEE
            diskBytes[450] = 0xEE;

            // GPT header at sector 1 with "EFI PART" signature
            var gptSignature = "EFI PART"u8.ToArray(); // "EFI PART"
            Buffer.BlockCopy(gptSignature, 0, diskBytes, sectorSize, 8);

            // Create partition table from bytes
            var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

            Assert.IsNotNull(partitionTable, "Partition table should be detected.");
            Assert.AreEqual(PartitionTableType.GPT, partitionTable!.TableType, "Should detect GPT when protective MBR type (0xEE) is present.");
        }

        [Test]
        public async Task Test_PartitionTableFactory_ProtectiveMBRWithoutGptHeader_FallsBackToMBR()
        {
            // Test that if protective MBR type is present but no valid GPT header, it falls back to MBR
            var sectorSize = 512;
            var diskBytes = new byte[sectorSize * 2];

            // MBR with valid boot signature
            diskBytes[510] = 0x55;
            diskBytes[511] = 0xAA;

            // Protective MBR type at offset 450: 0xEE
            diskBytes[450] = 0xEE;

            // NO valid GPT header at sector 1 (leave as zeros)

            // Create partition table from bytes
            var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

            Assert.IsNotNull(partitionTable, "Partition table should be detected.");
            // When GPT header is invalid, it should fall back to parsing as MBR
            Assert.AreEqual(PartitionTableType.MBR, partitionTable!.TableType, "Should fall back to MBR when GPT header is invalid.");
        }

        [Test]
        public async Task Test_PartitionTableFactory_GPTFromRealDisk_DetectsGPT()
        {
            // Use the real disk created in SetUp (which is GPT) to test detection
            var sectorSize = _rawDisk.SectorSize;

            // GPT requires reading the protective MBR (sector 0), GPT header (sector 1),
            // and partition entries (sectors 2-33). Read 34 sectors to be safe.
            var sectorsToRead = 34;
            using var stream = await _rawDisk.ReadSectorsAsync(0, sectorsToRead, CancellationToken.None);
            var diskBytes = new byte[sectorSize * sectorsToRead];
            await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

            // Create partition table from the real disk bytes
            var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

            Assert.IsNotNull(partitionTable, "Partition table should be detected from real disk.");
            Assert.AreEqual(PartitionTableType.GPT, partitionTable!.TableType, "Should detect GPT from real disk bytes.");
        }

        #endregion

        #region GPT and MBR partition table parsing tests

        [Test]
        public async Task Test_GPT_ParseFromRealDisk_ReturnsCorrectPartitions()
        {
            // The SetUp creates a GPT disk with a single FAT32 partition
            // Read raw bytes and parse with GPT.ParseAsync()
            var sectorSize = _rawDisk.SectorSize;

            // Read enough sectors to include protective MBR, GPT header, and partition entries
            // GPT typically stores partition entries in sectors 2-33 (128 entries * 128 bytes each = 16384 bytes = 32 sectors at 512b)
            var sectorsToRead = 34;
            using var stream = await _rawDisk.ReadSectorsAsync(0, sectorsToRead, CancellationToken.None);
            var diskBytes = new byte[sectorSize * sectorsToRead];
            await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

            // Parse the GPT from bytes
            var gpt = new Duplicati.Proprietary.DiskImage.Partition.GPT(null);
            var parsed = await gpt.ParseAsync(diskBytes, sectorSize, CancellationToken.None);

            Assert.IsTrue(parsed, "GPT should parse successfully from real disk bytes.");

            // Verify partition count - we created 1 partition
            var partitions = new List<Duplicati.Proprietary.DiskImage.Partition.IPartition>();
            await foreach (var partition in gpt.EnumeratePartitions(CancellationToken.None))
                partitions.Add(partition);

            Assert.AreEqual(1, partitions.Count, "Should have 1 partition.");

            // Verify partition properties
            var firstPartition = partitions[0];
            Assert.IsNotNull(firstPartition, "First partition should exist.");
            Assert.AreEqual(1, firstPartition.PartitionNumber, "Partition number should be 1.");
            Assert.Greater(firstPartition.StartOffset, 0, "StartOffset should be greater than 0.");
            Assert.Greater(firstPartition.Size, 0, "Size should be greater than 0.");

            // Verify sector alignment
            Assert.AreEqual(0, firstPartition.StartOffset % sectorSize,
                "StartOffset should be sector-aligned.");
            Assert.AreEqual(0, firstPartition.Size % sectorSize,
                "Size should be sector-aligned.");

            gpt.Dispose();
        }

        [Test]
        public async Task Test_MBR_ParseFromRealDisk_ReturnsCorrectPartitions()
        {
            // Create a separate MBR disk for this test
            var extension = OperatingSystem.IsWindows() ? "vhdx"
                : OperatingSystem.IsLinux() ? "img"
                : "dmg";
            var mbrDiskPath = Path.Combine(DATAFOLDER, $"duplicati_mbr_test_{Guid.NewGuid()}.{extension}");
            string mbrDiskIdentifier = "";
            Duplicati.Proprietary.DiskImage.Disk.IRawDisk? mbrRawDisk = null;

            try
            {
                // Create a 50 MiB MBR disk
                mbrDiskIdentifier = _diskHelper.CreateDisk(mbrDiskPath, 50 * MiB);

                // Initialize with MBR and a single FAT32 partition
                _diskHelper.InitializeDisk(mbrDiskIdentifier, PartitionTableType.MBR, [(FileSystemType.FAT32, 0)]);
                _diskHelper.Unmount(mbrDiskIdentifier);

                // Create raw disk interface for MBR disk
                if (OperatingSystem.IsWindows())
                    mbrRawDisk = new Duplicati.Proprietary.DiskImage.Disk.Windows(mbrDiskIdentifier);
                else if (OperatingSystem.IsLinux())
                    mbrRawDisk = new Duplicati.Proprietary.DiskImage.Disk.Linux(mbrDiskIdentifier);
                else if (OperatingSystem.IsMacOS())
                    mbrRawDisk = new Duplicati.Proprietary.DiskImage.Disk.Mac(mbrDiskIdentifier);

                Assert.NotNull(mbrRawDisk, "Raw disk interface should not be null.");

                if (!await mbrRawDisk!.InitializeAsync(true, CancellationToken.None))
                    throw new InvalidOperationException($"Failed to initialize MBR raw disk: {mbrDiskIdentifier}");

                var sectorSize = mbrRawDisk.SectorSize;

                // Read the MBR sector (first 512 bytes)
                using var stream = await mbrRawDisk.ReadSectorsAsync(0, 1, CancellationToken.None);
                var mbrBytes = new byte[sectorSize];
                await stream.ReadAtLeastAsync(mbrBytes, mbrBytes.Length, cancellationToken: CancellationToken.None);

                // Parse the MBR from bytes
                var mbr = new Duplicati.Proprietary.DiskImage.Partition.MBR(null);
                var parsed = await mbr.ParseAsync(mbrBytes, sectorSize, CancellationToken.None);

                Assert.IsTrue(parsed, "MBR should parse successfully from real disk bytes.");

                // Verify partition entries - we created 1 partition
                Assert.AreEqual(1, mbr.NumPartitionEntries, "Should have 1 partition entry.");

                // Verify via EnumeratePartitions
                var partitions = new List<Duplicati.Proprietary.DiskImage.Partition.IPartition>();
                await foreach (var partition in mbr.EnumeratePartitions(CancellationToken.None))
                    partitions.Add(partition);

                Assert.AreEqual(1, partitions.Count, "Should enumerate 1 partition.");

                // Verify partition properties
                var firstPartition = partitions[0];
                Assert.IsNotNull(firstPartition, "First partition should exist.");
                Assert.AreEqual(1, firstPartition.PartitionNumber, "Partition number should be 1.");
                Assert.Greater(firstPartition.StartOffset, 0, "StartOffset should be greater than 0.");
                Assert.Greater(firstPartition.Size, 0, "Size should be greater than 0.");

                // Verify sector alignment
                Assert.AreEqual(0, firstPartition.StartOffset % sectorSize,
                    "StartOffset should be sector-aligned.");
                Assert.AreEqual(0, firstPartition.Size % sectorSize,
                    "Size should be sector-aligned.");

                mbr.Dispose();
            }
            finally
            {
                mbrRawDisk?.Dispose();
                if (!string.IsNullOrEmpty(mbrDiskIdentifier))
                    _diskHelper.Unmount(mbrDiskIdentifier);
                if (File.Exists(mbrDiskPath))
                    File.Delete(mbrDiskPath);
            }
        }

        [Test]
        public async Task Test_GPT_EnumeratePartitions_ReturnsCorrectCount()
        {
            // Use the GPT disk created in SetUp
            var sectorSize = _rawDisk.SectorSize;

            // Read disk bytes
            var sectorsToRead = 34;
            using var stream = await _rawDisk.ReadSectorsAsync(0, sectorsToRead, CancellationToken.None);
            var diskBytes = new byte[sectorSize * sectorsToRead];
            await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

            // Parse GPT
            var gpt = new Duplicati.Proprietary.DiskImage.Partition.GPT(null);
            await gpt.ParseAsync(diskBytes, sectorSize, CancellationToken.None);

            // Test EnumeratePartitions
            var count = 0;
            await foreach (var partition in gpt.EnumeratePartitions(CancellationToken.None))
            {
                count++;
                Assert.IsNotNull(partition, "Partition should not be null.");
                Assert.Greater(partition.PartitionNumber, 0, "Partition number should be positive.");
            }

            Assert.AreEqual(1, count, "Should enumerate exactly 1 partition.");

            gpt.Dispose();
        }

        [Test]
        public async Task Test_MBR_EnumeratePartitions_ReturnsCorrectCount()
        {
            // Create a separate MBR disk for this test
            var extension = OperatingSystem.IsWindows() ? "vhdx"
                : OperatingSystem.IsLinux() ? "img"
                : "dmg";
            var mbrDiskPath = Path.Combine(DATAFOLDER, $"duplicati_mbr_enum_test_{Guid.NewGuid()}.{extension}");
            string mbrDiskIdentifier = "";
            Duplicati.Proprietary.DiskImage.Disk.IRawDisk? mbrRawDisk = null;

            try
            {
                // Create a 50 MiB MBR disk
                mbrDiskIdentifier = _diskHelper.CreateDisk(mbrDiskPath, 50 * MiB);
                _diskHelper.InitializeDisk(mbrDiskIdentifier, PartitionTableType.MBR, [(FileSystemType.FAT32, 0)]);
                _diskHelper.Unmount(mbrDiskIdentifier);

                // Create raw disk interface
                if (OperatingSystem.IsWindows())
                    mbrRawDisk = new Duplicati.Proprietary.DiskImage.Disk.Windows(mbrDiskIdentifier);
                else if (OperatingSystem.IsLinux())
                    mbrRawDisk = new Duplicati.Proprietary.DiskImage.Disk.Linux(mbrDiskIdentifier);
                else if (OperatingSystem.IsMacOS())
                    mbrRawDisk = new Duplicati.Proprietary.DiskImage.Disk.Mac(mbrDiskIdentifier);

                Assert.NotNull(mbrRawDisk, "Raw disk interface should not be null.");

                if (!await mbrRawDisk!.InitializeAsync(true, CancellationToken.None))
                    throw new InvalidOperationException("Failed to initialize MBR raw disk");

                var sectorSize = mbrRawDisk.SectorSize;

                // Read and parse MBR
                using var stream = await mbrRawDisk.ReadSectorsAsync(0, 1, CancellationToken.None);
                var mbrBytes = new byte[sectorSize];
                await stream.ReadAtLeastAsync(mbrBytes, mbrBytes.Length, cancellationToken: CancellationToken.None);

                var mbr = new Duplicati.Proprietary.DiskImage.Partition.MBR(null);
                await mbr.ParseAsync(mbrBytes, sectorSize, CancellationToken.None);

                // Test EnumeratePartitions
                var count = 0;
                await foreach (var partition in mbr.EnumeratePartitions(CancellationToken.None))
                {
                    count++;
                    Assert.IsNotNull(partition, "Partition should not be null.");
                    Assert.Greater(partition.PartitionNumber, 0, "Partition number should be positive.");
                }

                Assert.AreEqual(1, count, "Should enumerate exactly 1 partition.");

                mbr.Dispose();
            }
            finally
            {
                mbrRawDisk?.Dispose();
                if (!string.IsNullOrEmpty(mbrDiskIdentifier))
                    _diskHelper.Unmount(mbrDiskIdentifier);
                if (File.Exists(mbrDiskPath))
                    File.Delete(mbrDiskPath);
            }
        }

        [Test]
        public async Task Test_GPT_GetPartitionAsync_ReturnsCorrectPartition()
        {
            // Use the GPT disk created in SetUp
            var sectorSize = _rawDisk.SectorSize;

            // Read disk bytes
            var sectorsToRead = 34;
            using var stream = await _rawDisk.ReadSectorsAsync(0, sectorsToRead, CancellationToken.None);
            var diskBytes = new byte[sectorSize * sectorsToRead];
            await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

            // Parse GPT
            var gpt = new Duplicati.Proprietary.DiskImage.Partition.GPT(null);
            await gpt.ParseAsync(diskBytes, sectorSize, CancellationToken.None);

            // Test GetPartitionAsync for partition 1 (should exist)
            var partition1 = await gpt.GetPartitionAsync(1, CancellationToken.None);
            Assert.IsNotNull(partition1, "Partition 1 should exist.");
            Assert.AreEqual(1, partition1!.PartitionNumber, "Partition number should be 1.");

            // Test GetPartitionAsync for partition 2 (should not exist - we only created 1)
            var partition2 = await gpt.GetPartitionAsync(2, CancellationToken.None);
            Assert.IsNull(partition2, "Partition 2 should not exist.");

            // Test GetPartitionAsync for invalid partition number 0
            var partition0 = await gpt.GetPartitionAsync(0, CancellationToken.None);
            Assert.IsNull(partition0, "Partition 0 should not exist (invalid number).");

            gpt.Dispose();
        }

        [Test]
        public async Task Test_MBR_GetPartitionAsync_ReturnsCorrectPartition()
        {
            // Create a separate MBR disk for this test
            var extension = OperatingSystem.IsWindows() ? "vhdx"
                : OperatingSystem.IsLinux() ? "img"
                : "dmg";
            var mbrDiskPath = Path.Combine(DATAFOLDER, $"duplicati_mbr_get_test_{Guid.NewGuid()}.{extension}");
            string mbrDiskIdentifier = "";
            Duplicati.Proprietary.DiskImage.Disk.IRawDisk? mbrRawDisk = null;

            try
            {
                // Create a 50 MiB MBR disk
                mbrDiskIdentifier = _diskHelper.CreateDisk(mbrDiskPath, 50 * MiB);
                _diskHelper.InitializeDisk(mbrDiskIdentifier, PartitionTableType.MBR, [(FileSystemType.FAT32, 0)]);
                _diskHelper.Unmount(mbrDiskIdentifier);

                // Create raw disk interface
                if (OperatingSystem.IsWindows())
                    mbrRawDisk = new Duplicati.Proprietary.DiskImage.Disk.Windows(mbrDiskIdentifier);
                else if (OperatingSystem.IsLinux())
                    mbrRawDisk = new Duplicati.Proprietary.DiskImage.Disk.Linux(mbrDiskIdentifier);
                else if (OperatingSystem.IsMacOS())
                    mbrRawDisk = new Duplicati.Proprietary.DiskImage.Disk.Mac(mbrDiskIdentifier);

                Assert.NotNull(mbrRawDisk, "Raw disk interface should not be null.");

                if (!await mbrRawDisk!.InitializeAsync(true, CancellationToken.None))
                    throw new InvalidOperationException("Failed to initialize MBR raw disk");

                var sectorSize = mbrRawDisk.SectorSize;

                // Read and parse MBR
                using var stream = await mbrRawDisk.ReadSectorsAsync(0, 1, CancellationToken.None);
                var mbrBytes = new byte[sectorSize];
                await stream.ReadAtLeastAsync(mbrBytes, mbrBytes.Length, cancellationToken: CancellationToken.None);

                var mbr = new Duplicati.Proprietary.DiskImage.Partition.MBR(null);
                await mbr.ParseAsync(mbrBytes, sectorSize, CancellationToken.None);

                // Test GetPartitionAsync for partition 1 (should exist)
                var partition1 = await mbr.GetPartitionAsync(1, CancellationToken.None);
                Assert.IsNotNull(partition1, "Partition 1 should exist.");
                Assert.AreEqual(1, partition1!.PartitionNumber, "Partition number should be 1.");

                // Test GetPartitionAsync for partition 2 (should not exist)
                var partition2 = await mbr.GetPartitionAsync(2, CancellationToken.None);
                Assert.IsNull(partition2, "Partition 2 should not exist.");

                // Test GetPartitionAsync for invalid partition number 0
                var partition0 = await mbr.GetPartitionAsync(0, CancellationToken.None);
                Assert.IsNull(partition0, "Partition 0 should not exist (invalid number).");

                mbr.Dispose();
            }
            finally
            {
                mbrRawDisk?.Dispose();
                if (!string.IsNullOrEmpty(mbrDiskIdentifier))
                    _diskHelper.Unmount(mbrDiskIdentifier);
                if (File.Exists(mbrDiskPath))
                    File.Delete(mbrDiskPath);
            }
        }

        [Test]
        public async Task Test_GPT_PartitionAlignment_IsSectorAligned()
        {
            // Use the GPT disk created in SetUp to verify partition alignment
            var sectorSize = _rawDisk.SectorSize;

            // Read disk bytes
            var sectorsToRead = 34;
            using var stream = await _rawDisk.ReadSectorsAsync(0, sectorsToRead, CancellationToken.None);
            var diskBytes = new byte[sectorSize * sectorsToRead];
            await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

            // Parse GPT
            var gpt = new Duplicati.Proprietary.DiskImage.Partition.GPT(null);
            await gpt.ParseAsync(diskBytes, sectorSize, CancellationToken.None);

            // Verify all partitions are sector-aligned
            await foreach (var partition in gpt.EnumeratePartitions(CancellationToken.None))
            {
                Assert.AreEqual(0, partition.StartOffset % sectorSize,
                    $"Partition {partition.PartitionNumber} StartOffset ({partition.StartOffset}) should be sector-aligned (sector size: {sectorSize}).");
                Assert.AreEqual(0, partition.Size % sectorSize,
                    $"Partition {partition.PartitionNumber} Size ({partition.Size}) should be sector-aligned (sector size: {sectorSize}).");
            }

            gpt.Dispose();
        }

        [Test]
        public async Task Test_MBR_PartitionAlignment_IsSectorAligned()
        {
            // Create a separate MBR disk for this test
            var extension = OperatingSystem.IsWindows() ? "vhdx"
                : OperatingSystem.IsLinux() ? "img"
                : "dmg";
            var mbrDiskPath = Path.Combine(DATAFOLDER, $"duplicati_mbr_align_test_{Guid.NewGuid()}.{extension}");
            string mbrDiskIdentifier = "";
            Duplicati.Proprietary.DiskImage.Disk.IRawDisk? mbrRawDisk = null;

            try
            {
                // Create a 50 MiB MBR disk
                mbrDiskIdentifier = _diskHelper.CreateDisk(mbrDiskPath, 50 * MiB);
                _diskHelper.InitializeDisk(mbrDiskIdentifier, PartitionTableType.MBR, [(FileSystemType.FAT32, 0)]);
                _diskHelper.Unmount(mbrDiskIdentifier);

                // Create raw disk interface
                if (OperatingSystem.IsWindows())
                    mbrRawDisk = new Duplicati.Proprietary.DiskImage.Disk.Windows(mbrDiskIdentifier);
                else if (OperatingSystem.IsLinux())
                    mbrRawDisk = new Duplicati.Proprietary.DiskImage.Disk.Linux(mbrDiskIdentifier);
                else if (OperatingSystem.IsMacOS())
                    mbrRawDisk = new Duplicati.Proprietary.DiskImage.Disk.Mac(mbrDiskIdentifier);

                Assert.NotNull(mbrRawDisk, "Raw disk interface should not be null.");

                if (!await mbrRawDisk!.InitializeAsync(true, CancellationToken.None))
                    throw new InvalidOperationException("Failed to initialize MBR raw disk");

                var sectorSize = mbrRawDisk.SectorSize;

                // Read and parse MBR
                using var stream = await mbrRawDisk.ReadSectorsAsync(0, 1, CancellationToken.None);
                var mbrBytes = new byte[sectorSize];
                await stream.ReadAtLeastAsync(mbrBytes, mbrBytes.Length, cancellationToken: CancellationToken.None);

                var mbr = new Duplicati.Proprietary.DiskImage.Partition.MBR(null);
                await mbr.ParseAsync(mbrBytes, sectorSize, CancellationToken.None);

                // Verify all partitions are sector-aligned
                await foreach (var partition in mbr.EnumeratePartitions(CancellationToken.None))
                {
                    Assert.AreEqual(0, partition.StartOffset % sectorSize,
                        $"Partition {partition.PartitionNumber} StartOffset ({partition.StartOffset}) should be sector-aligned (sector size: {sectorSize}).");
                    Assert.AreEqual(0, partition.Size % sectorSize,
                        $"Partition {partition.PartitionNumber} Size ({partition.Size}) should be sector-aligned (sector size: {sectorSize}).");
                }

                mbr.Dispose();
            }
            finally
            {
                mbrRawDisk?.Dispose();
                if (!string.IsNullOrEmpty(mbrDiskIdentifier))
                    _diskHelper.Unmount(mbrDiskIdentifier);
                if (File.Exists(mbrDiskPath))
                    File.Delete(mbrDiskPath);
            }
        }

        #endregion

    }
}
