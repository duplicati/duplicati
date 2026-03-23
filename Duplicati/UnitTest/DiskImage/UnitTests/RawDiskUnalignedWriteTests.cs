using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

public partial class DiskImageUnitTests : BasicSetupHelper
{

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

}