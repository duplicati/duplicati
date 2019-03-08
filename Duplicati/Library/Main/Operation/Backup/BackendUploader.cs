//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using CoCoL;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using static Duplicati.Library.Main.Operation.Common.BackendHandler;

namespace Duplicati.Library.Main.Operation.Backup
{
    internal interface IUploadRequest
    {
    }

    internal class FlushRequest : IUploadRequest
    {
        public Task<long> LastWriteSizeAync { get { return m_tcs.Task; } }
        private readonly TaskCompletionSource<long> m_tcs = new TaskCompletionSource<long>();
        public void SetFlushed(long size)
        {
            m_tcs.TrySetResult(size);
        }
    }

    internal class IndexVolumeUploadRequest : IUploadRequest
    {
        public IndexVolumeWriter IndexVolume { get; private set; }

        public IndexVolumeUploadRequest(IndexVolumeWriter indexVolume)
        {
            IndexVolume = indexVolume;
        }
    }

    internal class FilesetUploadRequest : IUploadRequest
    {
        public FilesetVolumeWriter Fileset { get; private set; }

        public FilesetUploadRequest(FilesetVolumeWriter fileset)
        {
            Fileset = fileset;
        }
    }

    internal class VolumeUploadRequest : IUploadRequest
    {
        public FileEntryItem BlockEntry { get; }
        public BlockVolumeWriter BlockVolume { get; }
        public FileEntryItem IndexEntry { get; }
        public IndexVolumeWriter IndexVolume { get; }

        public VolumeUploadRequest(BlockVolumeWriter blockvolume, FileEntryItem blockEntry, IndexVolumeWriter indexVolume, FileEntryItem indexEntry)
        {
            BlockVolume = blockvolume;
            BlockEntry = blockEntry;
            IndexVolume = indexVolume;
            IndexEntry = indexEntry;
        }
    }

    /// <summary>
    /// This class encapsulates all requests to the backend
    /// and ensures that the <code>AsynchronousUploadLimit</code> is honored
    /// </summary>
    internal class BackendUploader
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<BackendUploader>();
        private readonly Func<IBackend> m_backendFactory;
        private CancellationTokenSource m_cancelTokenSource;
        private readonly Options m_options;
        private readonly ITaskReader m_taskreader;
        private readonly StatsCollector m_stats;
        private readonly DatabaseCommon m_database;
        private readonly BackupResults m_results;
        private readonly FileProgressThrottler m_progressUpdater;
        private string m_lastThrottleUploadValue;
        private int m_maxConcurrentUploads;
        private long m_initialUploadThrottle;

        public BackendUploader(Func<IBackend> backendFactory, Options options, DatabaseCommon database, BackupResults results, ITaskReader taskreader, StatsCollector stats)
        {
            m_backendFactory = backendFactory;
            m_options = options;
            m_taskreader = taskreader;
            m_stats = stats;
            m_database = database;
            m_results = results;
            m_progressUpdater = new FileProgressThrottler(stats, options.MaxUploadPrSecond);
        }

        public Task Run()
        {
            return AutomationExtensions.RunTask(new
            {
                Input = Channels.BackendRequest.ForRead,
            },

            async self =>
            {
                var workers = new List<Worker>();
                m_maxConcurrentUploads = m_options.AsynchronousConcurrentUploadLimit <= 0 ? int.MaxValue : m_options.AsynchronousConcurrentUploadLimit;
                m_initialUploadThrottle = m_options.AsynchronousConcurrentUploadLimit <= 0 ? int.MaxValue : m_options.MaxUploadPrSecond / m_maxConcurrentUploads;
                var lastSize = -1L;
                var uploadsInProgress = 0;
                m_cancelTokenSource = new CancellationTokenSource();
                m_progressUpdater.Run(m_cancelTokenSource.Token);

                try
                {
                    while (!await self.Input.IsRetiredAsync && await m_taskreader.ProgressAsync)
                    {
                        var req = await self.Input.ReadAsync();

                        if (!await m_taskreader.ProgressAsync)
                            break;

                        var worker = workers.FirstOrDefault(w => w.Task.IsCompleted && !w.Task.IsFaulted);
                        if (worker == null)
                        {
                            worker = new Worker(m_backendFactory());
                            workers.Add(worker);
                        }

                        if (req is VolumeUploadRequest volumeUpload)
                        {
                            if (volumeUpload.IndexVolume == null)
                                worker.Task = Task.Run(() => UploadFileAsync(volumeUpload.BlockEntry, worker, m_cancelTokenSource.Token));
                            else
                                worker.Task = Task.Run(() => UploadBlockAndIndexAsync(volumeUpload, worker, m_cancelTokenSource.Token));

                            lastSize = volumeUpload.BlockVolume.SourceSize;
                            uploadsInProgress++;
                        }
                        else if (req is FilesetUploadRequest filesetUpload)
                        {
                            worker.Task = Task.Run(() => UploadVolumeWriter(filesetUpload.Fileset, worker, m_cancelTokenSource.Token));
                            uploadsInProgress++;
                        }
                        else if (req is IndexVolumeUploadRequest indexUpload)
                        {
                            worker.Task = Task.Run(() => UploadVolumeWriter(indexUpload.IndexVolume, worker, m_cancelTokenSource.Token));
                            uploadsInProgress++;
                        }
                        else if (req is FlushRequest flush)
                        {
                            try
                            {
                                await Task.WhenAll(workers.Select(w => w.Task));
                                workers.Clear();
                                uploadsInProgress = 0;
                            }
                            finally
                            {
                                flush.SetFlushed(lastSize);
                            }
                            break;
                        }

                        if (uploadsInProgress >= m_maxConcurrentUploads)
                        {
                            await Task.WhenAny(workers.Select(w => w.Task)).ConfigureAwait(false);
                            uploadsInProgress--;

                            var failedUploads = workers.Where(w => w.Task.IsFaulted).Select(w => GetInnerMostException(w.Task.Exception)).ToList();
                            if (failedUploads.Any())
                            {
                                if (failedUploads.Count == 1)
                                    ExceptionDispatchInfo.Capture(failedUploads.First()).Throw();
                                else
                                    throw new AggregateException(failedUploads);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!ex.IsRetiredException())
                    {
                        m_cancelTokenSource.Cancel();
                        try
                        {
                            await Task.WhenAll(workers.Where(w => w.Task.Exception == null).Select(w => w.Task));
                        }
                        catch { }
                        throw;
                    }
                }

                m_results.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_WaitForUpload);

                try
                {
                    m_stats.SetBlocking(true);
                    await Task.WhenAll(workers.Select(w => w.Task));
                }
                finally
                {
                    m_stats.SetBlocking(false);
                }
            });
        }

        private static Exception GetInnerMostException(Exception ex)
        {
            while (ex.InnerException != null)
                ex = ex.InnerException;
            return ex;
        }

        private async Task UploadBlockAndIndexAsync(VolumeUploadRequest upload, Worker worker, CancellationToken cancelToken)
        {
            var blockUploaded = await UploadFileAsync(upload.BlockEntry, worker, cancelToken).ConfigureAwait(false);
            if (blockUploaded && await UploadFileAsync(upload.IndexEntry, worker, cancelToken).ConfigureAwait(false))
                await m_database.AddIndexBlockLinkAsync(upload.IndexVolume.VolumeID, upload.BlockVolume.VolumeID).ConfigureAwait(false);
        }

        private async Task UploadVolumeWriter(VolumeWriterBase volumeWriter, Worker worker, CancellationToken cancelToken)
        {
            var fileEntry = new FileEntryItem(BackendActionType.Put, volumeWriter.RemoteFilename);
            fileEntry.SetLocalfilename(volumeWriter.LocalFilename);
            fileEntry.Encrypt(m_options);
            fileEntry.UpdateHashAndSize(m_options);

            await UploadFileAsync(fileEntry, worker, cancelToken).ConfigureAwait(false);
        }

        private async Task<bool> UploadFileAsync(FileEntryItem item, Worker worker, CancellationToken cancelToken)
        {
            if (cancelToken.IsCancellationRequested)
                return false;

            return await DoWithRetry(async () =>
            {
                if (item.IsRetry)
                    await RenameFileAfterErrorAsync(item).ConfigureAwait(false);
                await DoPut(item, worker.Backend, cancelToken).ConfigureAwait(false);
            },
            item, worker, cancelToken).ConfigureAwait(false);
        }

        private async Task<bool> DoWithRetry(Func<Task> method, FileEntryItem item, Worker worker, CancellationToken cancelToken)
        {
            item.IsRetry = false;
            var retryCount = 0;

            for (retryCount = 0; retryCount <= m_options.NumberOfRetries; retryCount++)
            {
                if (m_options.RetryDelay.Ticks != 0 && retryCount != 0)
                    await Task.Delay(m_options.RetryDelay).ConfigureAwait(false);

                if (cancelToken.IsCancellationRequested)
                    return false;

                try
                {
                    if (worker.Backend == null)
                        worker.Backend = m_backendFactory();
                    await method().ConfigureAwait(false);
                    return true;
                }
                catch (Exception ex)
                {
                    item.IsRetry = true;
                    Logging.Log.WriteRetryMessage(LOGTAG, $"Retry{item.Operation}", ex, "Operation {0} with file {1} attempt {2} of {3} failed with message: {4}", item.Operation, item.RemoteFilename, retryCount + 1, m_options.NumberOfRetries, ex.Message);
                    if (ex is ThreadAbortException || ex is OperationCanceledException)
                        break;

                    await m_stats.SendEventAsync(item.Operation, retryCount < m_options.NumberOfRetries ? BackendEventType.Retrying : BackendEventType.Failed, item.RemoteFilename, item.Size);

                    bool recovered = false;
                    if (m_options.AutocreateFolders && ex is FolderMissingException)
                    {
                        try
                        {
                            // If we successfully create the folder, we can re-use the connection
                            worker.Backend.CreateFolder();
                            recovered = true;
                        }
                        catch (Exception dex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "FolderCreateError", dex, "Failed to create folder: {0}", ex.Message);
                        }
                    }

                    if (!recovered)
                        ResetBackend(ex, worker);

                    if (retryCount == m_options.NumberOfRetries)
                        throw;
                }
                finally
                {
                    if (m_options.NoConnectionReuse)
                        ResetBackend(null, worker);
                }
            }

            return false;
        }

        private void ResetBackend(Exception ex, Worker worker)
        {
            try
            {
                worker.Backend?.Dispose();
            }
            catch (Exception dex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "BackendDisposeError", dex, "Failed to dispose backend instance: {0}", ex?.Message);
            }
            finally
            {
                worker.Backend = null;
            }
        }

        private async Task RenameFileAfterErrorAsync(FileEntryItem item)
        {
            var p = VolumeBase.ParseFilename(item.RemoteFilename);
            var guid = VolumeWriterBase.GenerateGuid();
            var time = p.Time.Ticks == 0 ? p.Time : p.Time.AddSeconds(1);
            var newname = VolumeBase.GenerateFilename(p.FileType, p.Prefix, guid, time, p.CompressionModule, p.EncryptionModule);
            var oldname = item.RemoteFilename;

            await m_stats.SendEventAsync(item.Operation, BackendEventType.Rename, oldname, item.Size);
            await m_stats.SendEventAsync(item.Operation, BackendEventType.Rename, newname, item.Size);
            Logging.Log.WriteInformationMessage(LOGTAG, "RenameRemoteTargetFile", "Renaming \"{0}\" to \"{1}\"", oldname, newname);
            await m_database.RenameRemoteFileAsync(oldname, newname);
            item.RemoteFilename = newname;
        }

        private async Task DoPut(FileEntryItem item, IBackend backend, CancellationToken cancelToken)
        {
            if (cancelToken.IsCancellationRequested)
                return;

            if (item.TrackedInDb)
                await m_database.UpdateRemoteVolumeAsync(item.RemoteFilename, RemoteVolumeState.Uploading, item.Size, item.Hash);

            if (m_options.Dryrun)
            {
                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldUploadVolume", "Would upload volume: {0}, size: {1}", item.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(item.LocalFilename).Length));
                item.DeleteLocalFile();
                return;
            }

            await m_database.LogRemoteOperationAsync("put", item.RemoteFilename, JsonConvert.SerializeObject(new { Size = item.Size, Hash = item.Hash }));
            await m_stats.SendEventAsync(BackendActionType.Put, BackendEventType.Started, item.RemoteFilename, item.Size, updateProgress: false);
            m_progressUpdater.StartFileProgress(item.RemoteFilename, item.Size);

            var begin = DateTime.Now;

            if (!m_options.DisableStreamingTransfers && backend is IStreamingBackend streamingBackend)
            {
                using (var fs = File.OpenRead(item.LocalFilename))
                using (var ts = new ThrottledStream(fs, m_initialUploadThrottle, 0))
                using (var pgs = new ProgressReportingStream(ts, pg => HandleProgress(ts, pg, item.RemoteFilename)))
                    await streamingBackend.Put(item.RemoteFilename, pgs, cancelToken).ConfigureAwait(false);
            }
            else
                await backend.Put(item.RemoteFilename, item.LocalFilename, cancelToken).ConfigureAwait(false);

            var duration = DateTime.Now - begin;
            m_progressUpdater.EndFileProgress(item.RemoteFilename);
            Logging.Log.WriteProfilingMessage(LOGTAG, "UploadSpeed", "Uploaded {0} in {1}, {2}/s", Library.Utility.Utility.FormatSizeString(item.Size), duration, Library.Utility.Utility.FormatSizeString((long)(item.Size / duration.TotalSeconds)));

            if (item.TrackedInDb)
                await m_database.UpdateRemoteVolumeAsync(item.RemoteFilename, RemoteVolumeState.Uploaded, item.Size, item.Hash);

            await m_stats.SendEventAsync(BackendActionType.Put, BackendEventType.Completed, item.RemoteFilename, item.Size);

            if (m_options.ListVerifyUploads)
            {
                var f = backend.List().FirstOrDefault(n => n.Name.Equals(item.RemoteFilename, StringComparison.OrdinalIgnoreCase));
                if (f == null)
                    throw new Exception(string.Format("List verify failed, file was not found after upload: {0}", item.RemoteFilename));
                else if (f.Size != item.Size && f.Size >= 0)
                    throw new Exception(string.Format("List verify failed for file: {0}, size was {1} but expected to be {2}", f.Name, f.Size, item.Size));
            }

            item.DeleteLocalFile();
            await m_database.CommitTransactionAsync("CommitAfterUpload");
        }

        private void HandleProgress(ThrottledStream ts, long progress, string path)
        {
            var updateThrottleSpeeds = false;
            m_options.RawOptions.TryGetValue("throttle-upload", out var tmp);
            if (tmp != m_lastThrottleUploadValue)
            {
                m_lastThrottleUploadValue = tmp;
                m_initialUploadThrottle = m_options.MaxUploadPrSecond / m_maxConcurrentUploads;
                updateThrottleSpeeds = true;
            }

            if (updateThrottleSpeeds)
                m_progressUpdater.UpdateThrottleSpeeds(m_options.MaxUploadPrSecond);
            m_progressUpdater.UpdateFileProgress(path, progress, ts);
        }

        private class Worker
        {
            public Task Task;
            public IBackend Backend;

            public Worker(IBackend backend)
            {
                Backend = backend;
                Task = Task.FromResult(true);
            }
        }
    }
}
