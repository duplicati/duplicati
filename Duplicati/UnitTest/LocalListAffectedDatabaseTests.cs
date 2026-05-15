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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Unit tests for <see cref="LocalListAffectedDatabase"/> methods that use
    /// <see cref="TemporaryDbValueList"/> for handling large IN clause parameters.
    /// </summary>
    [TestFixture]
    public class LocalListAffectedDatabaseTests
    {
        /// <summary>
        /// Tests that <see cref="LocalListAffectedDatabase.GetLogLinesAsync"/> works correctly
        /// with a large number of items that triggers the temporary table code path
        /// (when count > CHUNK_SIZE = 128).
        /// </summary>
        [Test]
        [Category("Database")]
        public async Task GetLogLines_WithLargeItemCount_UsesTemporaryTable_Async()
        {
            // Arrange
            using var tempFile = new TempFile();
            await using var db = await LocalListAffectedDatabase.CreateAsync(tempFile, null, CancellationToken.None)
                .ConfigureAwait(false);

            // Create 150 remote operation entries (exceeds CHUNK_SIZE of 128)
            // These will be used as the "items" parameter to GetLogLines
            var remotePaths = new List<string>();
            using (var cmd = db.Connection.CreateCommand())
            {
                // Insert operation entry first
                await cmd.SetCommandAndParameters(@"
                    INSERT OR IGNORE INTO Operation (ID, Description, Timestamp)
                    VALUES (1, 'TestOperation', 0);")
                    .ExecuteNonQueryAsync();

                // Insert 150 remote operation entries
                for (int i = 0; i < 150; i++)
                {
                    var path = $"/remote/path/file{i:D4}.zip";
                    remotePaths.Add(path);

                    await cmd.SetCommandAndParameters(@"
                        INSERT INTO RemoteOperation (OperationID, Timestamp, Operation, Path, Data)
                        VALUES (@operationId, @timestamp, @operation, @path, @data);")
                        .SetParameterValue("@operationId", 1L)
                        .SetParameterValue("@timestamp", i)
                        .SetParameterValue("@operation", "put")
                        .SetParameterValue("@path", path)
                        .SetParameterValue("@data", $"Operation data for {path}")
                        .ExecuteNonQueryAsync();
                }

                // Insert some log data that references these paths
                // Note: GetLogLines chunks by CHUNK_SIZE/2 (64), so we insert enough
                // log entries to match the first chunk
                for (int i = 0; i < 150; i++)
                {
                    await cmd.SetCommandAndParameters(@"
                        INSERT INTO LogData (OperationID, Timestamp, Type, Message, Exception)
                        VALUES (@operationId, @timestamp, @type, @message, @exception);")
                        .SetParameterValue("@operationId", 1L)
                        .SetParameterValue("@timestamp", i)
                        .SetParameterValue("@type", "Information")
                        .SetParameterValue("@message", $"Log message referencing {remotePaths[i]}")
                        .SetParameterValue("@exception", (string?)null)
                        .ExecuteNonQueryAsync();
                }
            }

            // Act: Call GetLogLines with 150 items
            // This should trigger the temporary table code path
            var results = await db.GetLogLinesAsync(remotePaths, CancellationToken.None)
                .ToListAsync()
                .ConfigureAwait(false);

            // Assert: Should return log entries
            Assert.That(results.Count, Is.GreaterThan(0), "Expected at least some log entries to be returned");
        }

        /// <summary>
        /// Tests that <see cref="LocalListAffectedDatabase.GetVolumesAsync"/> works correctly
        /// with a large number of items that triggers the temporary table code path
        /// (when count > CHUNK_SIZE = 128).
        /// </summary>
        [Test]
        [Category("Database")]
        public async Task GetVolumes_WithLargeItemCount_UsesTemporaryTable_Async()
        {
            // Arrange
            using var tempFile = new TempFile();
            await using var db = await LocalListAffectedDatabase.CreateAsync(tempFile, null, CancellationToken.None)
                .ConfigureAwait(false);

            // Create 150 volume names (exceeds CHUNK_SIZE of 128)
            var volumeNames = new List<string>();
            for (int i = 0; i < 150; i++)
            {
                volumeNames.Add($"duplicati-full-content.{i:D4}.zip");
            }

            using (var cmd = db.Connection.CreateCommand())
            {
                // Insert operation entry
                await cmd.SetCommandAndParameters(@"
                    INSERT OR IGNORE INTO Operation (ID, Description, Timestamp)
                    VALUES (1, 'TestOperation', 0);")
                    .ExecuteNonQueryAsync();

                // Insert 150 remote volumes
                for (int i = 0; i < 150; i++)
                {
                    await cmd.SetCommandAndParameters(@"
                        INSERT INTO RemoteVolume (ID, OperationID, Name, Type, State, Size, VerificationCount, DeleteGraceTime, ArchiveTime, LockExpirationTime)
                        VALUES (@id, @operationId, @name, @type, @state, @size, @verificationCount, @deleteGraceTime, @archiveTime, @lockExpirationTime);")
                        .SetParameterValue("@id", i + 1)
                        .SetParameterValue("@operationId", 1L)
                        .SetParameterValue("@name", volumeNames[i])
                        .SetParameterValue("@type", "Files")
                        .SetParameterValue("@state", "Verified")
                        .SetParameterValue("@size", 1024L * 1024L)
                        .SetParameterValue("@verificationCount", 0)
                        .SetParameterValue("@deleteGraceTime", 0)
                        .SetParameterValue("@archiveTime", 0)
                        .SetParameterValue("@lockExpirationTime", 0)
                        .ExecuteNonQueryAsync();
                }

                // Insert fileset entries that reference these volumes
                for (int i = 0; i < 150; i++)
                {
                    await cmd.SetCommandAndParameters(@"
                        INSERT INTO Fileset (ID, OperationID, VolumeID, IsFullBackup, Timestamp)
                        VALUES (@id, @operationId, @volumeId, @isFullBackup, @timestamp);")
                        .SetParameterValue("@id", i + 1)
                        .SetParameterValue("@operationId", 1L)
                        .SetParameterValue("@volumeId", i + 1)
                        .SetParameterValue("@isFullBackup", 1)
                        .SetParameterValue("@timestamp", i)
                        .ExecuteNonQueryAsync();
                }

                // Insert Block entries that reference these volumes
                for (int i = 0; i < 150; i++)
                {
                    await cmd.SetCommandAndParameters(@"
                        INSERT INTO Block (ID, VolumeID, Hash, Size)
                        VALUES (@id, @volumeId, @hash, @size);")
                        .SetParameterValue("@id", i + 1)
                        .SetParameterValue("@volumeId", i + 1)
                        .SetParameterValue("@hash", $"hash{i}")
                        .SetParameterValue("@size", 1024)
                        .ExecuteNonQueryAsync();
                }

                // Insert Blockset entries
                await cmd.SetCommandAndParameters(@"
                    INSERT INTO Blockset (ID, Length, FullHash)
                    VALUES (1, 1024, 'fullhash');")
                    .ExecuteNonQueryAsync();

                // Insert BlocksetEntry entries that link blocks to blocksets
                for (int i = 0; i < 150; i++)
                {
                    await cmd.SetCommandAndParameters(@"
                        INSERT INTO BlocksetEntry (BlocksetID, BlockID, ""Index"")
                        VALUES (@blocksetId, @blockId, @idx);")
                        .SetParameterValue("@blocksetId", 1)
                        .SetParameterValue("@blockId", i + 1)
                        .SetParameterValue("@idx", i)
                        .ExecuteNonQueryAsync();
                }

                // Insert Metadataset entries
                await cmd.SetCommandAndParameters(@"
                    INSERT INTO Metadataset (ID, BlocksetID)
                    VALUES (1, 1);")
                    .ExecuteNonQueryAsync();

                // Insert PathPrefix entries
                await cmd.SetCommandAndParameters(@"
                    INSERT INTO PathPrefix (ID, Prefix)
                    VALUES (1, '/test/');")
                    .ExecuteNonQueryAsync();

                // Insert FileLookup entries
                for (int i = 0; i < 150; i++)
                {
                    await cmd.SetCommandAndParameters(@"
                        INSERT INTO FileLookup (ID, PrefixID, Path, BlocksetID, MetadataID)
                        VALUES (@id, @prefixId, @path, @blocksetId, @metadataId);")
                        .SetParameterValue("@id", i + 1)
                        .SetParameterValue("@prefixId", 1)
                        .SetParameterValue("@path", $"file{i}.txt")
                        .SetParameterValue("@blocksetId", 1)
                        .SetParameterValue("@metadataId", 1)
                        .ExecuteNonQueryAsync();
                }

                // Insert FilesetEntry entries
                for (int i = 0; i < 150; i++)
                {
                    await cmd.SetCommandAndParameters(@"
                        INSERT INTO FilesetEntry (FilesetID, FileID, Lastmodified)
                        VALUES (@filesetId, @fileId, @lastModified);")
                        .SetParameterValue("@filesetId", i + 1)
                        .SetParameterValue("@fileId", i + 1)
                        .SetParameterValue("@lastModified", 0)
                        .ExecuteNonQueryAsync();
                }
            }

            // Act: Call GetVolumes with 150 items
            // This should trigger the temporary table code path
            var results = await db.GetVolumesAsync(volumeNames, CancellationToken.None)
                .ToListAsync()
                .ConfigureAwait(false);

            // Assert: Should return volume entries
            Assert.That(results.Count, Is.GreaterThan(0), "Expected at least some volume entries to be returned");
        }

        /// <summary>
        /// Tests that <see cref="LocalListAffectedDatabase.GetLogLinesAsync"/> returns empty
        /// results when given an empty list of items.
        /// </summary>
        [Test]
        [Category("Database")]
        public async Task GetLogLines_WithEmptyItems_ReturnsEmpty_Async()
        {
            // Arrange
            using var tempFile = new TempFile();
            await using var db = await LocalListAffectedDatabase.CreateAsync(tempFile, null, CancellationToken.None)
                .ConfigureAwait(false);

            // Act: Call GetLogLines with empty list
            var results = await db.GetLogLinesAsync(Array.Empty<string>(), CancellationToken.None)
                .ToListAsync()
                .ConfigureAwait(false);

            // Assert: Should return empty results
            Assert.That(results.Count, Is.EqualTo(0), "Expected empty results for empty input");
        }

        /// <summary>
        /// Tests that <see cref="LocalListAffectedDatabase.GetVolumesAsync"/> returns empty
        /// results when given an empty list of items.
        /// </summary>
        [Test]
        [Category("Database")]
        public async Task GetVolumes_WithEmptyItems_ReturnsEmpty_Async()
        {
            // Arrange
            using var tempFile = new TempFile();
            await using var db = await LocalListAffectedDatabase.CreateAsync(tempFile, null, CancellationToken.None)
                .ConfigureAwait(false);

            // Act: Call GetVolumes with empty list
            var results = await db.GetVolumesAsync(Array.Empty<string>(), CancellationToken.None)
                .ToListAsync()
                .ConfigureAwait(false);

            // Assert: Should return empty results
            Assert.That(results.Count, Is.EqualTo(0), "Expected empty results for empty input");
        }
    }
}
