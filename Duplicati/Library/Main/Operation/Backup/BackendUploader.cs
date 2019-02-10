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
using System;
using CoCoL;
using System.Threading.Tasks;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Main.Volumes;
using System.Collections.Generic;
using static Duplicati.Library.Main.Operation.Common.BackendHandler;
using Duplicati.Library.Interface;
using Newtonsoft.Json;
using Duplicati.Library.Utility;
using System.Linq;
using System.IO;

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
        public BlockVolumeWriter BlockVolume { get; private set; }
        public TemporaryIndexVolume IndexVolume { get; private set;}

        public VolumeUploadRequest(BlockVolumeWriter blockvolume, TemporaryIndexVolume indexvolume)
        {
            BlockVolume = blockvolume;
            IndexVolume = indexvolume;
        }
    }
   
    /// <summary>
    /// This class encapsulates all requests to the backend
    /// and ensures that the <code>AsynchronousUploadLimit</code> is honored
    /// </summary>
    internal class BackendUploader
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<BackendUploader>();

        private Options m_options;
        private IBackend m_backend;
        private BackendHandler m_backendHandler;
        private Common.ITaskReader m_taskreader;
        private StatsCollector m_stats;
        private DatabaseCommon m_database;
        private readonly BackupResults m_results;
        private string m_lastThrottleUploadValue;
        private string m_lastThrottleDownloadValue;

        public BackendUploader(Common.BackendHandler backendHandler, IBackend backend, Options options, Common.DatabaseCommon database, BackupResults results, Common.ITaskReader taskreader, StatsCollector stats)
        {
            this.m_options = options;
            this.m_backend = backend;
            this.m_backendHandler = backendHandler;
            this.m_taskreader = taskreader;
            this.m_stats = stats;
            this.m_database = database;
            this.m_results = results;
        }

        public Task Run()
        {
            return AutomationExtensions.RunTask(new
            {
                Input = Channels.BackendRequest.ForRead,
            },

            async self =>
            {
                var inProgress = new Queue<KeyValuePair<int, Task>>();
                var max_pending = m_options.AsynchronousUploadLimit == 0 ? long.MaxValue : m_options.AsynchronousUploadLimit;
                var noIndexFiles = m_options.IndexfilePolicy == Options.IndexFileStrategy.None;
                var active = 0;
                var lastSize = -1L;
                
                while(!await self.Input.IsRetiredAsync && await m_taskreader.ProgressAsync)
                {
                    try
                    {
                        var req = await self.Input.ReadAsync();

                        if (!await m_taskreader.ProgressAsync)
                            continue;
                        
                        var task = default(KeyValuePair<int, Task>);
                        if (req is VolumeUploadRequest)
                        {
                            lastSize = ((VolumeUploadRequest)req).BlockVolume.SourceSize;

                            if (noIndexFiles || ((VolumeUploadRequest)req).IndexVolume == null)
                                task = new KeyValuePair<int, Task>(1, UploadFileAsync(((VolumeUploadRequest)req).BlockVolume, null));
                            else
                                task = new KeyValuePair<int, Task>(2, UploadFileAsync(((VolumeUploadRequest)req).BlockVolume, name => ((VolumeUploadRequest)req).IndexVolume.CreateVolume(name, m_options, m_database)));
                        }
                        else if (req is FilesetUploadRequest)
                            task = new KeyValuePair<int, Task>(1, UploadFileAsync(((FilesetUploadRequest)req).Fileset));
                        else if (req is IndexVolumeUploadRequest)
                            task = new KeyValuePair<int, Task>(1, UploadFileAsync(((IndexVolumeUploadRequest)req).IndexVolume));
                        else if (req is FlushRequest)
                        {
                            try
                            {
                                while(inProgress.Count > 0)
                                    await inProgress.Dequeue().Value;
                                active = 0;
                            }
                            finally
                            {
                                ((FlushRequest)req).SetFlushed(lastSize);
                            }
                            break;
                        }

                        if (task.Value != null)
                        {
                            inProgress.Enqueue(task);
                            active += task.Key;
                        }
                    }
                    catch(Exception ex)
                    {
                        if (!ex.IsRetiredException())
                            throw;
                    }

                    while(active >= max_pending)
                    {
                        var top = inProgress.Dequeue();

                        // See if we are done
                        if (await Task.WhenAny(top.Value, Task.Delay(500)) != top.Value)
                        {
                            try
                            {
                                m_stats.SetBlocking(true);
                                await top.Value;
                            }
                            finally
                            {
                                m_stats.SetBlocking(false);
                            }
                        }

						active -= top.Key;
                    }
                }

                m_results.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_WaitForUpload);

                try
                {
                    m_stats.SetBlocking(true);
                    while (inProgress.Count > 0)
                        await inProgress.Dequeue().Value;
                }
                finally
                {
                    m_stats.SetBlocking(false);
                }
            });
                                
        }

        private async Task UploadFileAsync(VolumeWriterBase item, Func<string, Task<IndexVolumeWriter>> createIndexFile = null)
        {
            var fe = new FileEntryItem(BackendActionType.Put, item.RemoteFilename);
            fe.SetLocalfilename(item.LocalFilename);

            var tcs = new TaskCompletionSource<bool>();

            fe.Encrypt(m_options);
            fe.UpdateHashAndSize(m_options);

            try
            {
                await DoWithRetry(fe, async () =>
                {
                    if (fe.IsRetry)
                        await RenameFileAfterErrorAsync(fe).ConfigureAwait(false);
                    return await DoPut(fe).ConfigureAwait(false);
                }).ConfigureAwait(false);

                if (createIndexFile != null)
                {
                    var ix = await createIndexFile(fe.RemoteFilename).ConfigureAwait(false);
                    var indexFile = new FileEntryItem(BackendActionType.Put, ix.RemoteFilename);
                    indexFile.SetLocalfilename(ix.LocalFilename);

                    await m_database.UpdateRemoteVolumeAsync(indexFile.RemoteFilename, RemoteVolumeState.Uploading, -1, null);

                    await DoWithRetry(indexFile, async () =>
                    {
                        if (indexFile.IsRetry)
                            await RenameFileAfterErrorAsync(indexFile).ConfigureAwait(false);

                        var res = await DoPut(indexFile).ConfigureAwait(false);

                        // Register that the index file is tracking the block file
                        await m_database.AddIndexBlockLinkAsync(
                            ix.VolumeID,
                            await m_database.GetRemoteVolumeIDAsync(fe.RemoteFilename)
                        ).ConfigureAwait(false);


                        return res;
                    }).ConfigureAwait(false);
                }

                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                if (ex is System.Threading.ThreadAbortException)
                    tcs.TrySetCanceled();
                else
                    tcs.TrySetException(ex);
            }

            await tcs.Task.ConfigureAwait(false);
        }

        private async Task<T> DoWithRetry<T>(FileEntryItem item, Func<Task<T>> method)
        {
            item.IsRetry = false;
            Exception lastException = null;

            if(!await m_taskreader.ProgressAsync)
                throw new OperationCanceledException();

            for (var i = 0; i < m_options.NumberOfRetries; i++)
            {
                if (m_options.RetryDelay.Ticks != 0 && i != 0)
                    await Task.Delay(m_options.RetryDelay).ConfigureAwait(false);

                if (!await m_taskreader.ProgressAsync)
                    throw new OperationCanceledException();

                try
                {
                    return await method().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    item.IsRetry = true;
                    lastException = ex;
                    Logging.Log.WriteRetryMessage(LOGTAG, $"Retry{item.Operation}", ex, "Operation {0} with file {1} attempt {2} of {3} failed with message: {4}", item.Operation, item.RemoteFilename, i + 1, m_options.NumberOfRetries, ex.Message);
                    // If the thread is aborted, we exit here
                    if (ex is System.Threading.ThreadAbortException || ex is OperationCanceledException)
                        break;

                    await m_stats.SendEventAsync(item.Operation, i < m_options.NumberOfRetries ? BackendEventType.Retrying : BackendEventType.Failed, item.RemoteFilename, item.Size);

                    bool recovered = false;
                    if (ex is Duplicati.Library.Interface.FolderMissingException && m_options.AutocreateFolders)
                    {
                        try
                        {
                            // If we successfully create the folder, we can re-use the connection
                            m_backend.CreateFolder();
                            recovered = true;
                        }
                        catch (Exception dex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "FolderCreateError", dex, "Failed to create folder: {0}", ex.Message);
                        }
                    }

                    if (!recovered)
                        ResetBackend(ex);
                }
                finally
                {
                    if (m_options.NoConnectionReuse)
                        ResetBackend(null);
                }
            }

            throw lastException;
        }

        private void ResetBackend(Exception ex)
        {
            try
            {
                m_backend?.Dispose();
            }
            catch (Exception dex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "BackendDisposeError", dex, "Failed to dispose backend instance: {0}", ex?.Message);
            }
            m_backend = null;
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

        private async Task<bool> DoPut(FileEntryItem item, bool updatedHash = false)
        {
            // If this is not already encrypted, do it now
            item.Encrypt(m_options);

            updatedHash |= item.UpdateHashAndSize(m_options);

            if (updatedHash && item.TrackedInDb)
                await m_database.UpdateRemoteVolumeAsync(item.RemoteFilename, RemoteVolumeState.Uploading, item.Size, item.Hash);

            if (m_options.Dryrun)
            {
                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldUploadVolume", "Would upload volume: {0}, size: {1}", item.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(item.LocalFilename).Length));
                item.DeleteLocalFile();
                return true;
            }

            await m_database.LogRemoteOperationAsync("put", item.RemoteFilename, JsonConvert.SerializeObject(new { Size = item.Size, Hash = item.Hash }));
            await m_stats.SendEventAsync(BackendActionType.Put, BackendEventType.Started, item.RemoteFilename, item.Size);

            var begin = DateTime.Now;

            if (m_backend is Library.Interface.IStreamingBackend && !m_options.DisableStreamingTransfers)
            {
                using (var fs = System.IO.File.OpenRead(item.LocalFilename))
                using (var ts = new ThrottledStream(fs, m_options.MaxUploadPrSecond, m_options.MaxDownloadPrSecond))
                using (var pgs = new Library.Utility.ProgressReportingStream(ts, pg => HandleProgress(ts, pg)))
                    ((Library.Interface.IStreamingBackend)m_backend).Put(item.RemoteFilename, pgs);
            }
            else
                m_backend.Put(item.RemoteFilename, item.LocalFilename);

            var duration = DateTime.Now - begin;
            Logging.Log.WriteProfilingMessage(LOGTAG, "UploadSpeed", "Uploaded {0} in {1}, {2}/s", Library.Utility.Utility.FormatSizeString(item.Size), duration, Library.Utility.Utility.FormatSizeString((long)(item.Size / duration.TotalSeconds)));

            if (item.TrackedInDb)
                await m_database.UpdateRemoteVolumeAsync(item.RemoteFilename, RemoteVolumeState.Uploaded, item.Size, item.Hash);

            await m_stats.SendEventAsync(BackendActionType.Put, BackendEventType.Completed, item.RemoteFilename, item.Size);

            if (m_options.ListVerifyUploads)
            {
                var f = m_backend.List().FirstOrDefault(n => n.Name.Equals(item.RemoteFilename, StringComparison.OrdinalIgnoreCase));
                if (f == null)
                    throw new Exception(string.Format("List verify failed, file was not found after upload: {0}", item.RemoteFilename));
                else if (f.Size != item.Size && f.Size >= 0)
                    throw new Exception(string.Format("List verify failed for file: {0}, size was {1} but expected to be {2}", f.Name, f.Size, item.Size));
            }

            item.DeleteLocalFile();
            await m_database.CommitTransactionAsync("CommitAfterUpload");

            return true;
        }

        private void HandleProgress(ThrottledStream ts, long pg)
        {
            if (!m_taskreader.TransferProgressAsync.WaitForTask().Result)
                throw new OperationCanceledException();

            // Update the throttle speeds if they have changed
            string tmp;
            m_options.RawOptions.TryGetValue("throttle-upload", out tmp);
            if (tmp != m_lastThrottleUploadValue)
            {
                ts.WriteSpeed = m_options.MaxUploadPrSecond;
                m_lastThrottleUploadValue = tmp;
            }

            m_options.RawOptions.TryGetValue("throttle-download", out tmp);
            if (tmp != m_lastThrottleDownloadValue)
            {
                ts.ReadSpeed = m_options.MaxDownloadPrSecond;
                m_lastThrottleDownloadValue = tmp;
            }

            m_stats.UpdateBackendProgress(pg);
        }
    }
}

