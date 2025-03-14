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
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation
{
    internal class TestHandler
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<TestHandler>();

        private readonly Options m_options;
        private readonly TestResults m_results;

        public TestHandler(Options options, TestResults results)
        {
            m_options = options;
            m_results = results;
        }

        public async Task RunAsync(long samples, IBackendManager backendManager)
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath), "DatabaseDoesNotExist");

            using (var db = new LocalTestDatabase(m_options.Dbpath))
            {
                db.SetResult(m_results);
                Utility.UpdateOptionsFromDb(db, m_options);
                Utility.VerifyOptionsAndUpdateDatabase(db, m_options);
                db.VerifyConsistency(m_options.Blocksize, m_options.BlockhashSize, !m_options.DisableFilelistConsistencyChecks, null);
                await FilelistProcessor.VerifyRemoteList(backendManager, m_options, db, m_results.BackendWriter, latestVolumesOnly: true, verifyMode: FilelistProcessor.VerifyMode.VerifyOnly, null).ConfigureAwait(false);

                await DoRunAsync(samples, db, backendManager).ConfigureAwait(false);
                db.WriteResults();
            }
        }

        public async Task DoRunAsync(long samples, LocalTestDatabase db, IBackendManager backend)
        {
            var files = db.SelectTestTargets(samples, m_options).ToList();

            m_results.OperationProgressUpdater.UpdatePhase(OperationPhase.Verify_Running);
            m_results.OperationProgressUpdater.UpdateProgress(0);
            var progress = 0L;

            if (m_options.FullRemoteVerification != Options.RemoteTestStrategy.False)
            {
                await foreach (var (tf, hash, size, name) in backend.GetFilesOverlappedAsync(files, m_results.TaskControl.ProgressToken).ConfigureAwait(false))
                {
                    var vol = new RemoteVolume(name, hash, size);
                    try
                    {
                        if (!await m_results.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                        {
                            await backend.WaitForEmptyAsync(db, null, m_results.TaskControl.ProgressToken).ConfigureAwait(false);
                            m_results.EndTime = DateTime.UtcNow;
                            return;
                        }

                        progress++;
                        m_results.OperationProgressUpdater.UpdateProgress((float)progress / files.Count);

                        KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>> res;
                        using (tf)
                            res = TestVolumeInternals(db, vol, tf, m_options, m_options.FullBlockVerification ? 1.0 : 0.2);
                        m_results.AddResult(res.Key, res.Value);

                        if (!string.IsNullOrWhiteSpace(vol.Hash) && vol.Size > 0)
                        {
                            if (res.Value == null || !res.Value.Any())
                            {
                                var rv = db.GetRemoteVolume(vol.Name, null);

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
                                            db.UpdateRemoteVolume(vol.Name, RemoteVolumeState.Verified, vol.Size, vol.Hash);
                                        }
                                    }
                                }
                            }
                        }

                        db.UpdateVerificationCount(vol.Name);
                    }
                    catch (Exception ex)
                    {
                        m_results.AddResult(vol.Name, new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>[] { new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>(Duplicati.Library.Interface.TestEntryStatus.Error, ex.Message) });
                        Logging.Log.WriteErrorMessage(LOGTAG, "RemoteFileProcessingFailed", ex, "Failed to process file {0}", vol.Name);
                        if (ex is System.Threading.ThreadAbortException)
                        {
                            m_results.EndTime = DateTime.UtcNow;
                            throw;
                        }
                    }
                }
            }
            else
            {
                foreach (var f in files)
                {
                    try
                    {
                        if (!await m_results.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                        {
                            m_results.EndTime = DateTime.UtcNow;
                            return;
                        }

                        progress++;
                        m_results.OperationProgressUpdater.UpdateProgress((float)progress / files.Count);

                        if (f.Size <= 0 || string.IsNullOrWhiteSpace(f.Hash))
                        {
                            Logging.Log.WriteInformationMessage(LOGTAG, "MissingRemoteHash", "No hash or size recorded for {0}, performing full verification", f.Name);
                            KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>> res;

                            (var tf, var hash, var size) = await backend.GetWithInfoAsync(f.Name, f.Hash, f.Size, m_results.TaskControl.ProgressToken).ConfigureAwait(false);

                            using (tf)
                                res = TestVolumeInternals(db, f, tf, m_options, 1);
                            m_results.AddResult(res.Key, res.Value);

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
                                        db.UpdateRemoteVolume(f.Name, RemoteVolumeState.Verified, size, hash);
                                    }
                                }
                            }
                        }
                        else
                        {
                            using (var tf = await backend.GetAsync(f.Name, f.Hash, f.Size, m_results.TaskControl.ProgressToken).ConfigureAwait(false))
                            { }
                        }

                        db.UpdateVerificationCount(f.Name);
                        m_results.AddResult(f.Name, []);
                    }
                    catch (Exception ex)
                    {
                        m_results.AddResult(f.Name, [new KeyValuePair<TestEntryStatus, string>(TestEntryStatus.Error, ex.Message)]);
                        Logging.Log.WriteErrorMessage(LOGTAG, "FailedToProcessFile", ex, "Failed to process file {0}", f.Name);
                        if (ex is ThreadAbortException || ex is TaskCanceledException)
                        {
                            m_results.EndTime = DateTime.UtcNow;
                            throw;
                        }
                    }
                }
            }

            m_results.EndTime = DateTime.UtcNow;
            // generate a backup error status when any test is failing - except for 'extra' status
            // because these problems don't block database rebuilding.
            var filtered = from n in m_results.Verifications where n.Value.Any(x => x.Key != TestEntryStatus.Extra) select n;
            if (!filtered.Any())
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "Test results", "Successfully verified {0} remote files", m_results.VerificationsActualLength);
            }
            else
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Test results", null, "Verified {0} remote files with {1} problem(s)", m_results.VerificationsActualLength, filtered.Count());
            }
        }

        /// <summary>
        /// Tests the volume by examining the internal contents
        /// </summary>
        /// <param name="vol">The remote volume being examined</param>
        /// <param name="tf">The path to the downloaded copy of the file</param>
        /// <param name="sample_percent">A value between 0 and 1 that indicates how many blocks are tested in a dblock file</param>
        public static KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>> TestVolumeInternals(LocalTestDatabase db, IRemoteVolume vol, string tf, Options options, double sample_percent)
        {
            var hashsize = HashFactory.HashSizeBytes(options.BlockHashAlgorithm);
            var parsedInfo = Volumes.VolumeBase.ParseFilename(vol.Name);
            sample_percent = Math.Min(1, Math.Max(sample_percent, 0.01));

            switch (parsedInfo.FileType)
            {
                case RemoteVolumeType.Files:
                    //Compare with db and see if all files are accounted for 
                    // with correct file hashes and blocklist hashes
                    using (var fl = db.CreateFilelist(vol.Name))
                    {
                        using (var rd = new Volumes.FilesetVolumeReader(parsedInfo.CompressionModule, tf, options))
                            foreach (var f in rd.Files)
                                fl.Add(f.Path, f.Size, f.Hash, f.Metasize, f.Metahash, f.BlocklistHashes, f.Type, f.Time);

                        return new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(vol.Name, fl.Compare().ToList());
                    }

                case RemoteVolumeType.Index:
                    var blocklinks = new List<Tuple<string, string, long>>();
                    IEnumerable<KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>> combined = new KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>[0];

                    //Compare with db and see that all hashes and volumes are listed
                    using (var rd = new Volumes.IndexVolumeReader(parsedInfo.CompressionModule, tf, options, hashsize))
                        foreach (var v in rd.Volumes)
                        {
                            blocklinks.Add(new Tuple<string, string, long>(v.Filename, v.Hash, v.Length));
                            using (var bl = db.CreateBlocklist(v.Filename))
                            {
                                foreach (var h in v.Blocks)
                                    bl.AddBlock(h.Key, h.Value);

                                combined = combined.Union(bl.Compare().ToArray());
                            }
                        }

                    using (var il = db.CreateIndexlist(vol.Name))
                    {
                        foreach (var t in blocklinks)
                            il.AddBlockLink(t.Item1, t.Item2, t.Item3);

                        combined = combined.Union(il.Compare()).ToList();
                    }

                    return new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(vol.Name, combined.ToList());
                case RemoteVolumeType.Blocks:
                    using (var blockhasher = HashFactory.CreateHasher(options.BlockHashAlgorithm))
                    using (var bl = db.CreateBlocklist(vol.Name))
                    using (var rd = new Volumes.BlockVolumeReader(parsedInfo.CompressionModule, tf, options))
                    {
                        //Verify that all blocks are in the file
                        foreach (var b in rd.Blocks)
                            bl.AddBlock(b.Key, b.Value);

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

                        return new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(vol.Name, changes.Union(bl.Compare().ToList()));
                    }
            }

            Logging.Log.WriteWarningMessage(LOGTAG, "UnexpectedFileType", null, "Unexpected file type {0} for {1}", parsedInfo.FileType, vol.Name);
            return new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(vol.Name, null);
        }
    }
}

