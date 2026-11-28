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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Operation;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;
using NUnit.Framework;
using IndexVolumeWriter = Duplicati.Library.Main.Volumes.IndexVolumeWriter;
using InterfaceFileEntry = Duplicati.Library.Interface.IFileEntry;
using IRemoteVolume = Duplicati.Library.Main.Database.IRemoteVolume;
using VolumeWriterBase = Duplicati.Library.Main.Volumes.VolumeWriterBase;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Reading the lock information of the remote volumes stores it in the local database, except in a dry run
    /// </summary>
    public class ReadLockInfoDryRunTests : BasicSetupHelper
    {
        /// <summary>
        /// A destination that reports the same lock expiration for every volume
        /// </summary>
        private sealed class LockReportingBackendManager : IBackendManager
        {
            public static readonly DateTime LockUntil = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            public List<string> Asked { get; } = new();

            public Task<bool> SupportsObjectLockingAsync(CancellationToken cancelToken) => Task.FromResult(true);
            public Task<bool> SupportsHardLinksAsync(CancellationToken cancelToken) => Task.FromResult(false);
            public Task<bool> SupportsFolderDeleteAsync(CancellationToken cancelToken) => Task.FromResult(false);

            public Task<DateTime?> GetObjectLockUntilAsync(string remotename, CancellationToken cancelToken)
            {
                Asked.Add(remotename);
                return Task.FromResult<DateTime?>(LockUntil);
            }

            public Task WaitForEmptyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task WaitForEmptyAsync(IBackendManagerDatabase database, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task FlushPendingMessagesAsync(IBackendManagerDatabase database, CancellationToken cancellationToken) => Task.CompletedTask;
            public void Dispose() { }

            #region Unused interface members
            public Task SetObjectLockUntilAsync(string remotename, DateTime lockUntilUtc, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task<TempFile> GetFileUnencryptedWithPathAsync(string remotename, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task CreateHardLinkAsync(string existingPath, string targetPath, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task DeleteFolderRecursiveAsync(string path, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task PutAsync(VolumeWriterBase blockVolume, IndexVolumeWriter? indexVolume, Func<Task>? indexVolumeFinished, bool waitForComplete, Func<Task>? onDbUpdate, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task PutFileUnencryptedAsync(string remotename, TempFile tempFile, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task PutFileUnencryptedWithPathAsync(string remotename, TempFile tempFile, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task<IEnumerable<InterfaceFileEntry>> ListAsync(string? path, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task EnsureFolderAsync(string? path, CancellationToken cancelToken) => throw new NotImplementedException();
            public TempFile DecryptFile(TempFile volume, string volume_name, Options options, bool dispose) => throw new NotImplementedException();
            public Task DeleteAsync(string remotename, long size, bool waitForComplete, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task DeleteWithPathAsync(string remotename, long size, bool waitForComplete, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task<IQuotaInfo?> GetQuotaInfoAsync(CancellationToken cancelToken) => throw new NotImplementedException();
            public Task<(TempFile File, string Hash, long Size)> GetWithInfoAsync(string remotename, string hash, long size, bool allowParityRepair, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task<TempFile> GetAsync(string remotename, string hash, long size, bool allowParityRepair, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task<TempFile> GetDirectAsync(string remotename, string hash, long size, bool allowParityRepair, CancellationToken cancelToken) => throw new NotImplementedException();
            public IAsyncEnumerable<(TempFile File, string Hash, long Size, string Name)> GetFilesOverlappedAsync(IEnumerable<IRemoteVolume> volumes, bool allowParityRepair, CancellationToken cancelToken) => throw new NotImplementedException();
            public IAsyncEnumerable<(TempFile File, string Name)> GetFilesOverlappedDirectAsync(IEnumerable<IRemoteVolume> volumes, bool allowParityRepair, CancellationToken cancelToken) => throw new NotImplementedException();
            public void UpdateThrottleValues(long maxUploadPrSecond, long maxDownloadPrSecond) => throw new NotImplementedException();
            #endregion
        }

        private async Task<long> CountLockedVolumesAsync()
        {
            using var connection = await SQLiteLoader.LoadConnectionAsync(DBFILE).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT COUNT(*) FROM ""Remotevolume"" WHERE ""LockExpirationTime"" != 0";
            return Convert.ToInt64(await command.ExecuteScalarAsync().ConfigureAwait(false));
        }

        [Test]
        [Category("LockHandler")]
        [TestCase(true)]
        [TestCase(false)]
        public async Task ADryRunDoesNotStoreTheLockInformation_Async(bool dryrun)
        {
            var options = new Dictionary<string, string>(TestOptions) { ["no-encryption"] = "true" };
            File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "content");
            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]).ConfigureAwait(false));

            Assert.That(await CountLockedVolumesAsync().ConfigureAwait(false), Is.Zero);

            var handlerOptions = new Options(new Dictionary<string, string?>(options.ToDictionary(x => x.Key, x => (string?)x.Value))
            {
                ["dry-run"] = dryrun ? "true" : "false"
            });
            var backend = new LockReportingBackendManager();
            var results = new ReadLockInfoResults();
            await new ReadLockInfoHandler(handlerOptions, results).RunAsync(backend).ConfigureAwait(false);

            Assert.That(backend.Asked, Is.Not.Empty, "The destination should be asked for the lock information");
            Assert.That(results.VolumesUpdated, Is.EqualTo(backend.Asked.Count), "Every volume has new lock information to report");
            Assert.That(await CountLockedVolumesAsync().ConfigureAwait(false), Is.EqualTo(dryrun ? 0 : backend.Asked.Count),
                dryrun ? "A dry run should not store the lock information" : "The lock information should be stored");
        }
    }
}
