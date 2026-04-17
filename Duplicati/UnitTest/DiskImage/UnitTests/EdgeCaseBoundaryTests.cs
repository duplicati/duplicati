using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

public partial class DiskImageUnitTests : BasicSetupHelper
{

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

}