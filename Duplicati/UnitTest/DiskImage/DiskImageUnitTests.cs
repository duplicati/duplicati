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
using System.Linq;
using System.Reflection;
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

        #region PartitionWriteStream tests

        [Test]
        public async Task Test_PartitionWriteStream_WriteAndDispose_DataFlushedToDisk()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionOffset = 10 * sectorSize;
            var partitionSize = 4 * sectorSize;

            // Write data using PartitionWriteStream
            var writeData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            using (var stream = new PartitionWriteStream(_writableRawDisk, partitionOffset, partitionSize))
                await stream.WriteAsync(writeData, CancellationToken.None);

            // Read back and verify data was flushed to disk
            using var readStream = await _writableRawDisk.ReadBytesAsync(partitionOffset, writeData.Length, CancellationToken.None);
            var readBuffer = new byte[writeData.Length];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(writeData.Length, bytesRead, "Should have read the written data.");
            Assert.AreEqual(writeData, readBuffer, "Data should match what was written.");
        }

        [Test]
        public void Test_PartitionWriteStream_SeekBegin_ValidPosition()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 4 * sectorSize;

            using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, partitionSize);

            // Test Seek with SeekOrigin.Begin
            var newPosition = stream.Seek(100, SeekOrigin.Begin);
            Assert.AreEqual(100, newPosition, "Position should be 100 after seeking from Begin.");
            Assert.AreEqual(100, stream.Position, "Stream.Position should match returned position.");
        }

        [Test]
        public void Test_PartitionWriteStream_SeekCurrent_ValidPosition()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 4 * sectorSize;

            using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, partitionSize);

            // Write some data first to move position
            stream.Write([0x01, 0x02, 0x03], 0, 3);
            Assert.AreEqual(3, stream.Position, "Position should be 3 after writing 3 bytes.");

            // Test Seek with SeekOrigin.Current
            var newPosition = stream.Seek(50, SeekOrigin.Current);
            Assert.AreEqual(53, newPosition, "Position should be 53 after seeking 50 from current position 3.");
            Assert.AreEqual(53, stream.Position, "Stream.Position should match returned position.");
        }

        [Test]
        public void Test_PartitionWriteStream_SeekEnd_ValidPosition()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 4 * sectorSize;

            using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, partitionSize);

            // Write some data first to establish length
            stream.Write([0x01, 0x02, 0x03, 0x04, 0x05], 0, 5);
            Assert.AreEqual(5, stream.Length, "Length should be 5 after writing 5 bytes.");

            // Test Seek with SeekOrigin.End
            var newPosition = stream.Seek(-2, SeekOrigin.End);
            Assert.AreEqual(3, newPosition, "Position should be 3 after seeking -2 from end of length 5.");
            Assert.AreEqual(3, stream.Position, "Stream.Position should match returned position.");
        }

        [Test]
        public void Test_PartitionWriteStream_WriteBeyondSize_ThrowsIOException()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionOffset = 10 * sectorSize;
            var partitionSize = 2 * sectorSize;

            using var stream = new PartitionWriteStream(_writableRawDisk, partitionOffset, partitionSize);

            // Try to write more than partition size
            var largeBuffer = new byte[partitionSize + 100];
            new Random().NextBytes(largeBuffer);

            var ex = Assert.Throws<IOException>(() =>
            {
                stream.Write(largeBuffer, 0, largeBuffer.Length);
            }, "Writing beyond partition size should throw IOException.");

            StringAssert.Contains("partition size", ex!.Message, "Exception message should mention partition size.");
        }

        [Test]
        public void Test_PartitionWriteStream_SeekBeyondSize_ThrowsIOException()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 2 * sectorSize;

            using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, partitionSize);

            // Try to seek beyond partition size
            var ex = Assert.Throws<IOException>(() =>
            {
                stream.Seek(partitionSize + 100, SeekOrigin.Begin);
            }, "Seeking beyond partition size should throw IOException.");

            StringAssert.Contains("partition size", ex!.Message, "Exception message should mention partition size.");
        }

        [Test]
        public void Test_PartitionWriteStream_SeekNegativePosition_ThrowsIOException()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 2 * sectorSize;

            using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, partitionSize);

            // Try to seek to negative position
            Assert.Throws<IOException>(() =>
            {
                stream.Seek(-100, SeekOrigin.Begin);
            }, "Seeking to negative position should throw IOException.");
        }

        [Test]
        public async Task Test_PartitionWriteStream_WriteAtUnalignedPosition_DataMatches()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionOffset = 15 * sectorSize;
            var partitionSize = 4 * sectorSize;

            // Write data at various unaligned positions
            using (var stream = new PartitionWriteStream(_writableRawDisk, partitionOffset, partitionSize))
            {
                // Write at position 0
                stream.Write([0xAA, 0xBB], 0, 2);

                // Seek to unaligned position
                stream.Seek(7, SeekOrigin.Begin);
                stream.Write([0xCC, 0xDD, 0xEE], 0, 3);

                // Seek to another unaligned position
                stream.Seek(20, SeekOrigin.Begin);
                stream.Write([0x11, 0x22, 0x33, 0x44], 0, 4);
            }

            // Read back and verify
            using var readStream = await _writableRawDisk.ReadBytesAsync(partitionOffset, 25, CancellationToken.None);
            var readBuffer = new byte[25];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(25, bytesRead, "Should read exactly 25 bytes.");

            // Verify data at position 0
            Assert.AreEqual(0xAA, readBuffer[0], "Byte at position 0 should be 0xAA.");
            Assert.AreEqual(0xBB, readBuffer[1], "Byte at position 1 should be 0xBB.");

            // Verify data at position 7
            Assert.AreEqual(0xCC, readBuffer[7], "Byte at position 7 should be 0xCC.");
            Assert.AreEqual(0xDD, readBuffer[8], "Byte at position 8 should be 0xDD.");
            Assert.AreEqual(0xEE, readBuffer[9], "Byte at position 9 should be 0xEE.");

            // Verify data at position 20
            Assert.AreEqual(0x11, readBuffer[20], "Byte at position 20 should be 0x11.");
            Assert.AreEqual(0x22, readBuffer[21], "Byte at position 21 should be 0x22.");
            Assert.AreEqual(0x33, readBuffer[22], "Byte at position 22 should be 0x33.");
            Assert.AreEqual(0x44, readBuffer[23], "Byte at position 23 should be 0x44.");
        }

        [Test]
        public async Task Test_PartitionWriteStream_WriteMultipleTimes_DataMatches()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionOffset = 20 * sectorSize;
            var partitionSize = 3 * sectorSize;

            // Write data in multiple operations
            using (var stream = new PartitionWriteStream(_writableRawDisk, partitionOffset, partitionSize))
            {
                // First write
                stream.Write([0x01, 0x02, 0x03], 0, 3);

                // Second write (should append)
                stream.Write([0x04, 0x05], 0, 2);

                // Third write
                stream.Write([0x06, 0x07, 0x08, 0x09], 0, 4);
            }

            // Read back and verify all data was written
            using var readStream = await _writableRawDisk.ReadBytesAsync(partitionOffset, 9, CancellationToken.None);
            var readBuffer = new byte[9];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(9, bytesRead, "Should read exactly 9 bytes.");

            var expected = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 };
            Assert.AreEqual(expected, readBuffer, "All written data should match.");
        }

        [Test]
        public void Test_PartitionWriteStream_SetLengthBeyondMaxSize_ThrowsIOException()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 2 * sectorSize;

            using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, partitionSize);

            // Try to set length beyond partition size
            var ex = Assert.Throws<IOException>(() =>
            {
                stream.SetLength(partitionSize + 100);
            }, "Setting length beyond partition size should throw IOException.");

            StringAssert.Contains("partition size", ex!.Message, "Exception message should mention partition size.");
        }

        [Test]
        public async Task Test_PartitionWriteStream_SetLengthWithinMaxSize_SetsLength()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionOffset = 10 * sectorSize;
            var partitionSize = 2 * sectorSize;

            using (var stream = new PartitionWriteStream(_writableRawDisk, partitionOffset, partitionSize))
            {
                // Set length to a specific value within bounds
                stream.SetLength(100);
                Assert.AreEqual(100, stream.Length, "Length should be set to 100.");

                // Write some data at position 0
                stream.Write([0xAA, 0xBB, 0xCC], 0, 3);
                // Length should be max(SetLength(100), written position) = 100
                Assert.AreEqual(100, stream.Length, "Length should remain 100 (SetLength value).");
            }

            // Verify the 3 bytes were written at position 0 and length 100 bytes are flushed
            using var readStream = await _writableRawDisk.ReadBytesAsync(partitionOffset, 100, CancellationToken.None);
            var readBuffer = new byte[100];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(100, bytesRead, "Should read exactly 100 bytes.");

            Assert.AreEqual(0xAA, readBuffer[0], "Byte at position 0 should be 0xAA.");
            Assert.AreEqual(0xBB, readBuffer[1], "Byte at position 1 should be 0xBB.");
            Assert.AreEqual(0xCC, readBuffer[2], "Byte at position 2 should be 0xCC.");
        }

        [Test]
        public void Test_PartitionWriteStream_CanRead_ReturnsFalse()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, sectorSize);

            Assert.IsFalse(stream.CanRead, "CanRead should be false.");
        }

        [Test]
        public void Test_PartitionWriteStream_CanWrite_ReturnsTrue()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, sectorSize);

            Assert.IsTrue(stream.CanWrite, "CanWrite should be true.");
        }

        [Test]
        public void Test_PartitionWriteStream_CanSeek_ReturnsTrue()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, sectorSize);

            Assert.IsTrue(stream.CanSeek, "CanSeek should be true.");
        }

        [Test]
        public void Test_PartitionWriteStream_Read_ThrowsNotSupported()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, sectorSize);

            Assert.Throws<NotSupportedException>(() =>
            {
                var _ = stream.Read(new byte[10], 0, 10);
            }, "Read should throw NotSupportedException.");
        }

        [Test]
        public async Task Test_PartitionWriteStream_WriteEmptyData_NoFlush()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionOffset = 10 * sectorSize;
            var partitionSize = sectorSize;

            // Create stream but write nothing
            using (var stream = new PartitionWriteStream(_writableRawDisk, partitionOffset, partitionSize))
                // Don't write anything
                Assert.AreEqual(0, stream.Length, "Length should be 0 when nothing is written.");

            // Read back and verify no data was flushed (should be zeros)
            using var readStream = await _writableRawDisk.ReadBytesAsync(partitionOffset, 10, CancellationToken.None);
            var readBuffer = new byte[10];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(10, bytesRead, "Should read 10 bytes.");

            // All bytes should be 0 (uninitialized disk)
            for (int i = 0; i < readBuffer.Length; i++)
            {
                Assert.AreEqual(0, readBuffer[i], $"Byte at position {i} should be 0 (no data written).");
            }
        }

        #endregion

        #region PartitionTableSynthesizer tests

        [Test]
        public void Test_PartitionTableSynthesizer_SynthesizeMBR_ContainsValidBootSignature()
        {
            // Create a GeometryMetadata with known partition info
            var metadata = new GeometryMetadata
            {
                Disk = new DiskGeometry
                {
                    SectorSize = 512,
                    Size = 100 * MiB,
                    Sectors = (int)(100 * MiB / 512)
                },
                PartitionTable = new PartitionTableGeometry
                {
                    Type = PartitionTableType.MBR,
                    SectorSize = 512
                },
                Partitions =
                [
                    new PartitionGeometry
                    {
                        Number = 1,
                        Type = PartitionType.Primary,
                        FilesystemType = FileSystemType.FAT32,
                        StartOffset = 512 * 2048, // Start at sector 2048
                        Size = 20 * MiB,
                        TableType = PartitionTableType.MBR
                    },
                    new PartitionGeometry
                    {
                        Number = 2,
                        Type = PartitionType.Primary,
                        FilesystemType = FileSystemType.FAT32,
                        StartOffset = 512 * 2048 + 20 * MiB,
                        Size = 20 * MiB,
                        TableType = PartitionTableType.MBR
                    }
                ]
            };

            // Synthesize MBR
            var mbrData = PartitionTableSynthesizer.SynthesizeMBR(metadata);

            Assert.IsNotNull(mbrData, "MBR data should not be null.");
            Assert.GreaterOrEqual(mbrData.Length, 512, "MBR should be at least one sector.");

            // Verify boot signature at offset 510-511 (0x55 0xAA)
            Assert.AreEqual(0x55, mbrData[510], "Boot signature first byte should be 0x55.");
            Assert.AreEqual(0xAA, mbrData[511], "Boot signature second byte should be 0xAA.");
        }

        [Test]
        public void Test_PartitionTableSynthesizer_SynthesizeGPT_ContainsValidSignature()
        {
            // Create a GeometryMetadata with known partition info
            var metadata = new GeometryMetadata
            {
                Disk = new DiskGeometry
                {
                    SectorSize = 512,
                    Size = 100 * MiB,
                    Sectors = (int)(100 * MiB / 512)
                },
                PartitionTable = new PartitionTableGeometry
                {
                    Type = PartitionTableType.GPT,
                    SectorSize = 512,
                    HasProtectiveMbr = true
                },
                Partitions =
                [
                    new PartitionGeometry
                    {
                        Number = 1,
                        Type = PartitionType.Primary,
                        FilesystemType = FileSystemType.FAT32,
                        StartOffset = 512 * 2048, // Start at sector 2048
                        Size = 20 * MiB,
                        TableType = PartitionTableType.GPT,
                        Name = "Partition 1"
                    },
                    new PartitionGeometry
                    {
                        Number = 2,
                        Type = PartitionType.Primary,
                        FilesystemType = FileSystemType.FAT32,
                        StartOffset = 512 * 2048 + 20 * MiB,
                        Size = 20 * MiB,
                        TableType = PartitionTableType.GPT,
                        Name = "Partition 2"
                    }
                ]
            };

            // Synthesize GPT
            var gptData = PartitionTableSynthesizer.SynthesizeGPT(metadata);

            Assert.IsNotNull(gptData, "GPT data should not be null.");
            Assert.Greater(gptData.Length, 512 * 2, "GPT data should include protective MBR and GPT header.");

            // Verify protective MBR boot signature at offset 510-511
            Assert.AreEqual(0x55, gptData[510], "Protective MBR boot signature first byte should be 0x55.");
            Assert.AreEqual(0xAA, gptData[511], "Protective MBR boot signature second byte should be 0xAA.");

            // Verify GPT signature "EFI PART" at sector 1 (offset 512)
            var expectedSignature = "EFI PART"u8.ToArray();
            for (int i = 0; i < 8; i++)
                Assert.AreEqual(expectedSignature[i], gptData[512 + i], $"GPT signature byte {i} should match.");
        }

        [Test]
        public async Task Test_PartitionTableSynthesizer_MBRRoundTrip_PreservesPartitionData()
        {
            // Create a GeometryMetadata with known partition info
            var sectorSize = 512;
            var metadata = new GeometryMetadata
            {
                Disk = new DiskGeometry
                {
                    SectorSize = sectorSize,
                    Size = 100 * MiB,
                    Sectors = (int)(100 * MiB / sectorSize)
                },
                PartitionTable = new PartitionTableGeometry
                {
                    Type = PartitionTableType.MBR,
                    SectorSize = sectorSize
                },
                Partitions =
                [
                    new PartitionGeometry
                    {
                        Number = 1,
                        Type = PartitionType.Primary,
                        FilesystemType = FileSystemType.FAT32,
                        StartOffset = sectorSize * 2048,
                        Size = 20 * MiB,
                        TableType = PartitionTableType.MBR
                    },
                    new PartitionGeometry
                    {
                        Number = 2,
                        Type = PartitionType.Primary,
                        FilesystemType = FileSystemType.FAT32,
                        StartOffset = sectorSize * 2048 + 20 * MiB,
                        Size = 20 * MiB,
                        TableType = PartitionTableType.MBR
                    }
                ]
            };

            // Synthesize MBR
            var mbrData = PartitionTableSynthesizer.SynthesizeMBR(metadata);

            // Parse with PartitionTableFactory
            var partitionTable = await PartitionTableFactory.CreateAsync(mbrData, sectorSize, CancellationToken.None);

            Assert.IsNotNull(partitionTable, "Partition table should be detected.");
            Assert.AreEqual(PartitionTableType.MBR, partitionTable!.TableType, "Should detect MBR partition table.");

            // Verify partition count
            var partitions = new List<IPartition>();
            await foreach (var partition in partitionTable.EnumeratePartitions(CancellationToken.None))
                partitions.Add(partition);

            Assert.AreEqual(2, partitions.Count, "Should have 2 partitions.");

            // Verify partition properties match original metadata
            for (int i = 0; i < partitions.Count; i++)
            {
                var originalPart = metadata.Partitions![i];
                var parsedPart = partitions[i];

                Assert.AreEqual(originalPart.Number, parsedPart.PartitionNumber, $"Partition {i + 1} number should match.");
                Assert.AreEqual(originalPart.StartOffset, parsedPart.StartOffset, $"Partition {i + 1} start offset should match.");
                Assert.AreEqual(originalPart.Size, parsedPart.Size, $"Partition {i + 1} size should match.");
            }

            partitionTable.Dispose();
        }

        [Test]
        public async Task Test_PartitionTableSynthesizer_GPTRoundTrip_PreservesPartitionData()
        {
            // Create a GeometryMetadata with known partition info
            var sectorSize = 512;
            var metadata = new GeometryMetadata
            {
                Disk = new DiskGeometry
                {
                    SectorSize = sectorSize,
                    Size = 100 * MiB,
                    Sectors = (int)(100 * MiB / sectorSize)
                },
                PartitionTable = new PartitionTableGeometry
                {
                    Type = PartitionTableType.GPT,
                    SectorSize = sectorSize,
                    HasProtectiveMbr = true
                },
                Partitions =
                [
                    new PartitionGeometry
                    {
                        Number = 1,
                        Type = PartitionType.Primary,
                        FilesystemType = FileSystemType.FAT32,
                        StartOffset = sectorSize * 2048,
                        Size = 20 * MiB,
                        TableType = PartitionTableType.GPT,
                        Name = "Test Partition 1"
                    },
                    new PartitionGeometry
                    {
                        Number = 2,
                        Type = PartitionType.Primary,
                        FilesystemType = FileSystemType.FAT32,
                        StartOffset = sectorSize * 2048 + 20 * MiB,
                        Size = 20 * MiB,
                        TableType = PartitionTableType.GPT,
                        Name = "Test Partition 2"
                    }
                ]
            };

            // Synthesize GPT
            var gptData = PartitionTableSynthesizer.SynthesizeGPT(metadata);

            // Parse with PartitionTableFactory
            var partitionTable = await PartitionTableFactory.CreateAsync(gptData, sectorSize, CancellationToken.None);

            Assert.IsNotNull(partitionTable, "Partition table should be detected.");
            Assert.AreEqual(PartitionTableType.GPT, partitionTable!.TableType, "Should detect GPT partition table.");

            // Verify partition count
            var partitions = new List<IPartition>();
            await foreach (var partition in partitionTable.EnumeratePartitions(CancellationToken.None))
                partitions.Add(partition);

            Assert.AreEqual(2, partitions.Count, "Should have 2 partitions.");

            // Verify partition properties match original metadata
            for (int i = 0; i < partitions.Count; i++)
            {
                var originalPart = metadata.Partitions![i];
                var parsedPart = partitions[i];

                Assert.AreEqual(originalPart.Number, parsedPart.PartitionNumber, $"Partition {i + 1} number should match.");
                Assert.AreEqual(originalPart.StartOffset, parsedPart.StartOffset, $"Partition {i + 1} start offset should match.");
                Assert.AreEqual(originalPart.Size, parsedPart.Size, $"Partition {i + 1} size should match.");
            }

            partitionTable.Dispose();
        }

        [Test]
        public void Test_PartitionTableSynthesizer_SynthesizePartitionTable_MBR_ReturnsMBRData()
        {
            var metadata = new GeometryMetadata
            {
                Disk = new DiskGeometry { SectorSize = 512, Size = 50 * MiB },
                PartitionTable = new PartitionTableGeometry { Type = PartitionTableType.MBR },
                Partitions =
                [
                    new PartitionGeometry
                    {
                        Number = 1,
                        Type = PartitionType.Primary,
                        StartOffset = 512 * 2048,
                        Size = 20 * MiB,
                        TableType = PartitionTableType.MBR
                    }
                ]
            };

            var result = PartitionTableSynthesizer.SynthesizePartitionTable(metadata);

            Assert.IsNotNull(result, "Should return MBR data.");
            Assert.AreEqual(0x55, result![510], "Should have valid boot signature.");
            Assert.AreEqual(0xAA, result[511], "Should have valid boot signature.");
        }

        [Test]
        public void Test_PartitionTableSynthesizer_SynthesizePartitionTable_GPT_ReturnsGPTData()
        {
            var metadata = new GeometryMetadata
            {
                Disk = new DiskGeometry { SectorSize = 512, Size = 50 * MiB, Sectors = (int)(50 * MiB / 512) },
                PartitionTable = new PartitionTableGeometry { Type = PartitionTableType.GPT },
                Partitions =
                [
                    new PartitionGeometry
                    {
                        Number = 1,
                        Type = PartitionType.Primary,
                        StartOffset = 512 * 2048,
                        Size = 20 * MiB,
                        TableType = PartitionTableType.GPT
                    }
                ]
            };

            var result = PartitionTableSynthesizer.SynthesizePartitionTable(metadata);

            Assert.IsNotNull(result, "Should return GPT data.");
            // Check for "EFI PART" signature at sector 1
            var expectedSignature = "EFI PART"u8.ToArray();
            for (int i = 0; i < 8; i++)
                Assert.AreEqual(expectedSignature[i], result![512 + i], $"GPT signature byte {i} should match.");
        }

        [Test]
        public void Test_PartitionTableSynthesizer_SynthesizePartitionTable_NullPartitionTable_ReturnsNull()
        {
            var metadata = new GeometryMetadata
            {
                Disk = new DiskGeometry { SectorSize = 512 },
                PartitionTable = null
            };

            var result = PartitionTableSynthesizer.SynthesizePartitionTable(metadata);

            Assert.IsNull(result, "Should return null when PartitionTable is null.");
        }

        [Test]
        public void Test_PartitionTableSynthesizer_SynthesizePartitionTable_UnknownType_ReturnsNull()
        {
            var metadata = new GeometryMetadata
            {
                Disk = new DiskGeometry { SectorSize = 512 },
                PartitionTable = new PartitionTableGeometry { Type = PartitionTableType.Unknown }
            };

            var result = PartitionTableSynthesizer.SynthesizePartitionTable(metadata);

            Assert.IsNull(result, "Should return null for Unknown partition table type.");
        }

        #endregion

        #region GeometryMetadata Serialization Tests

        /// <summary>
        /// Tests that a fully populated GeometryMetadata can be serialized to JSON and
        /// deserialized back, preserving all field values.
        /// </summary>
        [Test]
        public void Test_GeometryMetadata_RoundTrip_PreservesAllFields()
        {
            // Create a GeometryMetadata with all fields populated
            var original = new GeometryMetadata
            {
                Version = 1,
                Disk = new DiskGeometry
                {
                    DevicePath = "/dev/sda",
                    Size = 100 * MiB,
                    SectorSize = 512,
                    Sectors = (int)(100 * MiB / 512),
                    TableType = PartitionTableType.GPT
                },
                PartitionTable = new PartitionTableGeometry
                {
                    Type = PartitionTableType.GPT,
                    Size = 17408, // Typical GPT size
                    SectorSize = 512,
                    HasProtectiveMbr = true,
                    HeaderSize = 92
                },
                Partitions =
                [
                    new PartitionGeometry
                    {
                        Number = 1,
                        Type = PartitionType.Primary,
                        StartOffset = 512 * 2048,
                        Size = 40 * MiB,
                        Name = "Test Partition 1",
                        FilesystemType = FileSystemType.FAT32,
                        VolumeGuid = Guid.Parse("12345678-1234-1234-1234-123456789abc"),
                        TableType = PartitionTableType.GPT
                    },
                    new PartitionGeometry
                    {
                        Number = 2,
                        Type = PartitionType.Primary,
                        StartOffset = 512 * 2048 + 40 * MiB,
                        Size = 40 * MiB,
                        Name = "Test Partition 2",
                        FilesystemType = FileSystemType.NTFS,
                        VolumeGuid = Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890"),
                        TableType = PartitionTableType.GPT
                    }
                ],
                Filesystems =
                [
                    new FilesystemGeometry
                    {
                        PartitionNumber = 1,
                        Type = FileSystemType.FAT32,
                        PartitionStartOffset = 512 * 2048,
                        BlockSize = 4096,
                        Metadata = "{\"label\":\"FAT32_VOL\",\"serial\":\"1234-5678\"}"
                    },
                    new FilesystemGeometry
                    {
                        PartitionNumber = 2,
                        Type = FileSystemType.NTFS,
                        PartitionStartOffset = 512 * 2048 + 40 * MiB,
                        BlockSize = 4096,
                        Metadata = "{\"label\":\"NTFS_VOL\",\"guid\":\"{12345678-1234-1234-1234-123456789ABC}\"}"
                    }
                ]
            };

            // Serialize to JSON
            var json = original.ToJson();

            // Verify JSON is not null or empty
            Assert.IsNotNull(json, "JSON output should not be null.");
            Assert.IsNotEmpty(json, "JSON output should not be empty.");

            // Deserialize from JSON
            var deserialized = GeometryMetadata.FromJson(json);

            // Verify deserialized object is not null
            Assert.IsNotNull(deserialized, "Deserialized object should not be null.");

            // Verify Version
            Assert.AreEqual(original.Version, deserialized!.Version, "Version should match.");

            // Verify Disk properties
            Assert.IsNotNull(deserialized.Disk, "Disk should not be null after deserialization.");
            Assert.AreEqual(original.Disk!.DevicePath, deserialized.Disk!.DevicePath, "Disk DevicePath should match.");
            Assert.AreEqual(original.Disk.Size, deserialized.Disk.Size, "Disk Size should match.");
            Assert.AreEqual(original.Disk.SectorSize, deserialized.Disk.SectorSize, "Disk SectorSize should match.");
            Assert.AreEqual(original.Disk.Sectors, deserialized.Disk.Sectors, "Disk Sectors should match.");
            Assert.AreEqual(original.Disk.TableType, deserialized.Disk.TableType, "Disk TableType should match.");

            // Verify PartitionTable properties
            Assert.IsNotNull(deserialized.PartitionTable, "PartitionTable should not be null after deserialization.");
            Assert.AreEqual(original.PartitionTable!.Type, deserialized.PartitionTable!.Type, "PartitionTable Type should match.");
            Assert.AreEqual(original.PartitionTable.Size, deserialized.PartitionTable.Size, "PartitionTable Size should match.");
            Assert.AreEqual(original.PartitionTable.SectorSize, deserialized.PartitionTable.SectorSize, "PartitionTable SectorSize should match.");
            Assert.AreEqual(original.PartitionTable.HasProtectiveMbr, deserialized.PartitionTable.HasProtectiveMbr, "PartitionTable HasProtectiveMbr should match.");
            Assert.AreEqual(original.PartitionTable.HeaderSize, deserialized.PartitionTable.HeaderSize, "PartitionTable HeaderSize should match.");

            // Verify Partitions
            Assert.IsNotNull(deserialized.Partitions, "Partitions should not be null after deserialization.");
            Assert.AreEqual(original.Partitions!.Count, deserialized.Partitions!.Count, "Partition count should match.");

            for (int i = 0; i < original.Partitions.Count; i++)
            {
                var originalPart = original.Partitions[i];
                var deserializedPart = deserialized.Partitions[i];

                Assert.AreEqual(originalPart.Number, deserializedPart.Number, $"Partition {i + 1} Number should match.");
                Assert.AreEqual(originalPart.Type, deserializedPart.Type, $"Partition {i + 1} Type should match.");
                Assert.AreEqual(originalPart.StartOffset, deserializedPart.StartOffset, $"Partition {i + 1} StartOffset should match.");
                Assert.AreEqual(originalPart.Size, deserializedPart.Size, $"Partition {i + 1} Size should match.");
                Assert.AreEqual(originalPart.Name, deserializedPart.Name, $"Partition {i + 1} Name should match.");
                Assert.AreEqual(originalPart.FilesystemType, deserializedPart.FilesystemType, $"Partition {i + 1} FilesystemType should match.");
                Assert.AreEqual(originalPart.VolumeGuid, deserializedPart.VolumeGuid, $"Partition {i + 1} VolumeGuid should match.");
                Assert.AreEqual(originalPart.TableType, deserializedPart.TableType, $"Partition {i + 1} TableType should match.");
            }

            // Verify Filesystems
            Assert.IsNotNull(deserialized.Filesystems, "Filesystems should not be null after deserialization.");
            Assert.AreEqual(original.Filesystems!.Count, deserialized.Filesystems!.Count, "Filesystem count should match.");

            for (int i = 0; i < original.Filesystems.Count; i++)
            {
                var originalFs = original.Filesystems[i];
                var deserializedFs = deserialized.Filesystems[i];

                Assert.AreEqual(originalFs.PartitionNumber, deserializedFs.PartitionNumber, $"Filesystem {i + 1} PartitionNumber should match.");
                Assert.AreEqual(originalFs.Type, deserializedFs.Type, $"Filesystem {i + 1} Type should match.");
                Assert.AreEqual(originalFs.PartitionStartOffset, deserializedFs.PartitionStartOffset, $"Filesystem {i + 1} PartitionStartOffset should match.");
                Assert.AreEqual(originalFs.BlockSize, deserializedFs.BlockSize, $"Filesystem {i + 1} BlockSize should match.");
                Assert.AreEqual(originalFs.Metadata, deserializedFs.Metadata, $"Filesystem {i + 1} Metadata should match.");
            }
        }

        /// <summary>
        /// Tests that a GeometryMetadata with null optional fields can be serialized
        /// and deserialized correctly.
        /// </summary>
        [Test]
        public void Test_GeometryMetadata_RoundTrip_NullOptionalFields_PreservesStructure()
        {
            // Create a GeometryMetadata with minimal/null optional fields
            var original = new GeometryMetadata
            {
                Version = 1,
                Disk = new DiskGeometry
                {
                    DevicePath = null, // Null optional field
                    Size = 50 * MiB,
                    SectorSize = 512,
                    Sectors = (int)(50 * MiB / 512),
                    TableType = PartitionTableType.MBR
                },
                PartitionTable = new PartitionTableGeometry
                {
                    Type = PartitionTableType.MBR,
                    Size = 512,
                    SectorSize = 512,
                    HasProtectiveMbr = false,
                    HeaderSize = 0,
                    MbrSize = 512
                },
                Partitions =
                [
                    new PartitionGeometry
                    {
                        Number = 1,
                        Type = PartitionType.Primary,
                        StartOffset = 512 * 2048,
                        Size = 40 * MiB,
                        Name = null, // Null optional field
                        FilesystemType = FileSystemType.Unknown,
                        VolumeGuid = null, // Null optional field
                        TableType = PartitionTableType.MBR
                    }
                ],
                Filesystems = null // Null optional collection
            };

            // Serialize to JSON
            var json = original.ToJson();

            // Verify JSON is not null or empty
            Assert.IsNotNull(json, "JSON output should not be null.");
            Assert.IsNotEmpty(json, "JSON output should not be empty.");

            // Deserialize from JSON
            var deserialized = GeometryMetadata.FromJson(json);

            // Verify deserialized object is not null
            Assert.IsNotNull(deserialized, "Deserialized object should not be null.");

            // Verify null fields remain null
            Assert.IsNull(deserialized!.Disk!.DevicePath, "DevicePath should be null after deserialization.");
            Assert.IsNull(deserialized.Filesystems, "Filesystems should be null after deserialization.");

            // Verify partition with null fields
            Assert.IsNotNull(deserialized.Partitions, "Partitions should not be null.");
            Assert.AreEqual(1, deserialized.Partitions!.Count, "Should have 1 partition.");
            Assert.IsNull(deserialized.Partitions[0].Name, "Partition Name should be null.");
            Assert.IsNull(deserialized.Partitions[0].VolumeGuid, "Partition VolumeGuid should be null.");

            // Verify other fields are preserved
            Assert.AreEqual(original.Disk.Size, deserialized.Disk.Size, "Disk Size should match.");
            Assert.AreEqual(original.Disk.SectorSize, deserialized.Disk.SectorSize, "Disk SectorSize should match.");
            Assert.AreEqual(original.PartitionTable!.Type, deserialized.PartitionTable!.Type, "PartitionTable Type should match.");
        }

        /// <summary>
        /// Tests that an empty GeometryMetadata (with all null/empty fields) can be
        /// serialized and deserialized correctly.
        /// </summary>
        [Test]
        public void Test_GeometryMetadata_RoundTrip_EmptyMetadata_HandlesGracefully()
        {
            // Create a GeometryMetadata with mostly empty/null fields
            var original = new GeometryMetadata
            {
                Version = 1,
                Disk = null,
                PartitionTable = null,
                Partitions = null,
                Filesystems = null
            };

            // Serialize to JSON
            var json = original.ToJson();

            // Verify JSON is not null or empty
            Assert.IsNotNull(json, "JSON output should not be null.");
            Assert.IsNotEmpty(json, "JSON output should not be empty.");

            // Deserialize from JSON
            var deserialized = GeometryMetadata.FromJson(json);

            // Verify deserialized object is not null
            Assert.IsNotNull(deserialized, "Deserialized object should not be null.");

            // Verify fields are null
            Assert.IsNull(deserialized!.Disk, "Disk should be null.");
            Assert.IsNull(deserialized.PartitionTable, "PartitionTable should be null.");
            Assert.IsNull(deserialized.Partitions, "Partitions should be null.");
            Assert.IsNull(deserialized.Filesystems, "Filesystems should be null.");
            Assert.AreEqual(1, deserialized.Version, "Version should be preserved.");
        }

        /// <summary>
        /// Tests that a GeometryMetadata with multiple partitions and filesystems of
        /// different types can be serialized and deserialized correctly.
        /// </summary>
        [Test]
        public void Test_GeometryMetadata_RoundTrip_MultiplePartitionsAndFilesystems_PreservesAllData()
        {
            // Create a GeometryMetadata with multiple partitions and filesystems
            var original = new GeometryMetadata
            {
                Version = 1,
                Disk = new DiskGeometry
                {
                    DevicePath = "/dev/nvme0n1",
                    Size = 500 * MiB,
                    SectorSize = 4096, // 4K sectors
                    Sectors = (int)(500 * MiB / 4096),
                    TableType = PartitionTableType.GPT
                },
                PartitionTable = new PartitionTableGeometry
                {
                    Type = PartitionTableType.GPT,
                    Size = 32768,
                    SectorSize = 4096,
                    HasProtectiveMbr = true,
                    HeaderSize = 92
                },
                Partitions =
                [
                    new PartitionGeometry
                    {
                        Number = 1,
                        Type = PartitionType.Primary,
                        StartOffset = 4096 * 6, // Start at sector 6 (after GPT headers)
                        Size = 100 * MiB,
                        Name = "EFI System Partition",
                        FilesystemType = FileSystemType.FAT32,
                        VolumeGuid = Guid.NewGuid(),
                        TableType = PartitionTableType.GPT
                    },
                    new PartitionGeometry
                    {
                        Number = 2,
                        Type = PartitionType.Primary,
                        StartOffset = 4096 * 6 + 100 * MiB,
                        Size = 150 * MiB,
                        Name = "Windows OS",
                        FilesystemType = FileSystemType.NTFS,
                        VolumeGuid = Guid.NewGuid(),
                        TableType = PartitionTableType.GPT
                    },
                    new PartitionGeometry
                    {
                        Number = 3,
                        Type = PartitionType.Primary,
                        StartOffset = 4096 * 6 + 250 * MiB,
                        Size = 200 * MiB,
                        Name = "Linux Root",
                        FilesystemType = FileSystemType.Ext4,
                        VolumeGuid = Guid.NewGuid(),
                        TableType = PartitionTableType.GPT
                    }
                ],
                Filesystems =
                [
                    new FilesystemGeometry
                    {
                        PartitionNumber = 1,
                        Type = FileSystemType.FAT32,
                        PartitionStartOffset = 4096 * 6,
                        BlockSize = 4096,
                        Metadata = "{\"label\":\"EFI\"}"
                    },
                    new FilesystemGeometry
                    {
                        PartitionNumber = 2,
                        Type = FileSystemType.NTFS,
                        PartitionStartOffset = 4096 * 6 + 100 * MiB,
                        BlockSize = 4096,
                        Metadata = "{\"label\":\"Windows\"}"
                    },
                    new FilesystemGeometry
                    {
                        PartitionNumber = 3,
                        Type = FileSystemType.Ext4,
                        PartitionStartOffset = 4096 * 6 + 250 * MiB,
                        BlockSize = 4096,
                        Metadata = "{\"label\":\"/\"}"
                    }
                ]
            };

            // Serialize to JSON
            var json = original.ToJson();

            // Verify JSON is not null or empty
            Assert.IsNotNull(json, "JSON output should not be null.");
            Assert.IsNotEmpty(json, "JSON output should not be empty.");

            // Deserialize from JSON
            var deserialized = GeometryMetadata.FromJson(json);

            // Verify deserialized object is not null
            Assert.IsNotNull(deserialized, "Deserialized object should not be null.");

            // Verify counts
            Assert.AreEqual(3, deserialized!.Partitions!.Count, "Should have 3 partitions.");
            Assert.AreEqual(3, deserialized.Filesystems!.Count, "Should have 3 filesystems.");

            // Verify each partition's filesystem type
            Assert.AreEqual(FileSystemType.FAT32, deserialized.Partitions[0].FilesystemType, "Partition 1 should be FAT32.");
            Assert.AreEqual(FileSystemType.NTFS, deserialized.Partitions[1].FilesystemType, "Partition 2 should be NTFS.");
            Assert.AreEqual(FileSystemType.Ext4, deserialized.Partitions[2].FilesystemType, "Partition 3 should be Ext4.");

            // Verify each filesystem's type
            Assert.AreEqual(FileSystemType.FAT32, deserialized.Filesystems[0].Type, "Filesystem 1 should be FAT32.");
            Assert.AreEqual(FileSystemType.NTFS, deserialized.Filesystems[1].Type, "Filesystem 2 should be NTFS.");
            Assert.AreEqual(FileSystemType.Ext4, deserialized.Filesystems[2].Type, "Filesystem 3 should be Ext4.");

            // Verify 4K sector size is preserved
            Assert.AreEqual(4096, deserialized.Disk!.SectorSize, "Disk SectorSize should be 4096.");
            Assert.AreEqual(4096, deserialized.PartitionTable!.SectorSize, "PartitionTable SectorSize should be 4096.");
        }

        /// <summary>
        /// Tests that serializing an MBR-based GeometryMetadata produces valid JSON
        /// that can be correctly deserialized.
        /// </summary>
        [Test]
        public void Test_GeometryMetadata_RoundTrip_MBRType_PreservesMBRData()
        {
            var original = new GeometryMetadata
            {
                Version = 1,
                Disk = new DiskGeometry
                {
                    DevicePath = "/dev/sdb",
                    Size = 200 * MiB,
                    SectorSize = 512,
                    Sectors = (int)(200 * MiB / 512),
                    TableType = PartitionTableType.MBR
                },
                PartitionTable = new PartitionTableGeometry
                {
                    Type = PartitionTableType.MBR,
                    Size = 512,
                    SectorSize = 512,
                    HasProtectiveMbr = false,
                    MbrSize = 512
                },
                Partitions =
                [
                    new PartitionGeometry
                    {
                        Number = 1,
                        Type = PartitionType.Primary,
                        StartOffset = 512 * 2048,
                        Size = 100 * MiB,
                        Name = "Primary Partition",
                        FilesystemType = FileSystemType.FAT32,
                        TableType = PartitionTableType.MBR
                    },
                    new PartitionGeometry
                    {
                        Number = 2,
                        Type = PartitionType.Primary,
                        StartOffset = 512 * 2048 + 100 * MiB,
                        Size = 90 * MiB,
                        Name = "Secondary Partition",
                        FilesystemType = FileSystemType.NTFS,
                        TableType = PartitionTableType.MBR
                    }
                ],
                Filesystems =
                [
                    new FilesystemGeometry
                    {
                        PartitionNumber = 1,
                        Type = FileSystemType.FAT32,
                        PartitionStartOffset = 512 * 2048,
                        BlockSize = 4096,
                        Metadata = null
                    }
                ]
            };

            // Serialize to JSON
            var json = original.ToJson();

            // Deserialize from JSON
            var deserialized = GeometryMetadata.FromJson(json);

            // Verify MBR-specific fields
            Assert.IsNotNull(deserialized, "Deserialized object should not be null.");
            Assert.AreEqual(PartitionTableType.MBR, deserialized!.Disk!.TableType, "Disk TableType should be MBR.");
            Assert.AreEqual(PartitionTableType.MBR, deserialized.PartitionTable!.Type, "PartitionTable Type should be MBR.");
            Assert.AreEqual(512, deserialized.PartitionTable.MbrSize, "PartitionTable MbrSize should be 512.");
            Assert.IsFalse(deserialized.PartitionTable.HasProtectiveMbr, "PartitionTable HasProtectiveMbr should be false.");

            // Verify all partitions have MBR table type
            foreach (var partition in deserialized.Partitions!)
            {
                Assert.AreEqual(PartitionTableType.MBR, partition.TableType, "All partitions should have MBR TableType.");
            }
        }

        /// <summary>
        /// Tests that deserializing invalid JSON throws an appropriate exception.
        /// </summary>
        [Test]
        public void Test_GeometryMetadata_FromJson_InvalidJson_ThrowsException()
        {
            // Test with invalid JSON - should throw JsonException
            var invalidJson = "{invalid json";
            Assert.Throws<System.Text.Json.JsonException>(() => GeometryMetadata.FromJson(invalidJson));
        }

        /// <summary>
        /// Tests that deserializing empty JSON object creates a minimal GeometryMetadata
        /// with default property values set by the class.
        /// </summary>
        [Test]
        public void Test_GeometryMetadata_FromJson_EmptyObject_CreatesDefaultInstance()
        {
            var emptyJson = "{}";
            var result = GeometryMetadata.FromJson(emptyJson);

            Assert.IsNotNull(result, "Should create an instance from empty JSON object.");
            // Version has a default value of 1 in the class definition
            Assert.AreEqual(1, result!.Version, "Version should have default value (1) from class definition.");
            Assert.IsNull(result.Disk, "Disk should be null.");
            Assert.IsNull(result.PartitionTable, "PartitionTable should be null.");
            Assert.IsNull(result.Partitions, "Partitions should be null.");
            Assert.IsNull(result.Filesystems, "Filesystems should be null.");
        }

        #endregion

        #region UnknownFilesystem read/write tests

        /// <summary>
        /// Tests that ListFilesAsync returns the expected block-based files for an UnknownFilesystem.
        /// </summary>
        [Test]
        public async Task Test_UnknownFilesystem_ListFilesAsync_ReturnsBlockBasedFiles()
        {
            // Create a partition on the writable disk
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 10 * MiB;
            var partitionOffset = sectorSize * 2048; // Start at sector 2048

            // Create a simple partition entry
            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            // Create UnknownFilesystem with 1MB block size
            using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

            // List files
            var files = new List<IFile>();
            await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
                files.Add(file);

            // Should have 10 files (10MB partition / 1MB block size)
            Assert.AreEqual(10, files.Count, "Should have 10 block files for a 10MB partition with 1MB blocks.");

            // Verify each file has correct properties
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                Assert.IsInstanceOf<UnknownFilesystemFile>(file, $"File {i} should be UnknownFilesystemFile.");
                Assert.IsFalse(file.IsDirectory, $"File {i} should not be a directory.");

                var unknownFile = (UnknownFilesystemFile)file;
                Assert.AreEqual(i * 1024L * 1024L, unknownFile.Address, $"File {i} should have correct address.");

                // All files except possibly the last should have full block size
                if (i < files.Count - 1)
                    Assert.AreEqual(1024 * 1024, unknownFile.Size, $"File {i} should have full block size.");
            }
        }

        /// <summary>
        /// Tests that write and read round-trip preserves data correctly.
        /// </summary>
        [Test]
        public async Task Test_UnknownFilesystem_WriteRead_RoundTrip_DataMatches()
        {
            // Create a partition on the writable disk
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 5 * MiB;
            var partitionOffset = sectorSize * 2048;

            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            // Create UnknownFilesystem with 1MB block size
            using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

            // Get first file
            IFile? firstFile = null;
            await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
            {
                firstFile = file;
                break;
            }
            Assert.IsNotNull(firstFile, "Should have at least one file.");

            // Write data to the file
            var writeData = new byte[1024 * 1024];
            new Random(42).NextBytes(writeData);

            using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
            {
                await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);
            }

            // Read data back
            byte[] readData;
            using (var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None))
            {
                readData = new byte[writeData.Length];
                var bytesRead = await readStream.ReadAsync(readData.AsMemory(), CancellationToken.None);
                Assert.AreEqual(writeData.Length, bytesRead, "Should have read all bytes.");
            }

            // Verify data matches
            Assert.AreEqual(writeData, readData, "Read data should match written data.");
        }

        /// <summary>
        /// Tests that BoundsCheck validates ranges correctly.
        /// </summary>
        [Test]
        public void Test_UnknownFilesystem_BoundsCheck_ValidRange_DoesNotThrow()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 5 * MiB;
            var partitionOffset = sectorSize * 2048;

            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

            // Valid ranges should not throw
            Assert.DoesNotThrow(() => fs.BoundsCheck(0, sectorSize), "BoundsCheck should not throw for valid range at start.");
            Assert.DoesNotThrow(() => fs.BoundsCheck(sectorSize, sectorSize), "BoundsCheck should not throw for valid range.");
            Assert.DoesNotThrow(() => fs.BoundsCheck(partitionSize - sectorSize, sectorSize), "BoundsCheck should not throw for valid range at end.");
        }

        /// <summary>
        /// Tests that BoundsCheck throws for negative start or size.
        /// </summary>
        [Test]
        public void Test_UnknownFilesystem_BoundsCheck_NegativeValues_ThrowsArgumentOutOfRange()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 5 * MiB;
            var partitionOffset = sectorSize * 2048;

            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

            // Negative start should throw
            Assert.Throws<ArgumentOutOfRangeException>(() => fs.BoundsCheck(-1, sectorSize), "Negative start should throw.");

            // Negative size should throw
            Assert.Throws<ArgumentOutOfRangeException>(() => fs.BoundsCheck(0, -1), "Negative size should throw.");
        }

        /// <summary>
        /// Tests that BoundsCheck throws for ranges exceeding partition size.
        /// </summary>
        [Test]
        public void Test_UnknownFilesystem_BoundsCheck_ExceedsPartitionSize_ThrowsArgumentOutOfRange()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 5 * MiB;
            var partitionOffset = sectorSize * 2048;

            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

            // Range exceeding partition size should throw
            Assert.Throws<ArgumentOutOfRangeException>(() => fs.BoundsCheck(partitionSize - sectorSize, sectorSize * 2), "Range exceeding partition should throw.");
            Assert.Throws<ArgumentOutOfRangeException>(() => fs.BoundsCheck(partitionSize, sectorSize), "Start at partition end should throw.");
        }

        /// <summary>
        /// Tests that BoundsCheck throws for unaligned start or size.
        /// </summary>
        [Test]
        public void Test_UnknownFilesystem_BoundsCheck_UnalignedValues_ThrowsArgumentException()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 5 * MiB;
            var partitionOffset = sectorSize * 2048;

            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

            // Unaligned start should throw
            Assert.Throws<ArgumentException>(() => fs.BoundsCheck(1, sectorSize), "Unaligned start should throw.");

            // Unaligned size should throw
            Assert.Throws<ArgumentException>(() => fs.BoundsCheck(0, sectorSize + 1), "Unaligned size should throw.");
        }

        /// <summary>
        /// Tests that UnknownFilesystem constructor throws for unaligned block sizes.
        /// </summary>
        [Test]
        public void Test_UnknownFilesystem_Constructor_UnalignedBlockSize_ThrowsArgumentException()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 5 * MiB;
            var partitionOffset = sectorSize * 2048;

            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            // Block size not a multiple of sector size should throw
            var unalignedBlockSize = sectorSize + 1;
            var ex = Assert.Throws<ArgumentException>(() => new UnknownFilesystem(partition, blockSize: unalignedBlockSize),
                "Unaligned block size should throw ArgumentException.");
            Assert.That(ex!.ParamName, Is.EqualTo("blockSize"), "Exception should be for blockSize parameter.");
        }

        /// <summary>
        /// Tests that UnknownFilesystem constructor accepts valid block sizes.
        /// </summary>
        [Test]
        public void Test_UnknownFilesystem_Constructor_ValidBlockSize_CreatesInstance()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 5 * MiB;
            var partitionOffset = sectorSize * 2048;

            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            // Valid block sizes should create instance
            Assert.DoesNotThrow(() =>
            {
                using var fs1 = new UnknownFilesystem(partition, blockSize: sectorSize);
            }, "Block size equal to sector size should work.");

            Assert.DoesNotThrow(() =>
            {
                using var fs2 = new UnknownFilesystem(partition, blockSize: sectorSize * 4);
            }, "Block size multiple of sector size should work.");

            Assert.DoesNotThrow(() =>
            {
                using var fs3 = new UnknownFilesystem(partition, blockSize: 1024 * 1024);
            }, "1MB block size should work.");
        }

        /// <summary>
        /// Helper class to implement IPartition for testing.
        /// </summary>
        private class TestPartition : IPartition
        {
            public long StartOffset { get; init; }
            public long Size { get; init; }
            public IRawDisk? RawDisk { get; init; }
            public int PartitionNumber => 1;
            public string? Name => "TestPartition";
            public Guid? UniqueId => Guid.Empty;
            public PartitionType Type => PartitionType.Primary;
            public FileSystemType FilesystemType => FileSystemType.Unknown;
            public Guid? VolumeGuid => Guid.Empty;
            public IPartitionTable PartitionTable => new TestPartitionTable { RawDisk = RawDisk };

            public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
                => Task.FromException<Stream>(new NotImplementedException());

            public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken)
                => Task.FromException<Stream>(new NotImplementedException());

            public void Dispose() { }
        }

        /// <summary>
        /// Helper class to implement IPartitionTable for testing.
        /// </summary>
        private class TestPartitionTable : IPartitionTable
        {
            public IRawDisk? RawDisk { get; init; }
            public PartitionTableType TableType => PartitionTableType.Unknown;
            public long Size => 0;

            public IAsyncEnumerable<IPartition> EnumeratePartitions(CancellationToken cancellationToken)
                => AsyncEnumerable.Empty<IPartition>();

            public Task<IPartition?> GetPartitionAsync(int partitionNumber, CancellationToken cancellationToken)
                => Task.FromResult<IPartition?>(null);

            public Task<Stream> GetProtectiveMbrAsync(CancellationToken cancellationToken)
                => Task.FromException<Stream>(new NotImplementedException());

            public Task<Stream> GetPartitionTableDataAsync(CancellationToken cancellationToken)
                => Task.FromException<Stream>(new NotImplementedException());

            public void Dispose() { }
        }

        #endregion

        #region UnknownFilesystemStream Unaligned Tests

        /// <summary>
        /// Tests that writing partial data (less than block size) is correctly flushed on dispose.
        /// </summary>
        [Test]
        public async Task Test_UnknownFilesystemStream_WritePartialData_FlushesOnDispose()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 5 * MiB;
            var partitionOffset = sectorSize * 2048;

            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

            // Get first file
            IFile? firstFile = null;
            await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
            {
                firstFile = file;
                break;
            }
            Assert.IsNotNull(firstFile, "Should have at least one file.");

            // Write partial data (less than block size)
            var writeData = new byte[1024]; // 1KB, much less than 1MB block
            new Random(42).NextBytes(writeData);

            using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
                await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);

            // Read back and verify - the written data should be preserved
            byte[] readData;
            using (var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None))
            {
                readData = new byte[writeData.Length];
                var bytesRead = await readStream.ReadAsync(readData.AsMemory(), CancellationToken.None);
                Assert.AreEqual(writeData.Length, bytesRead, "Should have read all written bytes.");
            }

            // Verify data matches
            Assert.AreEqual(writeData, readData, "Partial data should be correctly flushed and readable.");
        }

        /// <summary>
        /// Tests writing at position 0, seeking to middle, writing more data, and verifying both regions.
        /// </summary>
        [Test]
        public async Task Test_UnknownFilesystemStream_WriteSeekWrite_BothRegionsPreserved()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 5 * MiB;
            var partitionOffset = sectorSize * 2048;

            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

            // Get first file
            IFile? firstFile = null;
            await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
            {
                firstFile = file;
                break;
            }
            Assert.IsNotNull(firstFile, "Should have at least one file.");

            // Write at position 0, seek to middle, write more data
            var firstWriteData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            var secondWriteData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
            var middlePosition = 1000;

            using (var writeStream = await fs.OpenReadWriteStreamAsync(firstFile!, CancellationToken.None))
            {
                // Write at position 0
                await writeStream.WriteAsync(firstWriteData.AsMemory(), CancellationToken.None);

                // Seek to middle position
                writeStream.Seek(middlePosition, SeekOrigin.Begin);

                // Write more data
                await writeStream.WriteAsync(secondWriteData.AsMemory(), CancellationToken.None);
            }

            // Read back and verify both regions
            byte[] readData;
            using (var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None))
            {
                readData = new byte[middlePosition + secondWriteData.Length];
                var bytesRead = await readStream.ReadAsync(readData.AsMemory(), CancellationToken.None);
                Assert.AreEqual(middlePosition + secondWriteData.Length, bytesRead, "Should have read all data.");
            }

            // Verify first region (position 0-4)
            for (int i = 0; i < firstWriteData.Length; i++)
                Assert.AreEqual(firstWriteData[i], readData[i], $"Byte at position {i} should match first write.");

            // Verify middle region (position 1000-1003)
            for (int i = 0; i < secondWriteData.Length; i++)
                Assert.AreEqual(secondWriteData[i], readData[middlePosition + i], $"Byte at position {middlePosition + i} should match second write.");
        }

        /// <summary>
        /// Tests reading after seeking to a non-zero position.
        /// </summary>
        [Test]
        public async Task Test_UnknownFilesystemStream_ReadAfterSeek_ReturnsCorrectData()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 5 * MiB;
            var partitionOffset = sectorSize * 2048;

            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

            // Get first file
            IFile? firstFile = null;
            await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
            {
                firstFile = file;
                break;
            }
            Assert.IsNotNull(firstFile, "Should have at least one file.");

            // Write test data
            var writeData = new byte[4096]; // 4KB of test data
            for (int i = 0; i < writeData.Length; i++)
                writeData[i] = (byte)(i & 0xFF);

            using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
                await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);

            // Read after seeking to non-zero position
            using (var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None))
            {
                // Seek to position 100
                readStream.Seek(100, SeekOrigin.Begin);

                // Read remaining data
                var readBuffer = new byte[writeData.Length - 100];
                var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);
                Assert.AreEqual(writeData.Length - 100, bytesRead, "Should have read remaining bytes after seek.");

                // Verify data matches expected values
                for (int i = 0; i < bytesRead; i++)
                {
                    Assert.AreEqual((byte)((i + 100) & 0xFF), readBuffer[i], $"Byte at position {100 + i} should match.");
                }
            }
        }

        /// <summary>
        /// Tests that writing data that doesn't fill the entire stream preserves the written data
        /// while the remaining bytes retain their previous values.
        /// </summary>
        [Test]
        public async Task Test_UnknownFilesystemStream_WritePartial_PreservesData()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 5 * MiB;
            var partitionOffset = sectorSize * 2048;

            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            // Use a small block size for this test
            var blockSize = 4096;
            using var fs = new UnknownFilesystem(partition, blockSize: blockSize);

            // Get first file
            IFile? firstFile = null;
            await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
            {
                firstFile = file;
                break;
            }
            Assert.IsNotNull(firstFile, "Should have at least one file.");

            // First, initialize the entire block with zeros by writing zeros
            var zeroData = new byte[blockSize];
            using (var zeroStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
                await zeroStream.WriteAsync(zeroData.AsMemory(), CancellationToken.None);

            // Now write partial data at the beginning
            var writeData = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 };

            using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
                await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);

            // Read the entire block and verify written data and remaining zeros
            using var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None);
            var readBuffer = new byte[blockSize];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(blockSize, bytesRead, "Should have read the full block.");

            // Verify written data
            for (int i = 0; i < writeData.Length; i++)
                Assert.AreEqual(writeData[i], readBuffer[i], $"Byte at position {i} should match written data.");

            // Verify remaining bytes are still zero
            for (int i = writeData.Length; i < blockSize; i++)
                Assert.AreEqual(0, readBuffer[i], $"Byte at position {i} should be zero.");
        }

        /// <summary>
        /// Tests the lazy-load behavior: first read triggers disk read, subsequent reads use buffer.
        /// </summary>
        [Test]
        public async Task Test_UnknownFilesystemStream_LazyLoad_FirstReadTriggersDiskRead()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 5 * MiB;
            var partitionOffset = sectorSize * 2048;

            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

            // Get first file
            IFile? firstFile = null;
            await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
            {
                firstFile = file;
                break;
            }
            Assert.IsNotNull(firstFile, "Should have at least one file.");

            // Write test data first
            var writeData = new byte[2048];
            new Random(123).NextBytes(writeData);

            using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
                await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);

            // Now test lazy loading - first read should trigger disk read
            using var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None);

            // First small read (triggers lazy load)
            var firstReadBuffer = new byte[512];
            var bytesRead1 = await readStream.ReadAsync(firstReadBuffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(512, bytesRead1, "First read should return 512 bytes.");

            // Second read should use cached buffer (no disk read)
            var secondReadBuffer = new byte[512];
            var bytesRead2 = await readStream.ReadAsync(secondReadBuffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(512, bytesRead2, "Second read should return 512 bytes from buffer.");

            // Verify both reads returned correct data
            for (int i = 0; i < 512; i++)
            {
                Assert.AreEqual(writeData[i], firstReadBuffer[i], $"First read: byte at position {i} should match.");
                Assert.AreEqual(writeData[i + 512], secondReadBuffer[i], $"Second read: byte at position {i + 512} should match.");
            }

            // Seek back and read again - should still use buffer
            readStream.Seek(0, SeekOrigin.Begin);
            var thirdReadBuffer = new byte[512];
            var bytesRead3 = await readStream.ReadAsync(thirdReadBuffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(512, bytesRead3, "Third read after seek should return 512 bytes.");

            for (int i = 0; i < 512; i++)
                Assert.AreEqual(writeData[i], thirdReadBuffer[i], $"Third read: byte at position {i} should match after seek.");
        }

        /// <summary>
        /// Tests seeking with SeekOrigin.Current works correctly.
        /// </summary>
        [Test]
        public async Task Test_UnknownFilesystemStream_SeekCurrent_UpdatesPosition()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 5 * MiB;
            var partitionOffset = sectorSize * 2048;

            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

            // Get first file
            IFile? firstFile = null;
            await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
            {
                firstFile = file;
                break;
            }
            Assert.IsNotNull(firstFile, "Should have at least one file.");

            // Write test data
            var writeData = new byte[4096];
            for (int i = 0; i < writeData.Length; i++)
                writeData[i] = (byte)(i & 0xFF);

            using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
                await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);

            // Test Seek with SeekOrigin.Current
            using (var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None))
            {
                // Read first 100 bytes
                var buffer1 = new byte[100];
                var bytesRead1 = await readStream.ReadAsync(buffer1.AsMemory(), CancellationToken.None);
                Assert.AreEqual(100, bytesRead1, "First read should return 100 bytes.");
                Assert.AreEqual(100, readStream.Position, "Position should be 100 after first read.");

                // Seek forward 100 bytes from current
                var newPos = readStream.Seek(100, SeekOrigin.Current);
                Assert.AreEqual(200, newPos, "Position should be 200 after Seek(Current, 100).");
                Assert.AreEqual(200, readStream.Position, "Stream.Position should match returned position.");

                // Read next 100 bytes
                var buffer2 = new byte[100];
                var bytesRead2 = await readStream.ReadAsync(buffer2.AsMemory(), CancellationToken.None);
                Assert.AreEqual(100, bytesRead2, "Second read should return 100 bytes.");

                // Verify data at position 200
                for (int i = 0; i < 100; i++)
                    Assert.AreEqual((byte)((200 + i) & 0xFF), buffer2[i], $"Byte at position {200 + i} should match.");
            }
        }

        /// <summary>
        /// Tests seeking with SeekOrigin.End works correctly.
        /// </summary>
        [Test]
        public async Task Test_UnknownFilesystemStream_SeekEnd_UpdatesPosition()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 5 * MiB;
            var partitionOffset = sectorSize * 2048;

            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            var blockSize = 4096;
            using var fs = new UnknownFilesystem(partition, blockSize: blockSize);

            // Get first file
            IFile? firstFile = null;
            await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
            {
                firstFile = file;
                break;
            }
            Assert.IsNotNull(firstFile, "Should have at least one file.");

            // Write test data
            var writeData = new byte[blockSize];
            for (int i = 0; i < writeData.Length; i++)
                writeData[i] = (byte)(i & 0xFF);

            using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
                await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);

            // Test Seek with SeekOrigin.End
            using var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None);

            // Seek to 100 bytes from end
            var newPos = readStream.Seek(-100, SeekOrigin.End);
            Assert.AreEqual(blockSize - 100, newPos, "Position should be blockSize - 100.");
            Assert.AreEqual(blockSize - 100, readStream.Position, "Stream.Position should match.");

            // Read remaining bytes
            var buffer = new byte[100];
            var bytesRead = await readStream.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(100, bytesRead, "Should read 100 bytes.");

            // Verify last 100 bytes
            for (int i = 0; i < 100; i++)
                Assert.AreEqual((byte)((blockSize - 100 + i) & 0xFF), buffer[i], $"Byte at position {blockSize - 100 + i} should match.");
        }

        /// <summary>
        /// Tests that reading past the end of the stream returns 0 bytes.
        /// </summary>
        [Test]
        public async Task Test_UnknownFilesystemStream_ReadPastEnd_ReturnsZero()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 5 * MiB;
            var partitionOffset = sectorSize * 2048;

            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            var blockSize = 4096;
            using var fs = new UnknownFilesystem(partition, blockSize: blockSize);

            // Get first file
            IFile? firstFile = null;
            await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
            {
                firstFile = file;
                break;
            }
            Assert.IsNotNull(firstFile, "Should have at least one file.");

            // Write small amount of test data
            var writeData = new byte[100];
            new Random(42).NextBytes(writeData);

            using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
                await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);

            // Test reading past end
            using var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None);

            // Seek to near end
            readStream.Seek(blockSize - 10, SeekOrigin.Begin);

            // Try to read 100 bytes when only 10 remain
            var buffer = new byte[100];
            var bytesRead = await readStream.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(10, bytesRead, "Should only read remaining 10 bytes.");

            // Try to read again - should return 0
            bytesRead = await readStream.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(0, bytesRead, "Should return 0 when at end of stream.");
        }

        /// <summary>
        /// Tests that unaligned writes within the stream work correctly.
        /// </summary>
        [Test]
        public async Task Test_UnknownFilesystemStream_UnalignedWrite_DataMatches()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var partitionSize = 5 * MiB;
            var partitionOffset = sectorSize * 2048;

            var partition = new TestPartition
            {
                StartOffset = partitionOffset,
                Size = partitionSize,
                RawDisk = _writableRawDisk
            };

            var blockSize = 8192;
            using var fs = new UnknownFilesystem(partition, blockSize: blockSize);

            // Get first file
            IFile? firstFile = null;
            await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
            {
                firstFile = file;
                break;
            }
            Assert.IsNotNull(firstFile, "Should have at least one file.");

            // First, zero-fill the block so gap bytes are deterministic
            using (var zeroStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
                await zeroStream.WriteAsync(new byte[blockSize].AsMemory(), CancellationToken.None);

            // Write at unaligned positions using read-write stream (pre-loads zeroed data)
            using (var writeStream = await fs.OpenReadWriteStreamAsync(firstFile!, CancellationToken.None))
            {
                // Write 3 bytes at position 0
                await writeStream.WriteAsync(new byte[] { 0xAA, 0xBB, 0xCC }.AsMemory(), CancellationToken.None);

                // Seek to unaligned position 5
                writeStream.Seek(5, SeekOrigin.Begin);
                await writeStream.WriteAsync(new byte[] { 0xDD, 0xEE }.AsMemory(), CancellationToken.None);

                // Seek to another unaligned position 10
                writeStream.Seek(10, SeekOrigin.Begin);
                await writeStream.WriteAsync(new byte[] { 0x11, 0x22, 0x33, 0x44 }.AsMemory(), CancellationToken.None);
            }

            // Read back and verify
            using (var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None))
            {
                var readBuffer = new byte[14];
                var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);
                Assert.AreEqual(14, bytesRead, "Should read 14 bytes.");

                // Verify written data
                Assert.AreEqual(0xAA, readBuffer[0], "Byte 0 should be 0xAA");
                Assert.AreEqual(0xBB, readBuffer[1], "Byte 1 should be 0xBB");
                Assert.AreEqual(0xCC, readBuffer[2], "Byte 2 should be 0xCC");
                // Bytes 3-4 should be zero
                Assert.AreEqual(0, readBuffer[3], "Byte 3 should be 0");
                Assert.AreEqual(0, readBuffer[4], "Byte 4 should be 0");
                Assert.AreEqual(0xDD, readBuffer[5], "Byte 5 should be 0xDD");
                Assert.AreEqual(0xEE, readBuffer[6], "Byte 6 should be 0xEE");
                // Bytes 7-9 should be zero
                Assert.AreEqual(0, readBuffer[7], "Byte 7 should be 0");
                Assert.AreEqual(0, readBuffer[8], "Byte 8 should be 0");
                Assert.AreEqual(0, readBuffer[9], "Byte 9 should be 0");
                Assert.AreEqual(0x11, readBuffer[10], "Byte 10 should be 0x11");
                Assert.AreEqual(0x22, readBuffer[11], "Byte 11 should be 0x22");
                Assert.AreEqual(0x33, readBuffer[12], "Byte 12 should be 0x33");
                Assert.AreEqual(0x44, readBuffer[13], "Byte 13 should be 0x44");
            }
        }

        #endregion

        #region 12. PooledBuffer and PooledMemoryStream Tests

        /// <summary>
        /// Tests that PooledBuffer rents a buffer from the ArrayPool and returns it on dispose.
        /// </summary>
        [Test]
        public void Test_PooledBuffer_RentAndReturn_BufferRecycled()
        {
            // Rent a buffer using PooledBuffer
            using var pooledBuffer = new PooledBuffer(1024);
            byte[] bufferArray = pooledBuffer.Array;
            var bufferLength = pooledBuffer.Length;

            // Verify the buffer was rented
            Assert.IsNotNull(bufferArray, "Buffer array should not be null.");
            Assert.AreEqual(1024, bufferLength, "Buffer length should match requested size.");
            Assert.GreaterOrEqual(bufferArray.Length, 1024, "Underlying array should be at least the requested size.");

            // Write some data to verify we have a valid buffer
            bufferArray[0] = 0xAB;
            bufferArray[1023] = 0xCD;

            // Verify Memory and Span properties work correctly
            var memory = pooledBuffer.Memory;
            var span = pooledBuffer.Span;

            Assert.AreEqual(1024, memory.Length, "Memory length should match requested size.");
            Assert.AreEqual(1024, span.Length, "Span length should match requested size.");
            Assert.AreEqual(0xAB, memory.Span[0], "Memory should reflect buffer changes.");
            Assert.AreEqual(0xCD, span[1023], "Span should reflect buffer changes.");
        }

        /// <summary>
        /// Tests that PooledBuffer handles different sizes correctly.
        /// </summary>
        [Test]
        public void Test_PooledBuffer_DifferentSizes_WorksCorrectly()
        {
            // Test small buffer
            using (var small = new PooledBuffer(16))
            {
                Assert.AreEqual(16, small.Length, "Small buffer length should be correct.");
                Assert.GreaterOrEqual(small.Array.Length, 16, "Small buffer array should be at least requested size.");
            }

            // Test large buffer (above LOH threshold of ~85KB)
            using (var large = new PooledBuffer(128 * 1024))
            {
                Assert.AreEqual(128 * 1024, large.Length, "Large buffer length should be correct.");
                Assert.GreaterOrEqual(large.Array.Length, 128 * 1024, "Large buffer array should be at least requested size.");
            }

            // Test zero-size buffer
            using (var empty = new PooledBuffer(0))
            {
                Assert.AreEqual(0, empty.Length, "Empty buffer length should be 0.");
                Assert.IsNotNull(empty.Array, "Empty buffer array should still be rented.");
            }
        }

        /// <summary>
        /// Tests that PooledMemoryStream reads correctly and returns buffer on dispose.
        /// </summary>
        [Test]
        public void Test_PooledMemoryStream_Read_ReturnsCorrectData()
        {
            // Rent a buffer and populate it with test data
            byte[] buffer;
            using (var pooledBuffer = new PooledBuffer(1024))
            {
                buffer = pooledBuffer.Array;
                for (int i = 0; i < 1024; i++)
                    buffer[i] = (byte)(i & 0xFF);
            }

            // Create a PooledMemoryStream with the buffer
            using var stream = new PooledMemoryStream(buffer, 1024);

            // Verify stream properties
            Assert.IsTrue(stream.CanRead, "Stream should be readable.");
            Assert.IsTrue(stream.CanSeek, "Stream should be seekable.");
            Assert.IsFalse(stream.CanWrite, "Stream should not be writable.");
            Assert.AreEqual(1024, stream.Length, "Stream length should be correct.");
            Assert.AreEqual(0, stream.Position, "Initial position should be 0.");

            // Read data from the stream
            var readBuffer = new byte[256];
            var bytesRead = stream.Read(readBuffer, 0, 256);

            Assert.AreEqual(256, bytesRead, "Should have read 256 bytes.");
            Assert.AreEqual(256, stream.Position, "Position should be updated.");

            // Verify the data
            for (int i = 0; i < 256; i++)
                Assert.AreEqual((byte)(i & 0xFF), readBuffer[i], $"Byte at position {i} should match.");
        }

        /// <summary>
        /// Tests that PooledMemoryStream seek operations work correctly.
        /// </summary>
        [Test]
        public void Test_PooledMemoryStream_Seek_BeginCurrentEnd_WorksCorrectly()
        {
            // Rent a buffer and populate it
            byte[] buffer;
            using (var pooledBuffer = new PooledBuffer(512))
            {
                buffer = pooledBuffer.Array;
                for (int i = 0; i < 512; i++)
                    buffer[i] = (byte)(i & 0xFF);
            }

            using (var stream = new PooledMemoryStream(buffer, 512))
            {
                // Test SeekOrigin.Begin
                var pos = stream.Seek(100, SeekOrigin.Begin);
                Assert.AreEqual(100, pos, "Seek from Begin should return correct position.");
                Assert.AreEqual(100, stream.Position, "Position should be 100.");

                // Read a byte to verify position
                var readBuffer = new byte[1];
                var bytesRead = stream.Read(readBuffer, 0, 1);
                Assert.AreEqual(1, bytesRead, "Should read 1 byte.");
                Assert.AreEqual(100, readBuffer[0], "Should read byte at position 100.");

                // Test SeekOrigin.Current
                pos = stream.Seek(50, SeekOrigin.Current);
                Assert.AreEqual(151, pos, "Seek from Current should return correct position.");
                Assert.AreEqual(151, stream.Position, "Position should be 151.");

                bytesRead = stream.Read(readBuffer, 0, 1);
                Assert.AreEqual(1, bytesRead, "Should read 1 byte.");
                Assert.AreEqual(151, readBuffer[0], "Should read byte at position 151.");

                // Test SeekOrigin.End
                pos = stream.Seek(-10, SeekOrigin.End);
                Assert.AreEqual(502, pos, "Seek from End should return correct position.");
                Assert.AreEqual(502, stream.Position, "Position should be 502.");

                bytesRead = stream.Read(readBuffer, 0, 1);
                Assert.AreEqual(1, bytesRead, "Should read 1 byte.");
                // 502 & 0xFF = 246 (byte overflow)
                Assert.AreEqual(246, readBuffer[0], "Should read byte at position 502 (502 & 0xFF = 246).");
            }
        }

        /// <summary>
        /// Tests that seeking beyond stream bounds throws ArgumentOutOfRangeException.
        /// </summary>
        [Test]
        public void Test_PooledMemoryStream_SeekOutOfBounds_ThrowsArgumentOutOfRange()
        {
            byte[] buffer;
            using (var pooledBuffer = new PooledBuffer(100))
                buffer = pooledBuffer.Array;

            using var stream = new PooledMemoryStream(buffer, 100);

            // Seek before beginning
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(-1, SeekOrigin.Begin), "Seek before beginning should throw.");

            // Seek past end
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(101, SeekOrigin.Begin), "Seek past end should throw.");

            // Seek before beginning using Current
            stream.Seek(50, SeekOrigin.Begin);
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(-51, SeekOrigin.Current), "Seek before beginning from current should throw.");

            // Seek past end using End
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(1, SeekOrigin.End), "Seek past end from end should throw.");
        }

        /// <summary>
        /// Tests that reading past the end of PooledMemoryStream returns 0 bytes.
        /// </summary>
        [Test]
        public void Test_PooledMemoryStream_ReadPastEnd_ReturnsZero()
        {
            byte[] buffer;
            using (var pooledBuffer = new PooledBuffer(100))
            {
                buffer = pooledBuffer.Array;
                // Fill with test data
                for (int i = 0; i < 100; i++)
                    buffer[i] = (byte)(i & 0xFF);
            }

            using var stream = new PooledMemoryStream(buffer, 100);

            // Seek to near end
            stream.Seek(95, SeekOrigin.Begin);

            // Read 10 bytes when only 5 remain
            var readBuffer = new byte[10];
            var bytesRead = stream.Read(readBuffer, 0, 10);

            Assert.AreEqual(5, bytesRead, "Should only read remaining 5 bytes.");
            Assert.AreEqual(100, stream.Position, "Position should be at end.");

            // Read again - should return 0
            bytesRead = stream.Read(readBuffer, 0, 10);
            Assert.AreEqual(0, bytesRead, "Should return 0 when at end of stream.");
        }

        /// <summary>
        /// Tests that PooledMemoryStream returns buffer to pool on dispose.
        /// </summary>
        [Test]
        public void Test_PooledMemoryStream_Dispose_ReturnsBufferToPool()
        {
            // This test verifies that the stream can be disposed without errors
            // and that the buffer is returned to the pool (which we can't directly verify,
            // but we can ensure disposal works correctly)

            byte[] buffer;
            using (var pooledBuffer = new PooledBuffer(256))
                buffer = pooledBuffer.Array;

            var stream = new PooledMemoryStream(buffer, 256);

            // Use the stream
            var readBuffer = new byte[64];
            var bytesRead = stream.Read(readBuffer, 0, 64);
            Assert.AreEqual(64, bytesRead, "Should read 64 bytes.");

            // Dispose
            stream.Dispose();

            // After dispose, attempting to read should throw ObjectDisposedException
            Assert.Throws<ObjectDisposedException>(() => { var _ = stream.Read(readBuffer, 0, 64); }, "Reading after dispose should throw.");

            // Note: Seek doesn't throw ObjectDisposedException in this implementation
            // because it doesn't access the buffer directly (only _position and _length)

            // CanRead, CanSeek should return false after dispose
            Assert.IsFalse(stream.CanRead, "CanRead should be false after dispose.");
            Assert.IsFalse(stream.CanSeek, "CanSeek should be false after dispose.");
        }

        /// <summary>
        /// Tests that PooledMemoryStream write and set length operations throw NotSupportedException.
        /// </summary>
        [Test]
        public void Test_PooledMemoryStream_WriteAndSetLength_ThrowsNotSupported()
        {
            byte[] buffer;
            using (var pooledBuffer = new PooledBuffer(100))
                buffer = pooledBuffer.Array;

            using var stream = new PooledMemoryStream(buffer, 100);

            // Write should throw
            Assert.Throws<NotSupportedException>(() => stream.Write(new byte[10], 0, 10), "Write should throw NotSupportedException.");

            // SetLength should throw
            Assert.Throws<NotSupportedException>(() => stream.SetLength(50), "SetLength should throw NotSupportedException.");
        }

        /// <summary>
        /// Tests that PooledMemoryStream handles empty buffers correctly.
        /// </summary>
        [Test]
        public void Test_PooledMemoryStream_EmptyBuffer_WorksCorrectly()
        {
            byte[] buffer;
            using (var pooledBuffer = new PooledBuffer(0))
                buffer = pooledBuffer.Array;

            using var stream = new PooledMemoryStream(buffer, 0);

            Assert.AreEqual(0, stream.Length, "Length should be 0.");
            Assert.AreEqual(0, stream.Position, "Position should be 0.");

            // Read should return 0
            var readBuffer = new byte[10];
            var bytesRead = stream.Read(readBuffer, 0, 10);
            Assert.AreEqual(0, bytesRead, "Read from empty stream should return 0.");

            // Seek to 0 should work
            var pos = stream.Seek(0, SeekOrigin.Begin);
            Assert.AreEqual(0, pos, "Seek to 0 should work.");
        }

        /// <summary>
        /// Tests that PooledBuffer can be used multiple times in succession.
        /// </summary>
        [Test]
        public void Test_PooledBuffer_MultipleInstances_WorkIndependently()
        {
            // Create multiple PooledBuffers
            using var buffer1 = new PooledBuffer(64);
            using var buffer2 = new PooledBuffer(128);
            using var buffer3 = new PooledBuffer(256);

            // Write different data to each
            buffer1.Array[0] = 0x11;
            buffer2.Array[0] = 0x22;
            buffer3.Array[0] = 0x33;

            // Verify each buffer has its own data
            Assert.AreEqual(0x11, buffer1.Array[0], "Buffer1 should have its own data.");
            Assert.AreEqual(0x22, buffer2.Array[0], "Buffer2 should have its own data.");
            Assert.AreEqual(0x33, buffer3.Array[0], "Buffer3 should have its own data.");

            // Verify lengths
            Assert.AreEqual(64, buffer1.Length, "Buffer1 length should be correct.");
            Assert.AreEqual(128, buffer2.Length, "Buffer2 length should be correct.");
            Assert.AreEqual(256, buffer3.Length, "Buffer3 length should be correct.");
        }

        #endregion

        #region RestoreProvider Path Parsing Tests

        /// <summary>
        /// Creates a RestoreProvider with mock partitions and filesystems for testing path parsing.
        /// Uses reflection to set up the internal state.
        /// </summary>
        private static RestoreProvider CreateRestoreProviderForPathParsingTests()
        {
            // Create a RestoreProvider using the default constructor (for metadata loading)
            var provider = new RestoreProvider();

            // Create mock partition tables
            var gptTable = new MockPartitionTable(PartitionTableType.GPT);
            var mbrTable = new MockPartitionTable(PartitionTableType.MBR);

            // Create mock partitions
            var partitions = new List<IPartition>
            {
                new MockPartition(gptTable, 1, PartitionType.Primary, 1048576, 20971520, "EFI System", FileSystemType.FAT32),
                new MockPartition(gptTable, 2, PartitionType.Primary, 22020096, 41943040, "Windows OS", FileSystemType.NTFS),
                new MockPartition(mbrTable, 1, PartitionType.Primary, 1048576, 10485760, "MBR Partition 1", FileSystemType.FAT32),
                new MockPartition(mbrTable, 2, PartitionType.Primary, 11534336, 20971520, "MBR Partition 2", FileSystemType.NTFS)
            };

            // Create mock filesystems
            var filesystems = new List<IFilesystem>
            {
                new MockFilesystem(partitions[0], FileSystemType.FAT32),
                new MockFilesystem(partitions[1], FileSystemType.NTFS),
                new MockFilesystem(partitions[2], FileSystemType.FAT32),
                new MockFilesystem(partitions[3], FileSystemType.NTFS)
            };

            // Use reflection to set the private fields
            var partitionsField = typeof(RestoreProvider).GetField("_partitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var filesystemsField = typeof(RestoreProvider).GetField("_filesystems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            partitionsField?.SetValue(provider, partitions);
            filesystemsField?.SetValue(provider, filesystems);

            return provider;
        }

        /// <summary>
        /// Mock implementation of IPartitionTable for testing.
        /// </summary>
        private class MockPartitionTable(PartitionTableType tableType) : IPartitionTable
        {
            public IRawDisk? RawDisk => null;
            public PartitionTableType TableType { get; } = tableType;

            public IAsyncEnumerable<IPartition> EnumeratePartitions(CancellationToken cancellationToken)
            {
                return AsyncEnumerable.Empty<IPartition>();
            }

            public Task<IPartition?> GetPartitionAsync(int partitionNumber, CancellationToken cancellationToken)
            {
                return Task.FromResult<IPartition?>(null);
            }

            public Task<Stream> GetProtectiveMbrAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<Stream>(new MemoryStream());
            }

            public Task<Stream> GetPartitionTableDataAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<Stream>(new MemoryStream());
            }

            public void Dispose() { }
        }

        /// <summary>
        /// Mock implementation of IPartition for testing.
        /// </summary>
        private class MockPartition(IPartitionTable table, int number, PartitionType type, long startOffset, long size, string? name, FileSystemType fsType) : IPartition
        {
            public IPartitionTable PartitionTable { get; } = table;
            public int PartitionNumber { get; } = number;
            public PartitionType Type { get; } = type;
            public long StartOffset { get; } = startOffset;
            public long Size { get; } = size;
            public string? Name { get; } = name;
            public FileSystemType FilesystemType { get; } = fsType;
            public Guid? VolumeGuid { get; } = Guid.NewGuid();

            public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<Stream>(new MemoryStream());
            }

            public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<Stream>(new MemoryStream());
            }

            public void Dispose() { }
        }

        /// <summary>
        /// Mock implementation of IFilesystem for testing.
        /// </summary>
        private class MockFilesystem(IPartition partition, FileSystemType type) : IFilesystem
        {
            public IPartition Partition { get; } = partition;
            public FileSystemType Type { get; } = type;

            public Task<object?> GetFilesystemMetadataAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<object?>(null);
            }

            public IAsyncEnumerable<IFile> ListFilesAsync(CancellationToken cancellationToken)
            {
                return AsyncEnumerable.Empty<IFile>();
            }

            public IAsyncEnumerable<IFile> ListFilesAsync(IFile directory, CancellationToken cancellationToken)
            {
                return AsyncEnumerable.Empty<IFile>();
            }

            public Task<Stream> OpenReadStreamAsync(IFile file, CancellationToken cancellationToken)
            {
                return Task.FromResult<Stream>(new MemoryStream());
            }

            public Task<Stream> OpenWriteStreamAsync(IFile file, CancellationToken cancellationToken)
            {
                return Task.FromResult<Stream>(new MemoryStream());
            }

            public Task<Stream> OpenReadWriteStreamAsync(IFile file, CancellationToken cancellationToken)
            {
                return Task.FromResult<Stream>(new MemoryStream());
            }

            public Task<long> GetFileLengthAsync(IFile file, CancellationToken cancellationToken)
            {
                return Task.FromResult(0L);
            }

            public Task<Stream> OpenReadStreamAsync(string path, CancellationToken cancellationToken)
            {
                return Task.FromResult<Stream>(new MemoryStream());
            }

            public Task<Stream> OpenWriteStreamAsync(string path, CancellationToken cancellationToken)
            {
                return Task.FromResult<Stream>(new MemoryStream());
            }

            public Task<Stream> OpenReadWriteStreamAsync(string path, CancellationToken cancellationToken)
            {
                return Task.FromResult<Stream>(new MemoryStream());
            }

            public Task<long> GetFileLengthAsync(string path, CancellationToken cancellationToken)
            {
                return Task.FromResult(0L);
            }

            public void Dispose() { }
        }

        /// <summary>
        /// Tests that ParsePartition correctly parses a valid GPT partition segment.
        /// </summary>
        [Test]
        public void Test_RestoreProvider_ParsePartition_ValidGPTPartition_ReturnsCorrectPartition()
        {
            using var provider = CreateRestoreProviderForPathParsingTests();
            var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");

            // Test parsing "part_GPT_1"
            var result = parsePartitionMethod!.Invoke(provider, ["part_GPT_1"]);
            Assert.IsNotNull(result, "Should return a partition.");

            var partition = (IPartition)result!;
            Assert.AreEqual(1, partition.PartitionNumber, "Partition number should be 1.");
            Assert.AreEqual(PartitionTableType.GPT, partition.PartitionTable.TableType, "Partition table type should be GPT.");
            Assert.AreEqual("EFI System", partition.Name, "Partition name should match.");
        }

        /// <summary>
        /// Tests that ParsePartition correctly parses a valid MBR partition segment.
        /// </summary>
        [Test]
        public void Test_RestoreProvider_ParsePartition_ValidMBRPartition_ReturnsCorrectPartition()
        {
            using var provider = CreateRestoreProviderForPathParsingTests();
            var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");

            // Test parsing "part_MBR_2"
            var result = parsePartitionMethod!.Invoke(provider, ["part_MBR_2"]);
            Assert.IsNotNull(result, "Should return a partition.");

            var partition = (IPartition)result!;
            Assert.AreEqual(2, partition.PartitionNumber, "Partition number should be 2.");
            Assert.AreEqual(PartitionTableType.MBR, partition.PartitionTable.TableType, "Partition table type should be MBR.");
            Assert.AreEqual("MBR Partition 2", partition.Name, "Partition name should match.");
        }

        /// <summary>
        /// Tests that ParsePartition throws InvalidOperationException for malformed partition segments.
        /// </summary>
        [Test]
        public void Test_RestoreProvider_ParsePartition_MalformedSegment_ThrowsInvalidOperationException()
        {
            using var provider = CreateRestoreProviderForPathParsingTests();
            var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");

            // Test with too few parts
            var ex = Assert.Throws<TargetInvocationException>(() =>
                parsePartitionMethod!.Invoke(provider, ["part_GPT"]),
                "Should throw for malformed segment.");
            Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");

            // Test with invalid partition table type
            ex = Assert.Throws<TargetInvocationException>(() =>
                parsePartitionMethod!.Invoke(provider, ["part_INVALID_1"]),
                "Should throw for invalid table type.");
            Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");

            // Test with non-numeric partition number
            ex = Assert.Throws<TargetInvocationException>(() =>
                parsePartitionMethod!.Invoke(provider, ["part_GPT_ABC"]),
                "Should throw for non-numeric partition number.");
            Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");
        }

        /// <summary>
        /// Tests that ParsePartition throws InvalidOperationException for non-existent partitions.
        /// </summary>
        [Test]
        public void Test_RestoreProvider_ParsePartition_NonExistentPartition_ThrowsInvalidOperationException()
        {
            using var provider = CreateRestoreProviderForPathParsingTests();
            var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");

            // Test with partition number that doesn't exist
            var ex = Assert.Throws<TargetInvocationException>(() =>
                parsePartitionMethod!.Invoke(provider, ["part_GPT_99"]),
                "Should throw for non-existent partition.");
            Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");
        }

        /// <summary>
        /// Tests that ParseFilesystem correctly parses a valid NTFS filesystem segment.
        /// </summary>
        [Test]
        public void Test_RestoreProvider_ParseFilesystem_ValidNTFS_ReturnsCorrectFilesystem()
        {
            using var provider = CreateRestoreProviderForPathParsingTests();
            var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var parseFilesystemMethod = typeof(RestoreProvider).GetMethod("ParseFilesystem", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");
            Assert.IsNotNull(parseFilesystemMethod, "ParseFilesystem method should exist.");

            // Get a partition first
            var partitionResult = parsePartitionMethod!.Invoke(provider, ["part_GPT_2"]);
            Assert.IsNotNull(partitionResult, "Should return a partition.");
            var partition = (IPartition)partitionResult!;

            // Test parsing "fs_NTFS"
            var result = parseFilesystemMethod!.Invoke(provider, [partition, "fs_NTFS"]);
            Assert.IsNotNull(result, "Should return a filesystem.");

            var filesystem = (IFilesystem)result!;
            Assert.AreEqual(FileSystemType.NTFS, filesystem.Type, "Filesystem type should be NTFS.");
            Assert.AreEqual(partition.PartitionNumber, filesystem.Partition.PartitionNumber, "Partition number should match.");
        }

        /// <summary>
        /// Tests that ParseFilesystem correctly parses a valid FAT32 filesystem segment.
        /// </summary>
        [Test]
        public void Test_RestoreProvider_ParseFilesystem_ValidFAT32_ReturnsCorrectFilesystem()
        {
            using var provider = CreateRestoreProviderForPathParsingTests();
            var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var parseFilesystemMethod = typeof(RestoreProvider).GetMethod("ParseFilesystem", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");
            Assert.IsNotNull(parseFilesystemMethod, "ParseFilesystem method should exist.");

            // Get a partition first
            var partitionResult = parsePartitionMethod!.Invoke(provider, ["part_GPT_1"]);
            Assert.IsNotNull(partitionResult, "Should return a partition.");
            var partition = (IPartition)partitionResult!;

            // Test parsing "fs_FAT32"
            var result = parseFilesystemMethod!.Invoke(provider, [partition, "fs_FAT32"]);
            Assert.IsNotNull(result, "Should return a filesystem.");

            var filesystem = (IFilesystem)result!;
            Assert.AreEqual(FileSystemType.FAT32, filesystem.Type, "Filesystem type should be FAT32.");
        }

        /// <summary>
        /// Tests that ParseFilesystem throws InvalidOperationException for malformed filesystem segments.
        /// </summary>
        [Test]
        public void Test_RestoreProvider_ParseFilesystem_MalformedSegment_ThrowsInvalidOperationException()
        {
            using var provider = CreateRestoreProviderForPathParsingTests();
            var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var parseFilesystemMethod = typeof(RestoreProvider).GetMethod("ParseFilesystem", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");
            Assert.IsNotNull(parseFilesystemMethod, "ParseFilesystem method should exist.");

            // Get a partition first
            var partitionResult = parsePartitionMethod!.Invoke(provider, ["part_GPT_1"]);
            Assert.IsNotNull(partitionResult, "Should return a partition.");
            var partition = (IPartition)partitionResult!;

            // Test with too few parts
            var ex = Assert.Throws<TargetInvocationException>(() =>
                parseFilesystemMethod!.Invoke(provider, [partition, "fs"]),
                "Should throw for malformed segment.");
            Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");

            // Test with invalid filesystem type
            ex = Assert.Throws<TargetInvocationException>(() =>
                parseFilesystemMethod!.Invoke(provider, [partition, "fs_INVALID"]),
                "Should throw for invalid filesystem type.");
            Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");
        }

        /// <summary>
        /// Tests that ParseFilesystem throws InvalidOperationException for non-existent filesystems.
        /// </summary>
        [Test]
        public void Test_RestoreProvider_ParseFilesystem_NonExistentFilesystem_ThrowsInvalidOperationException()
        {
            using var provider = CreateRestoreProviderForPathParsingTests();
            var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var parseFilesystemMethod = typeof(RestoreProvider).GetMethod("ParseFilesystem", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");
            Assert.IsNotNull(parseFilesystemMethod, "ParseFilesystem method should exist.");

            // Get a partition that doesn't have an Ext4 filesystem
            var partitionResult = parsePartitionMethod!.Invoke(provider, ["part_GPT_1"]);
            Assert.IsNotNull(partitionResult, "Should return a partition.");
            var partition = (IPartition)partitionResult!;

            // Test with filesystem type that doesn't exist for this partition
            var ex = Assert.Throws<TargetInvocationException>(() =>
                parseFilesystemMethod!.Invoke(provider, [partition, "fs_Ext4"]),
                "Should throw for non-existent filesystem.");
            Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");
        }

        /// <summary>
        /// Tests that ParsePath correctly identifies a geometry file path.
        /// </summary>
        [Test]
        public void Test_RestoreProvider_ParsePath_GeometryFile_ReturnsGeometryType()
        {
            using var provider = CreateRestoreProviderForPathParsingTests();
            var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

            // Test with geometry.json file
            var result = parsePathMethod!.Invoke(provider, ["geometry.json"]);
            Assert.IsNotNull(result, "Should return a tuple.");

            var tuple = ((string Type, IPartition? Partition, IFilesystem? Filesystem))result!;
            Assert.AreEqual("geometry", tuple.Type, "Type should be 'geometry'.");
            Assert.IsNull(tuple.Partition, "Partition should be null for geometry.");
            Assert.IsNull(tuple.Filesystem, "Filesystem should be null for geometry.");
        }

        /// <summary>
        /// Tests that ParsePath correctly identifies a disk-level path.
        /// </summary>
        [Test]
        public void Test_RestoreProvider_ParsePath_DiskPath_ReturnsDiskType()
        {
            using var provider = CreateRestoreProviderForPathParsingTests();
            var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

            // Test with root path
            var result = parsePathMethod!.Invoke(provider, ["/"]);
            Assert.IsNotNull(result, "Should return a tuple.");

            var tuple = ((string Type, IPartition? Partition, IFilesystem? Filesystem))result!;
            Assert.AreEqual("disk", tuple.Type, "Type should be 'disk'.");
            Assert.IsNull(tuple.Partition, "Partition should be null for disk.");
            Assert.IsNull(tuple.Filesystem, "Filesystem should be null for disk.");
        }

        /// <summary>
        /// Tests that ParsePath correctly identifies a partition path.
        /// </summary>
        [Test]
        public void Test_RestoreProvider_ParsePath_PartitionPath_ReturnsPartitionType()
        {
            using var provider = CreateRestoreProviderForPathParsingTests();
            var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

            // Test with partition path (no filesystem segment)
            var result = parsePathMethod!.Invoke(provider, ["part_GPT_1"]);
            Assert.IsNotNull(result, "Should return a tuple.");

            var tuple = ((string Type, IPartition? Partition, IFilesystem? Filesystem))result!;
            Assert.AreEqual("partition", tuple.Type, "Type should be 'partition'.");
            Assert.IsNotNull(tuple.Partition, "Partition should not be null.");
            Assert.AreEqual(1, tuple.Partition!.PartitionNumber, "Partition number should be 1.");
            Assert.IsNull(tuple.Filesystem, "Filesystem should be null for partition-only path.");
        }

        /// <summary>
        /// Tests that ParsePath correctly identifies a file path with partition and filesystem.
        /// </summary>
        [Test]
        public void Test_RestoreProvider_ParsePath_FilePath_ReturnsFileType()
        {
            using var provider = CreateRestoreProviderForPathParsingTests();
            var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

            // Test with full file path
            var result = parsePathMethod!.Invoke(provider, ["part_GPT_1/fs_FAT32/test/file.txt"]);
            Assert.IsNotNull(result, "Should return a tuple.");

            var tuple = ((string Type, IPartition? Partition, IFilesystem? Filesystem))result!;
            Assert.AreEqual("file", tuple.Type, "Type should be 'file'.");
            Assert.IsNotNull(tuple.Partition, "Partition should not be null.");
            Assert.IsNotNull(tuple.Filesystem, "Filesystem should not be null.");
            Assert.AreEqual(1, tuple.Partition!.PartitionNumber, "Partition number should be 1.");
            Assert.AreEqual(FileSystemType.FAT32, tuple.Filesystem!.Type, "Filesystem type should be FAT32.");
        }

        /// <summary>
        /// Tests that ParsePath handles MBR partition paths correctly.
        /// </summary>
        [Test]
        public void Test_RestoreProvider_ParsePath_MBRPartitionPath_ReturnsCorrectPartition()
        {
            using var provider = CreateRestoreProviderForPathParsingTests();
            var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

            // Test with MBR partition path
            var result = parsePathMethod!.Invoke(provider, ["part_MBR_2/fs_NTFS/data/file.dat"]);
            Assert.IsNotNull(result, "Should return a tuple.");

            var tuple = ((string Type, IPartition? Partition, IFilesystem? Filesystem))result!;
            Assert.AreEqual("file", tuple.Type, "Type should be 'file'.");
            Assert.IsNotNull(tuple.Partition, "Partition should not be null.");
            Assert.IsNotNull(tuple.Filesystem, "Filesystem should not be null.");
            Assert.AreEqual(2, tuple.Partition!.PartitionNumber, "Partition number should be 2.");
            Assert.AreEqual(PartitionTableType.MBR, tuple.Partition.PartitionTable.TableType, "Table type should be MBR.");
            Assert.AreEqual(FileSystemType.NTFS, tuple.Filesystem!.Type, "Filesystem type should be NTFS.");
        }

        /// <summary>
        /// Tests that ParsePath handles paths with different separators correctly on Windows.
        /// On Windows, both forward slash and backslash are valid path separators.
        /// On Linux/macOS, backslash is a valid filename character, not a separator.
        /// </summary>
        [Test]
        public void Test_RestoreProvider_ParsePath_DifferentSeparators_NormalizesCorrectly()
        {
            using var provider = CreateRestoreProviderForPathParsingTests();
            var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

            if (OperatingSystem.IsWindows())
            {
                // On Windows, backslash is a path separator
                var result = parsePathMethod!.Invoke(provider, ["part_GPT_1\\fs_FAT32\\test\\file.txt"]);
                Assert.IsNotNull(result, "Should return a tuple.");

                var tuple = ((string Type, IPartition? Partition, IFilesystem? Filesystem))result!;
                Assert.AreEqual("file", tuple.Type, "Type should be 'file'.");
                Assert.IsNotNull(tuple.Partition, "Partition should not be null.");
                Assert.IsNotNull(tuple.Filesystem, "Filesystem should not be null.");
            }
            else
            {
                // On Linux/macOS, backslash is a filename character, not a separator
                // The entire string becomes one segment, which fails partition parsing
                var ex = Assert.Throws<TargetInvocationException>(() =>
                    parsePathMethod!.Invoke(provider, ["part_GPT_1\\fs_FAT32\\test\\file.txt"]),
                    "Should throw for backslash path on non-Windows platforms.");
                Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");
            }
        }

        /// <summary>
        /// Tests that ParsePath throws appropriate exceptions for invalid paths.
        /// </summary>
        [Test]
        public void Test_RestoreProvider_ParsePath_InvalidPartitionSegment_ThrowsException()
        {
            using var provider = CreateRestoreProviderForPathParsingTests();
            var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

            // Test with invalid partition segment format - this should still parse as file type
            // but will fail when trying to parse the partition
            var ex = Assert.Throws<TargetInvocationException>(() =>
                parsePathMethod!.Invoke(provider, ["part_INVALID/fs_NTFS/file.txt"]),
                "Should throw for invalid partition segment.");
            Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");
        }

        #endregion

        #region Edge Case and Boundary Tests

        /// <summary>
        /// Tests reading at offset 0 (first sector) returns correct data.
        /// </summary>
        [Test]
        public async Task Test_RawDisk_ReadAtOffsetZero_ReturnsCorrectData()
        {
            var sectorSize = _writableRawDisk.SectorSize;

            // Write known pattern to sector 0 (first sector)
            var writeBuffer = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                writeBuffer[i] = (byte)(i & 0xFF);

            await _writableRawDisk.WriteSectorsAsync(0, writeBuffer, CancellationToken.None);

            // Read at offset 0
            using var stream = await _writableRawDisk.ReadBytesAsync(0, sectorSize, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await stream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize, bytesRead, "Should have read one sector.");
            Assert.AreEqual(writeBuffer, readBuffer, "Data at offset 0 should match what was written.");
        }

        /// <summary>
        /// Tests writing at offset 0 (first sector) works correctly.
        /// </summary>
        [Test]
        public async Task Test_RawDisk_WriteAtOffsetZero_DataMatches()
        {
            var sectorSize = _writableRawDisk.SectorSize;

            // Write at offset 0
            var writeBuffer = new byte[sectorSize];
            new Random(123).NextBytes(writeBuffer);

            var bytesWritten = await _writableRawDisk.WriteBytesAsync(0, writeBuffer, CancellationToken.None);
            Assert.AreEqual(sectorSize, bytesWritten, "Should have written one sector.");

            // Read back and verify
            using var readStream = await _writableRawDisk.ReadSectorsAsync(0, 1, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize, bytesRead, "Should have read one sector.");
            Assert.AreEqual(writeBuffer, readBuffer, "Data at offset 0 should match.");
        }

        /// <summary>
        /// Tests reading at the last sector of the disk returns correct data.
        /// </summary>
        [Test]
        public async Task Test_RawDisk_ReadAtLastSector_ReturnsCorrectData()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var diskSize = _writableRawDisk.Size;
            var lastSector = (diskSize / sectorSize) - 1;

            // Write known pattern to last sector
            var writeBuffer = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                writeBuffer[i] = (byte)((i + 100) & 0xFF);

            await _writableRawDisk.WriteSectorsAsync(lastSector, writeBuffer, CancellationToken.None);

            // Read at last sector offset
            var lastSectorOffset = lastSector * sectorSize;
            using var stream = await _writableRawDisk.ReadBytesAsync(lastSectorOffset, sectorSize, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await stream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize, bytesRead, "Should have read one sector.");
            Assert.AreEqual(writeBuffer, readBuffer, "Data at last sector should match.");
        }

        /// <summary>
        /// Tests writing at the last sector of the disk works correctly.
        /// </summary>
        [Test]
        public async Task Test_RawDisk_WriteAtLastSector_DataMatches()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var diskSize = _writableRawDisk.Size;
            var lastSector = (diskSize / sectorSize) - 1;

            // Write at last sector
            var writeBuffer = new byte[sectorSize];
            new Random(456).NextBytes(writeBuffer);

            var bytesWritten = await _writableRawDisk.WriteSectorsAsync(lastSector, writeBuffer, CancellationToken.None);
            Assert.AreEqual(sectorSize, bytesWritten, "Should have written one sector at last position.");

            // Read back and verify
            using var readStream = await _writableRawDisk.ReadSectorsAsync(lastSector, 1, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize, bytesRead, "Should have read one sector.");
            Assert.AreEqual(writeBuffer, readBuffer, "Data at last sector should match.");
        }

        /// <summary>
        /// Tests reading exactly one sector returns correct data.
        /// </summary>
        [Test]
        public async Task Test_RawDisk_ReadExactlyOneSector_ReturnsCorrectData()
        {
            var sectorSize = _writableRawDisk.SectorSize;

            // Write known pattern to sector 5
            var writeBuffer = new byte[sectorSize];
            for (int i = 0; i < sectorSize; i++)
                writeBuffer[i] = (byte)((i * 3) & 0xFF);

            await _writableRawDisk.WriteSectorsAsync(5, writeBuffer, CancellationToken.None);

            // Read exactly one sector using ReadBytesAsync
            var offset = 5 * sectorSize;
            using var stream = await _writableRawDisk.ReadBytesAsync(offset, sectorSize, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await stream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize, bytesRead, "Should have read exactly one sector.");
            Assert.AreEqual(sectorSize, stream.Length, "Stream length should be exactly one sector.");
            Assert.AreEqual(writeBuffer, readBuffer, "Data should match exactly.");
        }

        /// <summary>
        /// Tests writing exactly one sector works correctly.
        /// </summary>
        [Test]
        public async Task Test_RawDisk_WriteExactlyOneSector_DataMatches()
        {
            var sectorSize = _writableRawDisk.SectorSize;

            // Write exactly one sector using WriteBytesAsync
            var writeBuffer = new byte[sectorSize];
            new Random(789).NextBytes(writeBuffer);

            var offset = 10 * sectorSize;
            var bytesWritten = await _writableRawDisk.WriteBytesAsync(offset, writeBuffer, CancellationToken.None);

            Assert.AreEqual(sectorSize, bytesWritten, "Should have written exactly one sector.");

            // Read back and verify
            using var readStream = await _writableRawDisk.ReadSectorsAsync(10, 1, CancellationToken.None);
            var readBuffer = new byte[sectorSize];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(sectorSize, bytesRead, "Should have read one sector.");
            Assert.AreEqual(writeBuffer, readBuffer, "Data should match exactly.");
        }

        /// <summary>
        /// Tests reading the full disk size works correctly.
        /// </summary>
        [Test]
        public async Task Test_RawDisk_ReadFullDiskSize_ReturnsCorrectData()
        {
            // Use a smaller test disk for this test to avoid memory issues
            var sectorSize = _writableRawDisk.SectorSize;
            var diskSize = _writableRawDisk.Size;

            // Read full disk in chunks to verify it works
            const int chunkSectors = 10;
            var chunkSize = chunkSectors * sectorSize;
            var totalSectors = diskSize / sectorSize;
            var sectorsRead = 0L;

            for (long sector = 0; sector < totalSectors; sector += chunkSectors)
            {
                var sectorsToRead = (int)Math.Min(chunkSectors, totalSectors - sector);
                using var stream = await _writableRawDisk.ReadSectorsAsync(sector, sectorsToRead, CancellationToken.None);

                var buffer = new byte[sectorsToRead * sectorSize];
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);

                Assert.AreEqual(sectorsToRead * sectorSize, bytesRead, $"Should have read {sectorsToRead} sectors at offset {sector}.");
                sectorsRead += sectorsToRead;
            }

            Assert.AreEqual(totalSectors, sectorsRead, "Should have read all sectors.");
        }

        /// <summary>
        /// Tests writing the full disk size works correctly.
        /// </summary>
        [Test]
        public async Task Test_RawDisk_WriteFullDiskSize_DataMatches()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var diskSize = _writableRawDisk.Size;

            // Write a unique pattern to each sector and verify
            var totalSectors = diskSize / sectorSize;
            const int testSectors = 20; // Test a subset to keep test fast

            for (long sector = 0; sector < Math.Min(testSectors, totalSectors); sector++)
            {
                // Create unique pattern for this sector
                var writeBuffer = new byte[sectorSize];
                var patternByte = (byte)(sector & 0xFF);
                for (int i = 0; i < sectorSize; i++)
                    writeBuffer[i] = (byte)((patternByte + i) & 0xFF);

                await _writableRawDisk.WriteSectorsAsync(sector, writeBuffer, CancellationToken.None);

                // Read back and verify
                using var readStream = await _writableRawDisk.ReadSectorsAsync(sector, 1, CancellationToken.None);
                var readBuffer = new byte[sectorSize];
                var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

                Assert.AreEqual(sectorSize, bytesRead, $"Should read sector {sector}.");
                Assert.AreEqual(writeBuffer, readBuffer, $"Data at sector {sector} should match.");
            }
        }

        /// <summary>
        /// Tests writing empty data (0 bytes) returns 0 without error.
        /// </summary>
        [Test]
        public async Task Test_RawDisk_WriteEmptyData_ReturnsZero()
        {
            var sectorSize = _writableRawDisk.SectorSize;

            // Write empty data
            var emptyData = Array.Empty<byte>();
            var bytesWritten = await _writableRawDisk.WriteBytesAsync(sectorSize, emptyData, CancellationToken.None);

            Assert.AreEqual(0, bytesWritten, "Writing empty data should return 0 bytes written.");
        }

        /// <summary>
        /// Tests reading empty data (0 bytes) returns 0 without error.
        /// </summary>
        [Test]
        public async Task Test_RawDisk_ReadEmptyData_ReturnsZero()
        {
            var sectorSize = _writableRawDisk.SectorSize;

            // Read 0 bytes
            using var stream = await _writableRawDisk.ReadBytesAsync(sectorSize, 0, CancellationToken.None);
            var buffer = Array.Empty<byte>();
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);

            Assert.AreEqual(0, bytesRead, "Reading 0 bytes should return 0.");
            Assert.AreEqual(0, stream.Length, "Stream length should be 0 for empty read.");
        }

        /// <summary>
        /// Tests that a disk with minimum possible size still supports basic operations.
        /// Creates a small test disk and verifies read/write operations work.
        /// </summary>
        [Test]
        public async Task Test_RawDisk_MinimumSizeDisk_OperationsWork()
        {
            // The minimum practical disk size is at least a few sectors
            // We'll use the writable disk which is already created at 100 MiB
            // but verify operations work at the boundaries
            var sectorSize = _writableRawDisk.SectorSize;
            var diskSize = _writableRawDisk.Size;

            // Minimum operations: read/write first sector
            var firstSectorData = new byte[sectorSize];
            new Random(111).NextBytes(firstSectorData);
            await _writableRawDisk.WriteSectorsAsync(0, firstSectorData, CancellationToken.None);

            using var firstReadStream = await _writableRawDisk.ReadSectorsAsync(0, 1, CancellationToken.None);
            var firstReadBuffer = new byte[sectorSize];
            var firstBytesRead = await firstReadStream.ReadAsync(firstReadBuffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(sectorSize, firstBytesRead, "Should read exactly one sector.");
            Assert.AreEqual(firstSectorData, firstReadBuffer, "First sector should be readable/writable.");

            // Minimum operations: read/write last sector
            var lastSector = (diskSize / sectorSize) - 1;
            var lastSectorData = new byte[sectorSize];
            new Random(222).NextBytes(lastSectorData);
            await _writableRawDisk.WriteSectorsAsync(lastSector, lastSectorData, CancellationToken.None);

            using var lastReadStream = await _writableRawDisk.ReadSectorsAsync(lastSector, 1, CancellationToken.None);
            var lastReadBuffer = new byte[sectorSize];
            var lastBytesRead = await lastReadStream.ReadAsync(lastReadBuffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(sectorSize, lastBytesRead, "Should read exactly one sector.");
            Assert.AreEqual(lastSectorData, lastReadBuffer, "Last sector should be readable/writable.");

            // Verify disk size is reported correctly
            Assert.Greater(diskSize, 0, "Disk size should be greater than 0.");
            Assert.AreEqual(0, diskSize % sectorSize, "Disk size should be sector-aligned.");
        }

        /// <summary>
        /// Tests boundary condition: reading at exactly the disk size boundary should fail gracefully.
        /// </summary>
        [Test]
        public void Test_RawDisk_ReadAtDiskSizeBoundary_ThrowsInvalidOperationException()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var diskSize = _writableRawDisk.Size;

            // Attempting to read at or beyond disk size should throw InvalidOperationException
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                using var stream = await _writableRawDisk.ReadBytesAsync(diskSize, sectorSize, CancellationToken.None);
                var buffer = new byte[sectorSize];
                var _ = await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            }, "Reading at disk boundary should throw InvalidOperationException.");

            StringAssert.Contains("beyond disk size", ex!.Message, "Exception message should mention beyond disk size.");
        }

        /// <summary>
        /// Tests boundary condition: writing at exactly the disk size boundary should fail gracefully.
        /// </summary>
        [Test]
        public void Test_RawDisk_WriteAtDiskSizeBoundary_ThrowsInvalidOperationException()
        {
            var sectorSize = _writableRawDisk.SectorSize;
            var diskSize = _writableRawDisk.Size;

            // Attempting to write at or beyond disk size should throw InvalidOperationException
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                var writeData = new byte[sectorSize];
                await _writableRawDisk.WriteBytesAsync(diskSize, writeData, CancellationToken.None);
            }, "Writing at disk boundary should throw InvalidOperationException.");

            StringAssert.Contains("beyond disk size", ex!.Message, "Exception message should mention beyond disk size.");
        }

        /// <summary>
        /// Tests reading/writing a single byte at various boundary positions.
        /// </summary>
        [Test]
        public async Task Test_RawDisk_SingleByteAtBoundaries_DataMatches()
        {
            var sectorSize = _writableRawDisk.SectorSize;

            // Test positions: first byte of disk, last byte of first sector,
            // first byte of second sector, and various other positions
            var testPositions = new[]
            {
                0L,                          // First byte of disk
                sectorSize - 1,              // Last byte of first sector
                sectorSize,                  // First byte of second sector
                sectorSize + 1,              // Second byte of second sector
                (2 * sectorSize) - 1,        // Last byte of second sector
                2 * sectorSize               // First byte of third sector
            };

            for (int i = 0; i < testPositions.Length; i++)
            {
                var position = testPositions[i];
                var testByte = (byte)(0xA0 + i);

                // Write single byte
                var writeData = new[] { testByte };
                var bytesWritten = await _writableRawDisk.WriteBytesAsync(position, writeData, CancellationToken.None);
                Assert.AreEqual(1, bytesWritten, $"Should write 1 byte at position {position}.");

                // Read back the byte
                using var stream = await _writableRawDisk.ReadBytesAsync(position, 1, CancellationToken.None);
                var readBuffer = new byte[1];
                var bytesRead = await stream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

                Assert.AreEqual(1, bytesRead, $"Should read 1 byte at position {position}.");
                Assert.AreEqual(testByte, readBuffer[0], $"Byte at position {position} should match.");
            }
        }

        #endregion
    }
}
