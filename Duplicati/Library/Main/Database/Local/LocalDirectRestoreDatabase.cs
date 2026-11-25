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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Duplicati.Library.Main.Database.Local
{
    /// <summary>
    /// The database access for building the temporary database of a direct restore.
    /// The filelist is loaded with the methods of <see cref="LocalRecreateDatabase"/>;
    /// the blocks and blockset entries are written with IDs chosen by the caller,
    /// so no lookup is needed to write a row.
    /// </summary>
    internal class LocalDirectRestoreDatabase : LocalRecreateDatabase
    {
        /// <summary>
        /// Inserts a block with a given ID.
        /// </summary>
        private SqliteCommand m_insertBlockWithIdCommand = null!;
        /// <summary>
        /// Inserts a blockset entry.
        /// </summary>
        private SqliteCommand m_insertBlocksetEntryCommand = null!;

        /// <summary>
        /// Creates a new instance of the <see cref="LocalDirectRestoreDatabase"/> class.
        /// </summary>
        /// <param name="parentdb">The database to write to.</param>
        /// <param name="options">The options to use for the database creation.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>The new instance.</returns>
        public static async Task<LocalDirectRestoreDatabase> CreateAsync(LocalDatabase parentdb, Options options, CancellationToken token)
        {
            var dbnew = (LocalDirectRestoreDatabase)
                await CreateAsync(parentdb, options, new LocalDirectRestoreDatabase(), token)
                    .ConfigureAwait(false);

            dbnew.m_insertBlockWithIdCommand = await dbnew.m_connection.CreateCommandAsync(@"
                INSERT INTO ""Block"" (
                    ""ID"",
                    ""Hash"",
                    ""Size"",
                    ""VolumeID""
                )
                VALUES (
                    @Id,
                    @Hash,
                    @Size,
                    @VolumeId
                )
            ", token)
                .ConfigureAwait(false);

            dbnew.m_insertBlocksetEntryCommand = await dbnew.m_connection.CreateCommandAsync(@"
                INSERT INTO ""BlocksetEntry"" (
                    ""BlocksetID"",
                    ""Index"",
                    ""BlockID""
                )
                VALUES (
                    @BlocksetId,
                    @Index,
                    @BlockId
                )
            ", token)
                .ConfigureAwait(false);

            return dbnew;
        }

        /// <summary>
        /// Inserts a block with an ID chosen by the caller.
        /// </summary>
        /// <param name="id">The ID of the block.</param>
        /// <param name="hash">The hash of the block.</param>
        /// <param name="size">The size of the block.</param>
        /// <param name="volumeId">The ID of the volume that holds the block.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that completes when the block is inserted.</returns>
        public async Task InsertBlockAsync(long id, string hash, long size, long volumeId, CancellationToken token)
        {
            await m_insertBlockWithIdCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@Id", id)
                .SetParameterValue("@Hash", hash)
                .SetParameterValue("@Size", size)
                .SetParameterValue("@VolumeId", volumeId)
                .ExecuteNonQueryAsync(writeLog: false, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Adjusts the fileset to what can be restored. A blockset is incomplete if it does not have
        /// every block, or uses a block that is not in the Block table. A file with incomplete data
        /// is removed, as is a symbolic link with incomplete metadata, because the metadata holds
        /// its target. Other entries with incomplete metadata are kept, and what there is of the
        /// metadata is removed, so they are restored without it.
        /// </summary>
        /// <param name="blocksize">The size of a full block.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>The paths of the removed entries, and the paths of the entries that are kept without metadata.</returns>
        public async Task<(List<string> Removed, List<string> WithoutMetadata)> RemoveUnrestorableEntriesAsync(long blocksize, CancellationToken token)
        {
            var broken = $"DirectRestoreBroken_{Library.Utility.Utility.GetHexGuid()}";
            var str_blocksize = Library.Utility.Utility.FormatInvariantValue(blocksize);
            var str_symlink = Library.Utility.Utility.FormatInvariantValue(SYMLINK_BLOCKSET_ID);
            var brokenMetadata = $@"
                SELECT ""ID""
                FROM ""Metadataset""
                WHERE ""BlocksetID"" IN (SELECT ""ID"" FROM ""{broken}"")
            ";
            var removedFiles = $@"
                SELECT ""ID""
                FROM ""FileLookup""
                WHERE
                    ""BlocksetID"" IN (SELECT ""ID"" FROM ""{broken}"")
                    OR (
                        ""BlocksetID"" = {str_symlink}
                        AND ""MetadataID"" IN ({brokenMetadata})
                    )
            ";

            await using var cmd = m_connection.CreateCommand(m_rtr);
            await cmd.ExecuteNonQueryAsync($@"
                CREATE TEMPORARY TABLE ""{broken}"" AS
                SELECT ""BS"".""ID"" AS ""ID""
                FROM ""Blockset"" ""BS""
                WHERE (
                    SELECT COUNT(*)
                    FROM ""BlocksetEntry"" ""BE""
                    JOIN ""Block"" ""B""
                        ON ""B"".""ID"" = ""BE"".""BlockID""
                    WHERE ""BE"".""BlocksetID"" = ""BS"".""ID""
                ) != ((""BS"".""Length"" + {str_blocksize} - 1) / {str_blocksize})
            ", token)
                .ConfigureAwait(false);

            var removed = new List<string>();
            var withoutMetadata = new List<string>();
            try
            {
                cmd.SetCommandAndParameters($@"
                    SELECT ""Path""
                    FROM ""File""
                    WHERE ""ID"" IN ({removedFiles})
                    ORDER BY ""Path""
                ");
                await using (var reader = await cmd.ExecuteReaderAsync(writeLog: false, token).ConfigureAwait(false))
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                        removed.Add(reader.ConvertValueToString(0) ?? "");

                cmd.SetCommandAndParameters($@"
                    SELECT ""Path""
                    FROM ""File""
                    WHERE
                        ""MetadataID"" IN ({brokenMetadata})
                        AND ""ID"" NOT IN ({removedFiles})
                    ORDER BY ""Path""
                ");
                await using (var reader = await cmd.ExecuteReaderAsync(writeLog: false, token).ConfigureAwait(false))
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                        withoutMetadata.Add(reader.ConvertValueToString(0) ?? "");

                if (removed.Count > 0)
                    await cmd.ExecuteNonQueryAsync($@"
                        DELETE FROM ""FilesetEntry""
                        WHERE ""FileID"" IN ({removedFiles})
                    ", token)
                        .ConfigureAwait(false);

                // With no entries the restore finds no metadata to apply, where a part of
                // the metadata would fail to be read
                if (withoutMetadata.Count > 0)
                    await cmd.ExecuteNonQueryAsync($@"
                        DELETE FROM ""BlocksetEntry""
                        WHERE ""BlocksetID"" IN (
                            SELECT ""BlocksetID""
                            FROM ""Metadataset""
                            WHERE ""ID"" IN ({brokenMetadata})
                        )
                    ", token)
                        .ConfigureAwait(false);
            }
            finally
            {
                await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{broken}""", token).ConfigureAwait(false);
            }

            return (removed, withoutMetadata);
        }

        /// <summary>
        /// Inserts a blockset entry.
        /// </summary>
        /// <param name="blocksetId">The ID of the blockset.</param>
        /// <param name="index">The index of the block in the blockset.</param>
        /// <param name="blockId">The ID of the block.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that completes when the entry is inserted.</returns>
        public async Task InsertBlocksetEntryAsync(long blocksetId, long index, long blockId, CancellationToken token)
        {
            await m_insertBlocksetEntryCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@BlocksetId", blocksetId)
                .SetParameterValue("@Index", index)
                .SetParameterValue("@BlockId", blockId)
                .ExecuteNonQueryAsync(writeLog: false, token)
                .ConfigureAwait(false);
        }
    }
}
