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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Backend;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Operation;
using Duplicati.Library.Utility;
using NUnit.Framework;
using IndexVolumeWriter = Duplicati.Library.Main.Volumes.IndexVolumeWriter;
using InterfaceFileEntry = Duplicati.Library.Interface.IFileEntry;
using IRemoteVolume = Duplicati.Library.Main.Database.IRemoteVolume;
using VolumeWriterBase = Duplicati.Library.Main.Volumes.VolumeWriterBase;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A dry-run repair with --repair-refresh-lock-info on a destination that supports object locks
    /// </summary>
    public class RepairLockRefreshDryRunTests : BasicSetupHelper
    {
        /// <summary>
        /// The file destination, reporting that it supports object locks and that every volume is locked
        /// </summary>
        private sealed class LockingFileBackendManager(IBackendManager inner) : IBackendManager
        {
            public int LockQueries { get; private set; }

            public Task<bool> SupportsObjectLockingAsync(CancellationToken cancelToken) => Task.FromResult(true);
            public Task<DateTime?> GetObjectLockUntilAsync(string remotename, CancellationToken cancelToken)
            {
                LockQueries++;
                return Task.FromResult<DateTime?>(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            }
            public Task SetObjectLockUntilAsync(string remotename, DateTime lockUntilUtc, CancellationToken cancelToken) => throw new NotSupportedException();

            public Task<bool> SupportsHardLinksAsync(CancellationToken cancelToken) => inner.SupportsHardLinksAsync(cancelToken);
            public Task<bool> SupportsFolderDeleteAsync(CancellationToken cancelToken) => inner.SupportsFolderDeleteAsync(cancelToken);
            public Task<TempFile> GetFileUnencryptedWithPathAsync(string remotename, CancellationToken cancelToken) => inner.GetFileUnencryptedWithPathAsync(remotename, cancelToken);
            public Task CreateHardLinkAsync(string existingPath, string targetPath, CancellationToken cancelToken) => inner.CreateHardLinkAsync(existingPath, targetPath, cancelToken);
            public Task DeleteFolderRecursiveAsync(string path, CancellationToken cancelToken) => inner.DeleteFolderRecursiveAsync(path, cancelToken);
            public Task PutAsync(VolumeWriterBase blockVolume, IndexVolumeWriter? indexVolume, Func<Task>? indexVolumeFinished, bool waitForComplete, Func<Task>? onDbUpdate, CancellationToken cancelToken) => inner.PutAsync(blockVolume, indexVolume, indexVolumeFinished, waitForComplete, onDbUpdate, cancelToken);
            public Task PutFileUnencryptedAsync(string remotename, TempFile tempFile, CancellationToken cancelToken) => inner.PutFileUnencryptedAsync(remotename, tempFile, cancelToken);
            public Task PutFileUnencryptedWithPathAsync(string remotename, TempFile tempFile, CancellationToken cancelToken) => inner.PutFileUnencryptedWithPathAsync(remotename, tempFile, cancelToken);
            public Task<IEnumerable<InterfaceFileEntry>> ListAsync(string? path, CancellationToken cancelToken) => inner.ListAsync(path, cancelToken);
            public Task EnsureFolderAsync(string? path, CancellationToken cancelToken) => inner.EnsureFolderAsync(path, cancelToken);
            public TempFile DecryptFile(TempFile volume, string volume_name, Options options, bool dispose) => inner.DecryptFile(volume, volume_name, options, dispose);
            public Task DeleteAsync(string remotename, long size, bool waitForComplete, CancellationToken cancelToken) => inner.DeleteAsync(remotename, size, waitForComplete, cancelToken);
            public Task DeleteWithPathAsync(string remotename, long size, bool waitForComplete, CancellationToken cancelToken) => inner.DeleteWithPathAsync(remotename, size, waitForComplete, cancelToken);
            public Task<IQuotaInfo?> GetQuotaInfoAsync(CancellationToken cancelToken) => inner.GetQuotaInfoAsync(cancelToken);
            public Task<(TempFile File, string Hash, long Size)> GetWithInfoAsync(string remotename, string hash, long size, bool allowParityRepair, CancellationToken cancelToken) => inner.GetWithInfoAsync(remotename, hash, size, allowParityRepair, cancelToken);
            public Task<TempFile> GetAsync(string remotename, string hash, long size, bool allowParityRepair, CancellationToken cancelToken) => inner.GetAsync(remotename, hash, size, allowParityRepair, cancelToken);
            public Task<TempFile> GetDirectAsync(string remotename, string hash, long size, bool allowParityRepair, CancellationToken cancelToken) => inner.GetDirectAsync(remotename, hash, size, allowParityRepair, cancelToken);
            public IAsyncEnumerable<(TempFile File, string Hash, long Size, string Name)> GetFilesOverlappedAsync(IEnumerable<IRemoteVolume> volumes, bool allowParityRepair, CancellationToken cancelToken) => inner.GetFilesOverlappedAsync(volumes, allowParityRepair, cancelToken);
            public IAsyncEnumerable<(TempFile File, string Name)> GetFilesOverlappedDirectAsync(IEnumerable<IRemoteVolume> volumes, bool allowParityRepair, CancellationToken cancelToken) => inner.GetFilesOverlappedDirectAsync(volumes, allowParityRepair, cancelToken);
            public Task FlushPendingMessagesAsync(IBackendManagerDatabase database, CancellationToken cancellationToken) => inner.FlushPendingMessagesAsync(database, cancellationToken);
            public Task WaitForEmptyAsync(CancellationToken cancellationToken) => inner.WaitForEmptyAsync(cancellationToken);
            public Task WaitForEmptyAsync(IBackendManagerDatabase database, CancellationToken cancellationToken) => inner.WaitForEmptyAsync(database, cancellationToken);
            public void UpdateThrottleValues(long maxUploadPrSecond, long maxDownloadPrSecond) => inner.UpdateThrottleValues(maxUploadPrSecond, maxDownloadPrSecond);
            public void Dispose() => inner.Dispose();
        }

        /// <summary>
        /// Without a local database, the dry run recreates it in a temporary file, so there is no database to refresh
        /// the lock information in afterwards
        /// </summary>
        [Test]
        [Category("LockHandler")]
        public async Task ADryRunRecreateWithLockRefreshCompletes_Async()
        {
            var options = new Dictionary<string, string>(TestOptions) { ["no-encryption"] = "true" };
            File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "content");
            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]).ConfigureAwait(false));
            File.Delete(DBFILE);

            var repairOptions = new Options(new Dictionary<string, string?>(System.Linq.Enumerable.ToDictionary(options, x => x.Key, x => (string?)x.Value))
            {
                ["dry-run"] = "true",
                ["repair-refresh-lock-info"] = "true",
            });
            var results = new RepairResults();
            using var backend = new LockingFileBackendManager(new BackendManager("file://" + TARGETFOLDER, repairOptions, results.BackendWriter, results.TaskControl, false));

            await new RepairHandler(repairOptions, results).RunAsync(backend, null!).ConfigureAwait(false);

            Assert.That(results.Errors, Is.Empty);
            Assert.That(File.Exists(DBFILE), Is.False, "The dry run should not write the database");
        }
    }
}
