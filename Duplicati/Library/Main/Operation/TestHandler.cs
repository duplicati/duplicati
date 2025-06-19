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
using Duplicati.Library.Main.Database;
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using System.Threading.Tasks;
using Duplicati.Library.Main.Volumes;
using System.Threading;

namespace Duplicati.Library.Main.Operation
{
    internal class TestHandler
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<TestHandler>();

        private readonly Options m_options;
        private readonly TestResults m_result;

        public TestHandler(Options options, TestResults results)
        {
            m_options = options;
            m_result = results;
        }

        public async Task RunAsync(long samples, IBackendManager backendManager)
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath), "DatabaseDoesNotExist");

            await using var db = await LocalTestDatabase.CreateAsync(m_options.Dbpath, m_options.SqlitePageCache, null, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
            await Utility.UpdateOptionsFromDb(db, m_options, m_result.TaskControl.ProgressToken)
                .ConfigureAwait(false);
            await Utility.VerifyOptionsAndUpdateDatabase(db, m_options, m_result.TaskControl.ProgressToken)
                .ConfigureAwait(false);

            await db
                .VerifyConsistency(m_options.Blocksize, m_options.BlockhashSize, !m_options.DisableFilelistConsistencyChecks, m_result.TaskControl.ProgressToken)
                .ConfigureAwait(false);

            await FilelistProcessor.VerifyRemoteList(backendManager, m_options, db, m_result.BackendWriter, latestVolumesOnly: true, verifyMode: FilelistProcessor.VerifyMode.VerifyOnly, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
            await DoRunAsync(samples, db, backendManager).ConfigureAwait(false);
            await db.Transaction
                .CommitAsync("TestHandlerComplete")
                .ConfigureAwait(false);
        }

        public async Task DoRunAsync(long samples, LocalTestDatabase db, IBackendManager backend)
        {
            var files = await db
                .SelectTestTargets(samples, m_options, m_result.TaskControl.ProgressToken)
                .ToListAsync(cancellationToken: m_result.TaskControl.ProgressToken)
                .ConfigureAwait(false);

            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Verify_Running);
            m_result.OperationProgressUpdater.UpdateProgress(0);
            var progress = 0L;

            if (m_options.FullRemoteVerification != Options.RemoteTestStrategy.False)
            {
                var faultyIndexFiles = new List<IRemoteVolume>();
                await foreach (var (tf, hash, size, name) in backend.GetFilesOverlappedAsync(files, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                {
                    var vol = new RemoteVolume(name, hash, size);
                    try
                    {
                        if (!await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                        {
                            await backend.WaitForEmptyAsync(db, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                            m_result.EndTime = DateTime.UtcNow;
                            return;
                        }

                        progress++;
                        m_result.OperationProgressUpdater.UpdateProgress((float)progress / files.Count);

                        KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>> res;
                        using (tf)
                            res = await TestVolumeInternals(db, vol, tf, m_options, m_options.FullBlockVerification ? 1.0 : 0.2, m_result.TaskControl.ProgressToken)
                                .ConfigureAwait(false);

                        var parsedInfo = VolumeBase.ParseFilename(vol.Name);
                        if (parsedInfo.FileType == RemoteVolumeType.Index)
                        {
                            if (res.Value.Any(x => x.Key == TestEntryStatus.Extra))
                            {
                                // Bad hack, but for now, the index files sometimes have extra blocklist hashes
                                Logging.Log.WriteVerboseMessage(LOGTAG, "IndexFileExtraBlocks", null, "Index file {0} has extra blocks", vol.Name);
                                res = new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(
                                    res.Key, res.Value.Where(x => x.Key != TestEntryStatus.Extra).ToList()
                                );
                            }

                            if (res.Value.Any(x => x.Key == TestEntryStatus.Missing || x.Key == TestEntryStatus.Modified))
                                faultyIndexFiles.Add(vol);
                        }

                        m_result.AddResult(res.Key, res.Value);

                        if (!string.IsNullOrWhiteSpace(vol.Hash) && vol.Size > 0)
                        {
                            if (res.Value == null || !res.Value.Any())
                            {
                                var rv = await db
                                    .GetRemoteVolume(vol.Name, m_result.TaskControl.ProgressToken)
                                    .ConfigureAwait(false);

                                if (rv.ID < 0)
                                {
                                    if (string.IsNullOrWhiteSpace(rv.Hash) || rv.Size <= 0)
                                    {
                                        if (m_options.Dryrun)
                                        {
                                            Logging.Log.WriteDryrunMessage(LOGTAG, "CaptureHashAndSize", "Successfully captured hash and size for {0}, would update database", vol.Name);
                                        }
                                        else
                                        {
                                            Logging.Log.WriteInformationMessage(LOGTAG, "CaptureHashAndSize", "Successfully captured hash and size for {0}, updating database", vol.Name);
                                            await db
                                                .UpdateRemoteVolume(vol.Name, RemoteVolumeState.Verified, vol.Size, vol.Hash, m_result.TaskControl.ProgressToken)
                                                .ConfigureAwait(false);
                                        }
                                    }
                                }
                            }
                        }

                        await db
                            .UpdateVerificationCount(vol.Name, m_result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_result.AddResult(vol.Name, [new KeyValuePair<TestEntryStatus, string>(TestEntryStatus.Error, ex.Message)]);
                        Logging.Log.WriteErrorMessage(LOGTAG, "RemoteFileProcessingFailed", ex, "Failed to process file {0}", vol.Name);
                        if (ex.IsAbortException())
                        {
                            m_result.EndTime = DateTime.UtcNow;
                            throw;
                        }
                    }
                }

                if (faultyIndexFiles.Any())
                {
                    if (m_options.ReplaceFaultyIndexFiles)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "FaultyIndexFiles", null, "Found {0} faulty index files, repairing now", faultyIndexFiles.Count);
                        await ReplaceFaultyIndexFilesAsync(faultyIndexFiles, backend, db, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                    }
                    else
                        Logging.Log.WriteWarningMessage(LOGTAG, "FaultyIndexFiles", null, "Found {0} faulty index files, use the option {1} to repair them", faultyIndexFiles.Count, "--replace-faulty-index-files");

                }
            }
            else
            {
                foreach (var f in files)
                {
                    try
                    {
                        if (!await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                        {
                            m_result.EndTime = DateTime.UtcNow;
                            return;
                        }

                        progress++;
                        m_result.OperationProgressUpdater.UpdateProgress((float)progress / files.Count);

                        if (f.Size <= 0 || string.IsNullOrWhiteSpace(f.Hash))
                        {
                            Logging.Log.WriteInformationMessage(LOGTAG, "MissingRemoteHash", "No hash or size recorded for {0}, performing full verification", f.Name);
                            KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>> res;

                            (var tf, var hash, var size) = await backend.GetWithInfoAsync(f.Name, f.Hash, f.Size, m_result.TaskControl.ProgressToken).ConfigureAwait(false);

                            using (tf)
                                res = await TestVolumeInternals(db, f, tf, m_options, 1, m_result.TaskControl.ProgressToken)
                                    .ConfigureAwait(false);
                            m_result.AddResult(res.Key, res.Value);

                            if (!string.IsNullOrWhiteSpace(hash) && size > 0)
                            {
                                if (res.Value == null || !res.Value.Any())
                                {
                                    if (m_options.Dryrun)
                                    {
                                        Logging.Log.WriteDryrunMessage(LOGTAG, "CapturedHashAndSize", "Successfully captured hash and size for {0}, would update database", f.Name);
                                    }
                                    else
                                    {
                                        Logging.Log.WriteInformationMessage(LOGTAG, "CapturedHashAndSize", "Successfully captured hash and size for {0}, updating database", f.Name);
                                        await db
                                            .UpdateRemoteVolume(f.Name, RemoteVolumeState.Verified, size, hash, m_result.TaskControl.ProgressToken)
                                            .ConfigureAwait(false);
                                    }
                                }
                            }
                        }
                        else
                        {
                            using (var tf = await backend.GetAsync(f.Name, f.Hash, f.Size, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                            { }
                        }

                        await db
                            .UpdateVerificationCount(f.Name, m_result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);
                        m_result.AddResult(f.Name, []);
                    }
                    catch (Exception ex)
                    {
                        m_result.AddResult(f.Name, [new KeyValuePair<TestEntryStatus, string>(TestEntryStatus.Error, ex.Message)]);
                        Logging.Log.WriteErrorMessage(LOGTAG, "FailedToProcessFile", ex, "Failed to process file {0}", f.Name);
                        if (ex.IsAbortOrCancelException())
                        {
                            m_result.EndTime = DateTime.UtcNow;
                            throw;
                        }
                    }
                }
            }

            m_result.EndTime = DateTime.UtcNow;
            // generate a backup error status when any test is failing - except for 'extra' status
            // because these problems don't block database rebuilding.
            var filtered = from n in m_result.Verifications where n.Value.Any(x => x.Key != TestEntryStatus.Extra) select n;
            if (!filtered.Any())
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "Test results", "Successfully verified {0} remote files", m_result.VerificationsActualLength);
            }
            else
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Test results", null, "Verified {0} remote files with {1} problem(s)", m_result.VerificationsActualLength, filtered.Count());
            }
        }

        /// <summary>
        /// Tests the volume by examining the internal contents.
        /// </summary>
        /// <param name="db">The local test database to use for verification.</param>
        /// <param name="vol">The remote volume being examined.</param>
        /// <param name="tf">The path to the downloaded copy of the file.</param>
        /// <param name="options">The options to use for the test.</param>
        /// <param name="sample_percent">A value between 0 and 1 that indicates how many blocks are tested in a dblock file.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited returns a key-value pair where the key is the volume name and the value is a list of test results.</returns>
        public static async Task<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>> TestVolumeInternals(LocalTestDatabase db, IRemoteVolume vol, string tf, Options options, double sample_percent, CancellationToken cancellationToken)
        {
            var hashsize = HashFactory.HashSizeBytes(options.BlockHashAlgorithm);
            var parsedInfo = Volumes.VolumeBase.ParseFilename(vol.Name);
            sample_percent = Math.Min(1, Math.Max(sample_percent, 0.01));

            switch (parsedInfo.FileType)
            {
                case RemoteVolumeType.Files:
                    //Compare with db and see if all files are accounted for
                    // with correct file hashes and blocklist hashes
                    await using (var fl = await db.CreateFilelist(vol.Name, cancellationToken).ConfigureAwait(false))
                    {
                        using (var rd = new Volumes.FilesetVolumeReader(parsedInfo.CompressionModule, tf, options))
                            foreach (var f in rd.Files)
                                await fl
                                    .Add(f.Path, f.Size, f.Hash, f.Metasize, f.Metahash, f.BlocklistHashes, f.Type, f.Time, cancellationToken)
                                    .ConfigureAwait(false);

                        return new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(vol.Name, await fl.Compare(cancellationToken).ToListAsync(cancellationToken: cancellationToken).ConfigureAwait(false));
                    }

                case RemoteVolumeType.Index:
                    var blocklinks = new List<Tuple<string, string, long>>();
                    var combined = new List<KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>>();

                    //Compare with db and see that all hashes and volumes are listed
                    using (var rd = new Volumes.IndexVolumeReader(parsedInfo.CompressionModule, tf, options, hashsize))
                    {
                        foreach (var v in rd.Volumes)
                        {
                            blocklinks.Add(new Tuple<string, string, long>(v.Filename, v.Hash, v.Length));
                            await using (var bl = await db.CreateBlocklist(v.Filename, cancellationToken).ConfigureAwait(false))
                            {
                                foreach (var h in v.Blocks)
                                    await bl
                                        .AddBlock(h.Key, h.Value, cancellationToken)
                                        .ConfigureAwait(false);

                                combined.AddRange(
                                    await bl
                                        .Compare(cancellationToken)
                                        .ToListAsync(cancellationToken: cancellationToken)
                                        .ConfigureAwait(false)
                                );
                            }
                        }

                        if (options.IndexfilePolicy == Options.IndexFileStrategy.Full)
                        {
                            var hashesPerBlock = options.Blocksize / options.BlockhashSize;
                            await using (var bl = await db.CreateBlocklistHashList(vol.Name, cancellationToken).ConfigureAwait(false))
                            {
                                foreach (var b in rd.BlockLists)
                                    await bl
                                        .AddBlockHash(b.Hash, b.Length, cancellationToken)
                                        .ConfigureAwait(false);

                                combined.AddRange(
                                    await bl
                                        .Compare(hashesPerBlock, options.BlockhashSize, options.Blocksize, cancellationToken)
                                        .ToListAsync(cancellationToken: cancellationToken)
                                        .ConfigureAwait(false)
                                );
                            }
                        }
                    }

                    // Compare with db and see that all blocklists are listed
                    await using (var il = await db.CreateIndexlist(vol.Name, cancellationToken).ConfigureAwait(false))
                    {
                        foreach (var t in blocklinks)
                            await il
                                .AddBlockLink(t.Item1, t.Item2, t.Item3, cancellationToken)
                                .ConfigureAwait(false);

                        combined.AddRange(
                            await il
                                .Compare(cancellationToken)
                                .ToArrayAsync(cancellationToken: cancellationToken)
                                .ConfigureAwait(false)
                        );
                    }

                    return new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(vol.Name, combined);
                case RemoteVolumeType.Blocks:
                    using (var blockhasher = HashFactory.CreateHasher(options.BlockHashAlgorithm))
                    await using (var bl = await db.CreateBlocklist(vol.Name, cancellationToken).ConfigureAwait(false))
                    using (var rd = new Volumes.BlockVolumeReader(parsedInfo.CompressionModule, tf, options))
                    {
                        //Verify that all blocks are in the file
                        foreach (var b in rd.Blocks)
                            await bl
                                .AddBlock(b.Key, b.Value, cancellationToken)
                                .ConfigureAwait(false);

                        //Select random blocks and verify their hashes match the filename and size
                        var hashsamples = new List<KeyValuePair<string, long>>(rd.Blocks);
                        var sampleCount = Math.Min(Math.Max(0, (int)(hashsamples.Count * sample_percent)), hashsamples.Count - 1);
                        var rnd = new Random();

                        while (hashsamples.Count > sampleCount)
                            hashsamples.RemoveAt(rnd.Next(hashsamples.Count));

                        var blockbuffer = new byte[options.Blocksize];
                        var changes = new List<KeyValuePair<Library.Interface.TestEntryStatus, string>>();
                        foreach (var s in hashsamples)
                        {
                            var size = rd.ReadBlock(s.Key, blockbuffer);
                            if (size != s.Value)
                                changes.Add(new KeyValuePair<Library.Interface.TestEntryStatus, string>(Library.Interface.TestEntryStatus.Modified, s.Key));
                            else
                            {
                                var hash = Convert.ToBase64String(blockhasher.ComputeHash(blockbuffer, 0, size));
                                if (hash != s.Key)
                                    changes.Add(new KeyValuePair<Library.Interface.TestEntryStatus, string>(Library.Interface.TestEntryStatus.Modified, s.Key));
                            }
                        }

                        return new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(
                            vol.Name,
                            changes.Union(
                                await bl
                                    .Compare(cancellationToken)
                                    .ToListAsync(cancellationToken: cancellationToken)
                                    .ConfigureAwait(false)
                            )
                        );
                    }
            }

            Logging.Log.WriteWarningMessage(LOGTAG, "UnexpectedFileType", null, "Unexpected file type {0} for {1}", parsedInfo.FileType, vol.Name);
            return new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(vol.Name, null);
        }

        private async Task ReplaceFaultyIndexFilesAsync(List<IRemoteVolume> faultyIndexFiles, IBackendManager backendManager, LocalTestDatabase db, CancellationToken cancellationToken)
        {
            await using var repairdb =
                await LocalRepairDatabase.CreateAsync(db, null, cancellationToken)
                    .ConfigureAwait(false);

            foreach (var vol in faultyIndexFiles)
            {
                if (!await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                {
                    m_result.EndTime = DateTime.UtcNow;
                    return;
                }

                IndexVolumeWriter newEntry = null;
                try
                {
                    var w = newEntry = new IndexVolumeWriter(m_options);
                    await RepairHandler.RunRepairDindex(backendManager, repairdb, w, vol, m_options, cancellationToken).ConfigureAwait(false);
                    if (m_options.Dryrun)
                    {
                        Logging.Log.WriteDryrunMessage(LOGTAG, "ReplaceFaultyIndexFile", "Would replace faulty index file {0} with {1}", vol.Name, w.RemoteFilename);
                    }
                    else
                    {
                        await backendManager.DeleteAsync(vol.Name, vol.Size, true, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                        await backendManager.WaitForEmptyAsync(repairdb, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                        await repairdb.Transaction
                            .CommitAsync("ReplaceFaultyIndexFileCommit")
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    newEntry?.Dispose();
                    Logging.Log.WriteErrorMessage(LOGTAG, "FailedToReplaceFaultyIndexFile", ex, "Failed to replace faulty index file {0}", vol.Name);
                    if (ex.IsAbortException())
                        throw;
                }
            }
        }
    }
}

