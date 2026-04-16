using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

public partial class DiskImageUnitTests : BasicSetupHelper
{
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

}