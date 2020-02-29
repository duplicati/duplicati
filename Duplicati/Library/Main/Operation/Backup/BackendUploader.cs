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
        public Task<long> LastWriteSizeAsync { get { return m_tcs.Task; } }
        private readonly TaskCompletionSource<long> m_tcs = new TaskCompletionSource<long>();

        public void SetFlushed(long size)
        {
            m_tcs.TrySetResult(size);
        }

        public void TrySetCanceled()
        {
            m_tcs.TrySetCanceled();
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
        public TemporaryIndexVolume IndexVolume { get; }
        public Options Options { get; }
        public BackupDatabase Database { get; }

        public VolumeUploadRequest(BlockVolumeWriter blockVolume, FileEntryItem blockEntry, TemporaryIndexVolume indexVolume, Options options, BackupDatabase database)
        {
            BlockVolume = blockVolume;
            BlockEntry = blockEntry;
            IndexVolume = indexVolume;
            Options = options;
            Database = database;
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
        private readonly DatabaseCommon m_database;
        private long m_initialUploadThrottleSpeed;
        private long m_lastSize;
        private int m_lastThrottleCheckTime;
        private long m_lastUploadThrottleSpeed;
        private int m_maxConcurrentUploads;
        private readonly Options m_options;
        private readonly FileProgressThrottler m_progressUpdater;
        private readonly StatsCollector m_stats;
        private readonly IOperationProgressUpdaterAndReporter m_operationUpdater;
        private readonly ITaskReader m_taskReader;
        private List<Worker> m_workers = new List<Worker>();

        public BackendUploader(Func<IBackend> backendFactory, Options options, DatabaseCommon database, ITaskReader taskReader, StatsCollector stats, IOperationProgressUpdaterAndReporter operationUpdater)
        {
            m_backendFactory = backendFactory;
            m_options = options;
            m_taskReader = taskReader;
            m_stats = stats;
            m_database = database;
            m_operationUpdater = operationUpdater;
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
                m_maxConcurrentUploads = m_options.AsynchronousConcurrentUploadLimit <= 0 ? int.MaxValue : m_options.AsynchronousConcurrentUploadLimit;
                m_initialUploadThrottleSpeed = m_options.AsynchronousConcurrentUploadLimit <= 0 ? int.MaxValue : m_options.MaxUploadPrSecond / m_maxConcurrentUploads;
                var uploadsInProgress = 0;
                var waitForInputTimeout = TimeSpan.FromSeconds(2);
                m_cancelTokenSource = new CancellationTokenSource();
                m_progressUpdater.Run(m_cancelTokenSource.Token);

                try
                {
                    while (!await self.Input.IsRetiredAsync)
                    {
                        var request = await GetRequest(self.Input, waitForInputTimeout);

                        if (m_operationUpdater.CurrentPhase == OperationPhase.Paused_WaitForUpload)
                        {
                            await WaitForUploadsToFinish(flush: null).ConfigureAwait(false);
                            uploadsInProgress = 0;
                            m_operationUpdater.UpdatePhase(OperationPhase.Paused);
                            m_progressUpdater.Pause();
                            if (!await m_taskReader.TransferProgressAsync.ConfigureAwait(false))
                            {
                                (request as FlushRequest)?.SetFlushed(-1);
                                break;
                            }
                            m_progressUpdater.Resume();
                        }
                        else if (!await m_taskReader.TransferProgressAsync.ConfigureAwait(false))
                        {
                            m_cancelTokenSource.Cancel();
                            (request as FlushRequest)?.SetFlushed(-1);
                            break;
                        }

                        if (request == null)
                            continue;

                        await HandleRequest(request).ConfigureAwait(false);
                        uploadsInProgress++;
                        if (request is FlushRequest)
                            break;

                        if (uploadsInProgress >= m_maxConcurrentUploads)
                        {
                            if (!await AnyUploadCompleted().ConfigureAwait(false))
                                continue;

                            uploadsInProgress--;

                            var failedUploads = m_workers.Where(w => w.Task.IsFaulted).Select(w => GetInnerMostException(w.Task.Exception)).ToList();
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
                catch (Exception)
                {
                    m_cancelTokenSource.Cancel();
                    try
                    {
                        await Task.WhenAll(m_workers.Select(w => w.Task));
                    }
                    catch
                    {
                        // As we are cancelling all threads we do not need to alert the user to any of these exceptions.
                    }
                    finally
                    {
                        m_workers.ForEach(w => w.Dispose());
                    }
                    throw;
                }

                try
                {
                    m_stats.SetBlocking(true);
                    await Task.WhenAll(m_workers.Select(w => w.Task));
                }
                finally
                {
                    m_stats.SetBlocking(false);
                    m_workers.ForEach(w => w.Dispose());
                }
            });
        }

        /// <returns><c>true</c> if an upload task completed or <c>false</c> if the operation is requested to pause or stop.</returns>
        private async Task<bool> AnyUploadCompleted()
        {
            var twoSeconds = TimeSpan.FromSeconds(2);
            while (true)
            {
                await Task.Delay(twoSeconds).ConfigureAwait(false);
                if (m_operationUpdater.CurrentPhase == OperationPhase.Paused_WaitForUpload || !await m_taskReader.TransferProgressAsync.ConfigureAwait(false))
                    return false;

                if (m_workers.Any(w => w.Task.IsCompleted))
                    return true;
            }
        }

        private static Exception GetInnerMostException(Exception ex)
        {
            while (ex.InnerException != null)
                ex = ex.InnerException;
            return ex;
        }

        private async Task<IUploadRequest> GetRequest(IReadChannel<IUploadRequest> channel, TimeSpan timeout)
        {
            try
            {
                return await channel.ReadAsync(timeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Timeout exception is expected and handled
            }
            catch (TaskCanceledException)
            {
                // Either the read will be tried again or uploads have been canceled
            }

            return null;
        }

        private async Task HandleRequest(IUploadRequest request)
        {
            var worker = m_workers.FirstOrDefault(w => w.Task.IsCompleted && !w.Task.IsFaulted);
            if (worker == null)
            {
                worker = new Worker(m_backendFactory());
                m_workers.Add(worker);
            }

            if (request is VolumeUploadRequest volumeUpload)
            {
                if (volumeUpload.IndexVolume == null)
                    worker.Task = Task.Run(() => UploadFileAsync(volumeUpload.BlockEntry, worker, m_cancelTokenSource.Token));
                else
                    worker.Task = Task.Run(() => UploadBlockAndIndexAsync(volumeUpload, worker, m_cancelTokenSource.Token));

                m_lastSize = volumeUpload.BlockVolume.SourceSize;
            }
            else if (request is FilesetUploadRequest filesetUpload)
            {
                worker.Task = Task.Run(() => UploadVolumeWriter(filesetUpload.Fileset, worker, m_cancelTokenSource.Token));
            }
            else if (request is IndexVolumeUploadRequest indexUpload)
            {
                worker.Task = Task.Run(() => UploadVolumeWriter(indexUpload.IndexVolume, worker, m_cancelTokenSource.Token));
            }
            else if (request is FlushRequest flush)
            {
                try
                {
                    await WaitForUploadsToFinish(flush).ConfigureAwait(false);
                }
                finally
                {
                    flush.SetFlushed(m_lastSize);
                }
            }
        }

        private async Task UploadBlockAndIndexAsync(VolumeUploadRequest upload, Worker worker, CancellationToken cancelToken)
        {
            if (await UploadFileAsync(upload.BlockEntry, worker, cancelToken).ConfigureAwait(false))
            {
                // We must use the BlockEntry's RemoteFilename and not the BlockVolume's since the
                // BlockEntry's' RemoteFilename reflects renamed files due to retries after errors.
                IndexVolumeWriter indexVolumeWriter = await upload.IndexVolume.CreateVolume(upload.BlockEntry.RemoteFilename, upload.BlockEntry.Hash, upload.BlockEntry.Size, upload.Options, upload.Database);
                FileEntryItem indexEntry = indexVolumeWriter.CreateFileEntryForUpload(upload.Options);
                if (await UploadFileAsync(indexEntry, worker, cancelToken).ConfigureAwait(false))
                {
                    await m_database.AddIndexBlockLinkAsync(indexVolumeWriter.VolumeID, upload.BlockVolume.VolumeID).ConfigureAwait(false);
                }
            }
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
                return await DoPut(item, worker.Backend, cancelToken).ConfigureAwait(false);
            },
            item, worker, cancelToken).ConfigureAwait(false);
        }

        private async Task<bool> DoWithRetry(Func<Task<bool>> method, FileEntryItem item, Worker worker, CancellationToken cancelToken)
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
                    return await method().ConfigureAwait(false);
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

        private async Task<bool> DoPut(FileEntryItem item, IBackend backend, CancellationToken cancelToken)
        {
            if (cancelToken.IsCancellationRequested)
                return false;

            if (item.TrackedInDb)
                await m_database.UpdateRemoteVolumeAsync(item.RemoteFilename, RemoteVolumeState.Uploading, item.Size, item.Hash);

            if (m_options.Dryrun)
            {
                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldUploadVolume", "Would upload volume: {0}, size: {1}", item.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(item.LocalFilename).Length));
                item.DeleteLocalFile();
                return true;
            }

            await m_database.LogRemoteOperationAsync("put", item.RemoteFilename, JsonConvert.SerializeObject(new { Size = item.Size, Hash = item.Hash }));
            await m_stats.SendEventAsync(BackendActionType.Put, BackendEventType.Started, item.RemoteFilename, item.Size, updateProgress: false);
            m_progressUpdater.StartFileProgress(item.RemoteFilename, item.Size);

            var begin = DateTime.Now;

            if (!m_options.DisableStreamingTransfers && backend is IStreamingBackend streamingBackend)
            {
                // A download throttle speed is not given to the ThrottledStream as we are only uploading data here
                using (var fs = File.OpenRead(item.LocalFilename))
                using (var ts = new ThrottledStream(fs, m_initialUploadThrottleSpeed, 0))
                using (var pgs = new ProgressReportingStream(ts, pg => HandleProgress(ts, pg, item.RemoteFilename)))
                    await streamingBackend.PutAsync(item.RemoteFilename, pgs, cancelToken).ConfigureAwait(false);
            }
            else
                await backend.PutAsync(item.RemoteFilename, item.LocalFilename, cancelToken).ConfigureAwait(false);

            var success = !cancelToken.IsCancellationRequested;

            var duration = DateTime.Now - begin;
            m_progressUpdater.EndFileProgress(item.RemoteFilename);

            if (success)
            {
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
            }

            item.DeleteLocalFile();
            await m_database.CommitTransactionAsync("CommitAfterUpload");
            return success;
        }

        private void HandleProgress(ThrottledStream stream, long progress, string path)
        {
            UpdateThrottleSpeed();
            m_progressUpdater.UpdateFileProgress(path, progress, stream);
        }

        private void UpdateThrottleSpeed()
        {
            var updateThrottleSpeed = false;

            lock (m_progressUpdater)
            {
                var currentTick = Environment.TickCount;
                if (currentTick - m_lastThrottleCheckTime > 2000)
                {
                    m_lastThrottleCheckTime = currentTick;
                    updateThrottleSpeed = true;
                }
            }

            if (!updateThrottleSpeed)
                return;

            var maxUploadPerSecond = m_options.MaxUploadPrSecond;
            if (maxUploadPerSecond != m_lastUploadThrottleSpeed)
            {
                m_lastUploadThrottleSpeed = maxUploadPerSecond;
                m_initialUploadThrottleSpeed = maxUploadPerSecond / m_maxConcurrentUploads;
                m_progressUpdater.UpdateThrottleSpeeds(maxUploadPerSecond);
            }
        }

        private async Task WaitForUploadsToFinish(FlushRequest flush)
        {
            while (m_workers.Any())
            {
                Task finishedTask = await Task.WhenAny(m_workers.Select(w => w.Task)).ConfigureAwait(false);
                if (finishedTask.IsFaulted)
                {
                    flush?.TrySetCanceled();
                    ExceptionDispatchInfo.Capture(finishedTask.Exception).Throw();
                }

                Worker finishedWorker = m_workers.Single(w => w.Task == finishedTask);
                m_workers.Remove(finishedWorker);
                finishedWorker.Dispose();
            }
        }

        private class Worker : IDisposable
        {
            public Task Task;
            public IBackend Backend;

            public Worker(IBackend backend)
            {
                Backend = backend;
                Task = Task.FromResult(true);
            }

            public void Dispose()
            {
                this.Task?.Dispose();
                this.Backend?.Dispose();
            }
        }
    }
}
