using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

public partial class DiskImageUnitTests : BasicSetupHelper
{

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

}