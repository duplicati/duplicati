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
        // Class-level read-only disks (set up once for entire test class)
        private static IDiskImageHelper? s_diskHelper;

        // GPT disk with 2 FAT32 partitions
        private static string s_gptDiskPath = "";
        private static string s_gptDiskIdentifier = "";
        private static IRawDisk? s_gptRawDisk;
        private static long s_gptPartition1Offset;
        private static long s_gptPartition1Size;
        private static long s_gptPartition2Offset;
        private static long s_gptPartition2Size;

        // MBR disk with 2 FAT32 partitions
        private static string s_mbrDiskPath = "";
        private static string s_mbrDiskIdentifier = "";
        private static IRawDisk? s_mbrRawDisk;
        private static long s_mbrPartition1Offset;
        private static long s_mbrPartition1Size;
        private static long s_mbrPartition2Offset;
        private static long s_mbrPartition2Size;

        // Writable disk for tests that need to write (re-initialized before each test)
        private static string s_writableDiskPath = "";
        private static string s_writableDiskIdentifier = "";

        // Per-test instance members
        private IRawDisk _writableRawDisk = null!;

        private const long MiB = 1024 * 1024;

        #region Class-level Setup and Teardown

        /// <summary>
        /// One-time setup for the entire test class.
        /// Creates read-only GPT and MBR disks with 2 FAT32 partitions each.
        /// </summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            base.BasicHelperSetUp();

            // Create the appropriate disk image helper for the current platform
            s_diskHelper = DiskImageHelperFactory.Create();

            // Check for admin privileges
            if (!s_diskHelper.HasRequiredPrivileges())
            {
                Assert.Ignore("DiskImage tests require administrator privileges");
            }

            var extension = DiskImageTestHelpers.GetPlatformDiskImageExtension();

            // Create GPT disk with 2 FAT32 partitions
            s_gptDiskPath = Path.Combine(DATAFOLDER, $"duplicati_gpt_class_test.{extension}");
            s_gptDiskIdentifier = DiskImageTestHelpers.CreateDiskWithTwoFat32Partitions(
                s_diskHelper, s_gptDiskPath, PartitionTableType.GPT, 50 * MiB);

            // Get partition info from GPT disk
            var gptPartitions = s_diskHelper.GetPartitions(s_gptDiskIdentifier);
            if (gptPartitions.Length >= 2)
            {
                s_gptPartition1Offset = gptPartitions[0].StartOffset;
                s_gptPartition1Size = gptPartitions[0].Size;
                s_gptPartition2Offset = gptPartitions[1].StartOffset;
                s_gptPartition2Size = gptPartitions[1].Size;
            }

            // Create and initialize raw disk for GPT (read-only)
            s_gptRawDisk = DiskImageTestHelpers.CreateRawDiskForIdentifier(s_gptDiskIdentifier);
            if (!s_gptRawDisk.InitializeAsync(true, CancellationToken.None).Result)
                throw new InvalidOperationException("Failed to initialize GPT raw disk");

            // Fill GPT partitions with well-known test data
            DiskImageTestHelpers.FillPartitionWithTestDataAsync(s_gptRawDisk, s_gptPartition1Offset, s_gptPartition1Size, CancellationToken.None).Wait();
            DiskImageTestHelpers.FillPartitionWithTestDataAsync(s_gptRawDisk, s_gptPartition2Offset, s_gptPartition2Size, CancellationToken.None).Wait();
            s_diskHelper.FlushDisk(s_gptDiskIdentifier);

            // Create MBR disk with 2 FAT32 partitions
            s_mbrDiskPath = Path.Combine(DATAFOLDER, $"duplicati_mbr_class_test.{extension}");
            s_mbrDiskIdentifier = DiskImageTestHelpers.CreateDiskWithTwoFat32Partitions(
                s_diskHelper, s_mbrDiskPath, PartitionTableType.MBR, 50 * MiB);

            // Get partition info from MBR disk
            var mbrPartitions = s_diskHelper.GetPartitions(s_mbrDiskIdentifier);
            if (mbrPartitions.Length >= 2)
            {
                s_mbrPartition1Offset = mbrPartitions[0].StartOffset;
                s_mbrPartition1Size = mbrPartitions[0].Size;
                s_mbrPartition2Offset = mbrPartitions[1].StartOffset;
                s_mbrPartition2Size = mbrPartitions[1].Size;
            }

            // Create and initialize raw disk for MBR (read-only)
            s_mbrRawDisk = DiskImageTestHelpers.CreateRawDiskForIdentifier(s_mbrDiskIdentifier);
            if (!s_mbrRawDisk.InitializeAsync(true, CancellationToken.None).Result)
                throw new InvalidOperationException("Failed to initialize MBR raw disk");

            // Fill MBR partitions with well-known test data
            DiskImageTestHelpers.FillPartitionWithTestDataAsync(s_mbrRawDisk, s_mbrPartition1Offset, s_mbrPartition1Size, CancellationToken.None).Wait();
            DiskImageTestHelpers.FillPartitionWithTestDataAsync(s_mbrRawDisk, s_mbrPartition2Offset, s_mbrPartition2Size, CancellationToken.None).Wait();
            s_diskHelper.FlushDisk(s_mbrDiskIdentifier);

            // Create writable disk path (will be created per-test)
            s_writableDiskPath = Path.Combine(DATAFOLDER, $"duplicati_writable_class_test.{extension}");
        }

        /// <summary>
        /// One-time teardown for the entire test class.
        /// Cleans up all class-level disk resources.
        /// </summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            // Dispose read-only raw disks
            s_gptRawDisk?.Dispose();
            s_mbrRawDisk?.Dispose();

            // Unmount and cleanup class-level disks
            if (s_diskHelper != null)
            {
                DiskImageTestHelpers.SafeUnmount(s_diskHelper, s_gptDiskIdentifier);
                DiskImageTestHelpers.SafeUnmount(s_diskHelper, s_mbrDiskIdentifier);
                DiskImageTestHelpers.SafeUnmount(s_diskHelper, s_writableDiskIdentifier);
            }

            // Delete disk image files
            DiskImageTestHelpers.SafeDeleteFile(s_gptDiskPath);
            DiskImageTestHelpers.SafeDeleteFile(s_mbrDiskPath);
            DiskImageTestHelpers.SafeDeleteFile(s_writableDiskPath);

            s_diskHelper?.Dispose();
        }

        #endregion

        #region Per-test Setup and Teardown

        /// <summary>
        /// Sets up the test environment before each test that needs a writable disk.
        /// Creates a fresh writable disk for tests that write data.
        /// </summary>
        [SetUp]
        public async Task SetUp()
        {
            // Create a fresh writable disk for this test
            if (s_diskHelper != null && !string.IsNullOrEmpty(s_writableDiskPath))
            {
                // Clean up any previous writable disk
                if (!string.IsNullOrEmpty(s_writableDiskIdentifier))
                {
                    DiskImageTestHelpers.SafeUnmount(s_diskHelper, s_writableDiskIdentifier);
                    s_writableDiskIdentifier = "";
                }
                DiskImageTestHelpers.SafeDeleteFile(s_writableDiskPath);

                // Create new writable disk (100 MiB, uninitialized)
                s_writableDiskIdentifier = s_diskHelper.CreateDisk(s_writableDiskPath, 100 * MiB);
                s_diskHelper.Unmount(s_writableDiskIdentifier);

                // Create raw disk interface (with write access)
                _writableRawDisk = DiskImageTestHelpers.CreateRawDiskForIdentifier(s_writableDiskIdentifier);
                if (!await _writableRawDisk.InitializeAsync(true, CancellationToken.None))
                    throw new InvalidOperationException("Failed to initialize writable raw disk");
            }
        }

        /// <summary>
        /// Cleans up after each test.
        /// Unmounts the writable disk so subsequent tests have a clean state.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            // Dispose the writable raw disk
            _writableRawDisk?.Dispose();

            // Unmount writable disk to ensure clean state for next test
            if (s_diskHelper != null && !string.IsNullOrEmpty(s_writableDiskIdentifier))
            {
                DiskImageTestHelpers.SafeUnmount(s_diskHelper, s_writableDiskIdentifier);
            }
        }

        #endregion

        #region IRawDisk Sector-Aligned Tests

        [Test]
        public async Task Test_RawDisk_ReadSector_ReturnsNonEmptyData()
        {
            var sectorSize = s_gptRawDisk!.SectorSize;
            using var stream = await s_gptRawDisk.ReadSectorsAsync(0, 1, CancellationToken.None);
            var buffer = new byte[sectorSize];
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(sectorSize, bytesRead, "Should have read 1 sector.");
            bool hasData = buffer.Any(x => x != 0);
            Assert.IsTrue(hasData, "Sector 0 should contain data.");
        }

        [Test]
        public async Task Test_RawDisk_ReadBytes_ReturnsData()
        {
            var sectorSize = s_gptRawDisk!.SectorSize;
            using var stream = await s_gptRawDisk.ReadBytesAsync(sectorSize, sectorSize, CancellationToken.None);
            Assert.AreEqual(sectorSize, stream.Length, "Should have read the correct amount of bytes.");
        }

        [Test]
        public async Task Test_RawDisk_ReadBytesAsync_CallerProvidedBuffer()
        {
            var sectorSize = s_gptRawDisk!.SectorSize;
            var buffer = new byte[sectorSize];
            var bytesRead = await s_gptRawDisk.ReadBytesAsync(0, buffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(sectorSize, bytesRead, "Should have read the correct amount of bytes.");
        }

        [Test]
        public async Task Test_RawDisk_WriteSectors_DataMatches()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var writeBuffer = new byte[sectorSize];
            new Random().NextBytes(writeBuffer);
            await _writableRawDisk.WriteSectorsAsync(1, writeBuffer, CancellationToken.None);
            using var readStream = await _writableRawDisk.ReadSectorsAsync(1, 1, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(sectorSize, bytesRead, "Should have read the correct amount of bytes.");
            Assert.AreEqual(writeBuffer, readBuffer, "Data should match.");
        }

        [Test]
        public async Task Test_RawDisk_WriteBytes_DataMatches()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var writeBuffer = new byte[sectorSize];
            new Random().NextBytes(writeBuffer);
            await _writableRawDisk.WriteBytesAsync(sectorSize, writeBuffer, CancellationToken.None);
            using var readStream = await _writableRawDisk.ReadBytesAsync(sectorSize, sectorSize, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(sectorSize, bytesRead, "Should have read the correct amount of bytes.");
            Assert.AreEqual(writeBuffer, readBuffer, "Data should match.");
        }

        [Test]
        public async Task Test_RawDisk_WriteBytes_Memory()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var writeBuffer = new byte[sectorSize];
            new Random().NextBytes(writeBuffer);
            await _writableRawDisk.WriteBytesAsync(sectorSize, writeBuffer.AsMemory(), CancellationToken.None);
            using var readStream = await _writableRawDisk.ReadBytesAsync(sectorSize, sectorSize, CancellationToken.None);
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
            // Use writable disk for this test
            var sectorSize = _writableRawDisk.SectorSize;

            // Write known pattern at sector 1
            var writeBuffer = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                writeBuffer[i] = (byte)(i & 0xFF);

            await _writableRawDisk.WriteSectorsAsync(1, writeBuffer, CancellationToken.None);

            // Read at offset = sector_size + 1 (unaligned)
            var offset = sectorSize + 1;
            var length = sectorSize - 2;

            using var stream = await _writableRawDisk.ReadBytesAsync(offset, length, CancellationToken.None);
            var readBuffer = new byte[length];
            var bytesRead = await stream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(length, bytesRead, "Should have read the requested length.");
            for (int i = 0; i < length; i++)
                Assert.AreEqual((byte)((i + 1) & 0xFF), readBuffer[i], $"Byte at position {i} should match.");
        }

        [Test]
        public async Task Test_RawDisk_ReadUnalignedLength_ReturnsCorrectData()
        {
            var sectorSize = _writableRawDisk.SectorSize;

            // Write known pattern at sector 0
            var writeBuffer = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                writeBuffer[i] = (byte)(i & 0xFF);

            await _writableRawDisk.WriteSectorsAsync(0, writeBuffer, CancellationToken.None);

            // Read with unaligned length (sectorSize - 1)
            var length = sectorSize - 1;
            using var stream = await _writableRawDisk.ReadBytesAsync(0, length, CancellationToken.None);
            var readBuffer = new byte[length];
            var bytesRead = await stream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(length, bytesRead, "Should have read the requested length.");
            for (int i = 0; i < length; i++)
                Assert.AreEqual((byte)(i & 0xFF), readBuffer[i], $"Byte at position {i} should match.");
        }

        [Test]
        public async Task Test_RawDisk_ReadShortLength_ReturnsCorrectData()
        {
            var sectorSize = _writableRawDisk.SectorSize;

            // Write known pattern at sector 0
            var writeBuffer = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                writeBuffer[i] = (byte)(i & 0xFF);

            await _writableRawDisk.WriteSectorsAsync(0, writeBuffer, CancellationToken.None);

            // Read short length
            var length = sectorSize - 1;
            using var stream = await _writableRawDisk.ReadBytesAsync(0, length, CancellationToken.None);
            var readBuffer = new byte[length];
            var bytesRead = await stream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(length, bytesRead, "Should have read the requested short length.");
            for (int i = 0; i < length; i++)
                Assert.AreEqual((byte)(i & 0xFF), readBuffer[i], $"Byte at position {i} should match.");
        }

        [Test]
        public async Task Test_RawDisk_ReadStraddlingSectors_ReturnsCorrectData()
        {
            var sectorSize = _writableRawDisk.SectorSize;

            // Write different patterns at sectors 0 and 1
            var sector0Data = new byte[sectorSize];
            var sector1Data = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
            {
                sector0Data[i] = (byte)(i | 0xF0);
                sector1Data[i] = (byte)(i | 0x0F);
            }

            await _writableRawDisk.WriteSectorsAsync(0, sector0Data, CancellationToken.None);
            await _writableRawDisk.WriteSectorsAsync(1, sector1Data, CancellationToken.None);

            // Read at half-sector offset with full sector length
            var offset = sectorSize / 2;
            using var stream = await _writableRawDisk.ReadBytesAsync(offset, sectorSize, CancellationToken.None);
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
            var sectorSize = _writableRawDisk.SectorSize;
            var diskSize = _writableRawDisk.Size;

            // Calculate the last sector and write data there
            var lastSector = (diskSize / sectorSize) - 1;
            var writeBuffer = new byte[sectorSize];
            new Random().NextBytes(writeBuffer);

            await _writableRawDisk.WriteSectorsAsync(lastSector, writeBuffer, CancellationToken.None);

            // Read from near the end
            var offset = diskSize - sectorSize;
            using var stream = await _writableRawDisk.ReadBytesAsync(offset, sectorSize, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await stream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize, bytesRead, "Should have read a full sector.");
            Assert.AreEqual(writeBuffer, readBuffer, "Data should match what was written.");
        }

        [Test]
        public async Task Test_RawDisk_ReadUnalignedOffsetWithMemory_ReturnsCorrectData()
        {
            var sectorSize = _writableRawDisk.SectorSize;

            // Write known pattern at sector 1
            var writeBuffer = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                writeBuffer[i] = (byte)((i * 2) & 0xFF);

            await _writableRawDisk.WriteSectorsAsync(1, writeBuffer, CancellationToken.None);

            // Read at unaligned offset
            var offset = sectorSize + 4;
            var length = sectorSize - 8;
            var readBuffer = new byte[length];
            var bytesRead = await _writableRawDisk.ReadBytesAsync(offset, readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(length, bytesRead, "Should have read the requested length.");
            for (int i = 0; i < length; i++)
                Assert.AreEqual((byte)(((i + 4) * 2) & 0xFF), readBuffer[i], $"Byte at position {i} should match.");
        }

        [Test]
        public async Task Test_RawDisk_ReadUnalignedLengthWithMemory_ReturnsCorrectData()
        {
            var sectorSize = _writableRawDisk.SectorSize;

            // Write known pattern at sector 0
            var writeBuffer = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                writeBuffer[i] = (byte)((i + 100) & 0xFF);

            await _writableRawDisk.WriteSectorsAsync(0, writeBuffer, CancellationToken.None);

            // Read with unaligned length
            var length = sectorSize - 5;
            var readBuffer = new byte[length];
            var bytesRead = await _writableRawDisk.ReadBytesAsync(0, readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(length, bytesRead, "Should have read the requested length.");
            for (int i = 0; i < length; i++)
                Assert.AreEqual((byte)((i + 100) & 0xFF), readBuffer[i], $"Byte at position {i} should match.");
        }

        #endregion

        #region IRawDisk Unaligned Write Tests

        [Test]
        public async Task Test_RawDisk_WriteUnalignedOffset_DataMatches()
        {
            var sectorSize = _writableRawDisk.SectorSize;

            // First, write known patterns to two consecutive sectors
            var sector1Data = new byte[sectorSize];
            var sector2Data = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
            {
                sector1Data[i] = (byte)(i | 0xA0);
                sector2Data[i] = (byte)(i | 0xB0);
            }

            await _writableRawDisk.WriteSectorsAsync(1, sector1Data, CancellationToken.None);
            await _writableRawDisk.WriteSectorsAsync(2, sector2Data, CancellationToken.None);

            // Now write at unaligned offset (sector_size + 5)
            var unalignedOffset = sectorSize + 5;
            var writeData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            var bytesWritten = await _writableRawDisk.WriteBytesAsync(unalignedOffset, writeData, CancellationToken.None);

            Assert.AreEqual(writeData.Length, bytesWritten, "Should have written all bytes.");

            // Read back both sectors and verify only the intended bytes were changed
            using var readStream = await _writableRawDisk.ReadSectorsAsync(1, 2, CancellationToken.None);
            var readBuffer = new byte[sectorSize * 2];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize * 2, bytesRead, "Should have read two sectors.");

            // Verify sector 1 data
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
            var sectorSize = _writableRawDisk.SectorSize;

            // Write known pattern to sector 3
            var sectorData = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                sectorData[i] = (byte)(i | 0xC0);

            await _writableRawDisk.WriteSectorsAsync(3, sectorData, CancellationToken.None);

            // Write unaligned length (sectorSize - 10)
            var writeLength = sectorSize - 10;
            var writeData = new byte[writeLength];
            for (int i = 0; i < writeLength; i++)
                writeData[i] = (byte)((i + 50) & 0xFF);

            var bytesWritten = await _writableRawDisk.WriteBytesAsync(3 * sectorSize, writeData, CancellationToken.None);

            Assert.AreEqual(writeData.Length, bytesWritten, "Should have written all bytes.");

            // Read back and verify
            using var readStream = await _writableRawDisk.ReadSectorsAsync(3, 1, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize, bytesRead, "Should have read one sector.");

            // First part should be our written data
            for (int i = 0; i < writeLength; i++)
                Assert.AreEqual((byte)((i + 50) & 0xFF), readBuffer[i], $"Byte at position {i} should match written data.");

            // Remaining bytes should be unchanged
            for (int i = writeLength; i < sectorSize; i++)
                Assert.AreEqual((byte)(i | 0xC0), readBuffer[i], $"Byte at position {i} should be unchanged.");
        }

        [Test]
        public async Task Test_RawDisk_WriteSingleByte_VerifySingleByteChanged()
        {
            var sectorSize = _writableRawDisk.SectorSize;

            // Write known pattern to sector 5
            var sectorData = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                sectorData[i] = (byte)(i & 0xFF);

            await _writableRawDisk.WriteSectorsAsync(5, sectorData, CancellationToken.None);

            // Write a single byte at offset 5 * sectorSize + 100
            var byteOffset = 5 * sectorSize + 100;
            var singleByte = new byte[] { 0xAB };
            var bytesWritten = await _writableRawDisk.WriteBytesAsync(byteOffset, singleByte, CancellationToken.None);

            Assert.AreEqual(1, bytesWritten, "Should have written 1 byte.");

            // Read back the full sector
            using var readStream = await _writableRawDisk.ReadSectorsAsync(5, 1, CancellationToken.None);
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
            var sectorSize = _writableRawDisk.SectorSize;

            // Write known patterns to sectors 6 and 7
            var sector6Data = new byte[sectorSize];
            var sector7Data = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
            {
                sector6Data[i] = (byte)(i | 0xD0);
                sector7Data[i] = (byte)(i | 0xE0);
            }

            await _writableRawDisk.WriteSectorsAsync(6, sector6Data, CancellationToken.None);
            await _writableRawDisk.WriteSectorsAsync(7, sector7Data, CancellationToken.None);

            // Write spanning sector boundary: offset = sector_size - 4, length = 8
            var spanOffset = 6 * sectorSize + sectorSize - 4;
            var spanData = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
            var bytesWritten = await _writableRawDisk.WriteBytesAsync(spanOffset, spanData, CancellationToken.None);

            Assert.AreEqual(spanData.Length, bytesWritten, "Should have written all bytes.");

            // Read back both sectors
            using var readStream = await _writableRawDisk.ReadSectorsAsync(6, 2, CancellationToken.None);
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
            var sectorSize = _writableRawDisk.SectorSize;
            var diskSize = _writableRawDisk.Size;

            // Calculate the last sector
            var lastSector = (diskSize / sectorSize) - 1;

            // Write a full sector at the last valid sector
            var writeData = new byte[sectorSize];
            new Random(42).NextBytes(writeData);

            var bytesWritten = await _writableRawDisk.WriteSectorsAsync(lastSector, writeData, CancellationToken.None);
            Assert.AreEqual(sectorSize, bytesWritten, "Should have written a full sector.");

            // Read it back and verify
            using var readStream = await _writableRawDisk.ReadSectorsAsync(lastSector, 1, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize, bytesRead, "Should have read one sector.");
            Assert.AreEqual(writeData, readBuffer, "Data should match.");
        }

        [Test]
        public async Task Test_RawDisk_WriteUnalignedWithMemory_DataMatches()
        {
            var sectorSize = _writableRawDisk.SectorSize;

            // Write known pattern to sector 8
            var sectorData = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                sectorData[i] = (byte)(i | 0xF0);

            await _writableRawDisk.WriteSectorsAsync(8, sectorData, CancellationToken.None);

            // Write at unaligned offset using Memory<byte>
            var unalignedOffset = 8 * sectorSize + 10;
            var writeData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE };
            var bytesWritten = await _writableRawDisk.WriteBytesAsync(unalignedOffset, writeData.AsMemory(), CancellationToken.None);

            Assert.AreEqual(writeData.Length, bytesWritten, "Should have written all bytes.");

            // Read back and verify
            using var readStream = await _writableRawDisk.ReadSectorsAsync(8, 1, CancellationToken.None);
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
            var sectorSize = 512;
            var diskBytes = new byte[sectorSize * 4];

            // MBR (sector 0) - Protective MBR with GPT signature
            diskBytes[510] = 0x55;
            diskBytes[511] = 0xAA;
            diskBytes[450] = 0xEE;

            // GPT header (sector 1) - "EFI PART" signature
            var gptSignature = "EFI PART"u8.ToArray();
            Buffer.BlockCopy(gptSignature, 0, diskBytes, sectorSize, 8);

            var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

            Assert.IsNotNull(partitionTable, "Partition table should be detected.");
            Assert.AreEqual(PartitionTableType.GPT, partitionTable!.TableType, "Should detect GPT partition table.");
        }

        [Test]
        public async Task Test_PartitionTableFactory_MBRBytes_DetectsMBR()
        {
            var sectorSize = 512;
            var diskBytes = new byte[sectorSize];

            diskBytes[510] = 0x55;
            diskBytes[511] = 0xAA;
            diskBytes[450] = 0x83;

            var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

            Assert.IsNotNull(partitionTable, "Partition table should be detected.");
            Assert.AreEqual(PartitionTableType.MBR, partitionTable!.TableType, "Should detect MBR partition table.");
        }

        [Test]
        public async Task Test_PartitionTableFactory_InvalidBytes_ReturnsUnknown()
        {
            var sectorSize = 512;
            var diskBytes = new byte[sectorSize];

            var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

            Assert.IsNotNull(partitionTable, "Partition table should be created even for unknown type.");
            Assert.AreEqual(PartitionTableType.Unknown, partitionTable!.TableType, "Should detect Unknown partition table for invalid bytes.");
        }

        [Test]
        public async Task Test_PartitionTableFactory_ProtectiveMBRType_DetectsGPT()
        {
            var sectorSize = 512;
            var diskBytes = new byte[sectorSize * 4];

            diskBytes[510] = 0x55;
            diskBytes[511] = 0xAA;
            diskBytes[450] = 0xEE;

            var gptSignature = "EFI PART"u8.ToArray();
            Buffer.BlockCopy(gptSignature, 0, diskBytes, sectorSize, 8);

            var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

            Assert.IsNotNull(partitionTable, "Partition table should be detected.");
            Assert.AreEqual(PartitionTableType.GPT, partitionTable!.TableType, "Should detect GPT when protective MBR type (0xEE) is present.");
        }

        [Test]
        public async Task Test_PartitionTableFactory_ProtectiveMBRWithoutGptHeader_FallsBackToMBR()
        {
            var sectorSize = 512;
            var diskBytes = new byte[sectorSize * 2];

            diskBytes[510] = 0x55;
            diskBytes[511] = 0xAA;
            diskBytes[450] = 0xEE;

            var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

            Assert.IsNotNull(partitionTable, "Partition table should be detected.");
            Assert.AreEqual(PartitionTableType.MBR, partitionTable!.TableType, "Should fall back to MBR when GPT header is invalid.");
        }

        [Test]
        public async Task Test_PartitionTableFactory_GPTFromRealDisk_DetectsGPT()
        {
            var sectorSize = s_gptRawDisk!.SectorSize;
            var sectorsToRead = 34;
            using var stream = await s_gptRawDisk.ReadSectorsAsync(0, sectorsToRead, CancellationToken.None);
            var diskBytes = new byte[sectorSize * sectorsToRead];
            await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

            var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

            Assert.IsNotNull(partitionTable, "Partition table should be detected from real disk.");
            Assert.AreEqual(PartitionTableType.GPT, partitionTable!.TableType, "Should detect GPT from real disk bytes.");
        }

        #endregion

        #region GPT and MBR partition table parsing tests

        [Test]
        public async Task Test_GPT_ParseFromRealDisk_ReturnsCorrectPartitions()
        {
            var sectorSize = s_gptRawDisk!.SectorSize;

            // Read enough sectors to include protective MBR, GPT header, and partition entries
            var sectorsToRead = 34;
            using var stream = await s_gptRawDisk.ReadSectorsAsync(0, sectorsToRead, CancellationToken.None);
            var diskBytes = new byte[sectorSize * sectorsToRead];
            await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

            // Parse the GPT from bytes
            var gpt = new GPT(null);
            var parsed = await gpt.ParseAsync(diskBytes, sectorSize, CancellationToken.None);

            Assert.IsTrue(parsed, "GPT should parse successfully from real disk bytes.");

            // Verify partition count - we created 2 partitions
            var partitions = new List<IPartition>();
            await foreach (var partition in gpt.EnumeratePartitions(CancellationToken.None))
            {
                partitions.Add(partition);
            }

            Assert.AreEqual(2, partitions.Count, "Should have 2 partitions.");

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
            var sectorSize = s_mbrRawDisk!.SectorSize;

            // Read the MBR sector (first 512 bytes)
            using var stream = await s_mbrRawDisk.ReadSectorsAsync(0, 1, CancellationToken.None);
            var mbrBytes = new byte[sectorSize];
            await stream.ReadAtLeastAsync(mbrBytes, mbrBytes.Length, cancellationToken: CancellationToken.None);

            // Parse the MBR from bytes
            var mbr = new MBR(null);
            var parsed = await mbr.ParseAsync(mbrBytes, sectorSize, CancellationToken.None);

            Assert.IsTrue(parsed, "MBR should parse successfully from real disk bytes.");

            // Verify partition entries - we created 2 partitions
            Assert.AreEqual(2, mbr.NumPartitionEntries, "Should have 2 partition entries.");

            // Verify via EnumeratePartitions
            var partitions = new List<IPartition>();
            await foreach (var partition in mbr.EnumeratePartitions(CancellationToken.None))
            {
                partitions.Add(partition);
            }

            Assert.AreEqual(2, partitions.Count, "Should enumerate 2 partitions.");

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

        [Test]
        public async Task Test_GPT_EnumeratePartitions_ReturnsCorrectCount()
        {
            var sectorSize = s_gptRawDisk!.SectorSize;

            using var stream = await s_gptRawDisk.ReadSectorsAsync(0, 34, CancellationToken.None);
            var diskBytes = new byte[sectorSize * 34];
            await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

            var gpt = new GPT(null);
            await gpt.ParseAsync(diskBytes, sectorSize, CancellationToken.None);

            var count = 0;
            await foreach (var partition in gpt.EnumeratePartitions(CancellationToken.None))
            {
                count++;
                Assert.IsNotNull(partition, "Partition should not be null.");
                Assert.Greater(partition.PartitionNumber, 0, "Partition number should be positive.");
            }

            Assert.AreEqual(2, count, "Should enumerate exactly 2 partitions.");

            gpt.Dispose();
        }

        [Test]
        public async Task Test_MBR_EnumeratePartitions_ReturnsCorrectCount()
        {
            var sectorSize = s_mbrRawDisk!.SectorSize;

            using var stream = await s_mbrRawDisk.ReadSectorsAsync(0, 1, CancellationToken.None);
            var mbrBytes = new byte[sectorSize];
            await stream.ReadAtLeastAsync(mbrBytes, mbrBytes.Length, cancellationToken: CancellationToken.None);

            var mbr = new MBR(null);
            await mbr.ParseAsync(mbrBytes, sectorSize, CancellationToken.None);

            var count = 0;
            await foreach (var partition in mbr.EnumeratePartitions(CancellationToken.None))
            {
                count++;
                Assert.IsNotNull(partition, "Partition should not be null.");
                Assert.Greater(partition.PartitionNumber, 0, "Partition number should be positive.");
            }

            Assert.AreEqual(2, count, "Should enumerate exactly 2 partitions.");

            mbr.Dispose();
        }

        [Test]
        public async Task Test_GPT_GetPartitionAsync_ReturnsCorrectPartition()
        {
            var sectorSize = s_gptRawDisk!.SectorSize;

            using var stream = await s_gptRawDisk.ReadSectorsAsync(0, 34, CancellationToken.None);
            var diskBytes = new byte[sectorSize * 34];
            await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

            var gpt = new GPT(null);
            await gpt.ParseAsync(diskBytes, sectorSize, CancellationToken.None);

            // Test GetPartitionAsync for partition 1 (should exist)
            var partition1 = await gpt.GetPartitionAsync(1, CancellationToken.None);
            Assert.IsNotNull(partition1, "Partition 1 should exist.");
            Assert.AreEqual(1, partition1!.PartitionNumber, "Partition number should be 1.");

            // Test GetPartitionAsync for partition 2 (should exist)
            var partition2 = await gpt.GetPartitionAsync(2, CancellationToken.None);
            Assert.IsNotNull(partition2, "Partition 2 should exist.");
            Assert.AreEqual(2, partition2!.PartitionNumber, "Partition number should be 2.");

            // Test GetPartitionAsync for partition 3 (should not exist)
            var partition3 = await gpt.GetPartitionAsync(3, CancellationToken.None);
            Assert.IsNull(partition3, "Partition 3 should not exist.");

            // Test GetPartitionAsync for invalid partition number 0
            var partition0 = await gpt.GetPartitionAsync(0, CancellationToken.None);
            Assert.IsNull(partition0, "Partition 0 should not exist (invalid number).");

            gpt.Dispose();
        }

        [Test]
        public async Task Test_MBR_GetPartitionAsync_ReturnsCorrectPartition()
        {
            var sectorSize = s_mbrRawDisk!.SectorSize;

            using var stream = await s_mbrRawDisk.ReadSectorsAsync(0, 1, CancellationToken.None);
            var mbrBytes = new byte[sectorSize];
            await stream.ReadAtLeastAsync(mbrBytes, mbrBytes.Length, cancellationToken: CancellationToken.None);

            var mbr = new MBR(null);
            await mbr.ParseAsync(mbrBytes, sectorSize, CancellationToken.None);

            // Test GetPartitionAsync for partition 1 (should exist)
            var partition1 = await mbr.GetPartitionAsync(1, CancellationToken.None);
            Assert.IsNotNull(partition1, "Partition 1 should exist.");
            Assert.AreEqual(1, partition1!.PartitionNumber, "Partition number should be 1.");

            // Test GetPartitionAsync for partition 2 (should exist)
            var partition2 = await mbr.GetPartitionAsync(2, CancellationToken.None);
            Assert.IsNotNull(partition2, "Partition 2 should exist.");
            Assert.AreEqual(2, partition2!.PartitionNumber, "Partition number should be 2.");

            // Test GetPartitionAsync for partition 3 (should not exist)
            var partition3 = await mbr.GetPartitionAsync(3, CancellationToken.None);
            Assert.IsNull(partition3, "Partition 3 should not exist.");

            // Test GetPartitionAsync for invalid partition number 0
            var partition0 = await mbr.GetPartitionAsync(0, CancellationToken.None);
            Assert.IsNull(partition0, "Partition 0 should not exist (invalid number).");

            mbr.Dispose();
        }

        [Test]
        public async Task Test_GPT_PartitionAlignment_IsSectorAligned()
        {
            var sectorSize = s_gptRawDisk!.SectorSize;

            using var stream = await s_gptRawDisk.ReadSectorsAsync(0, 34, CancellationToken.None);
            var diskBytes = new byte[sectorSize * 34];
            await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

            var gpt = new GPT(null);
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
            var sectorSize = s_mbrRawDisk!.SectorSize;

            using var stream = await s_mbrRawDisk.ReadSectorsAsync(0, 1, CancellationToken.None);
            var mbrBytes = new byte[sectorSize];
            await stream.ReadAtLeastAsync(mbrBytes, mbrBytes.Length, cancellationToken: CancellationToken.None);

            var mbr = new MBR(null);
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

        #endregion
    }
}
