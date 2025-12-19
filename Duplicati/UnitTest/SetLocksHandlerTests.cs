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
using Duplicati.Library.Utility;
using IndexVolumeWriter = Duplicati.Library.Main.Volumes.IndexVolumeWriter;
using IRemoteVolume = Duplicati.Library.Main.Database.IRemoteVolume;
using VolumeWriterBase = Duplicati.Library.Main.Volumes.VolumeWriterBase;
using InterfaceFileEntry = Duplicati.Library.Interface.IFileEntry;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class SetLocksHandlerTests : BasicSetupHelper
    {
        private sealed class FakeLockingBackendManager : IBackendManager
        {
            private readonly bool m_failFirstLock;
            private bool m_hasFailed;

            public FakeLockingBackendManager(bool failFirstLock = false)
            {
                m_failFirstLock = failFirstLock;
            }

            public List<(string Name, DateTime UntilUtc)> LockedVolumes { get; } = new();

            public bool WaitedForEmpty { get; private set; }

            public bool SupportsObjectLocking => true;

            public Task SetObjectLockUntilAsync(string remotename, DateTime lockUntilUtc, CancellationToken cancelToken)
            {
                LockedVolumes.Add((remotename, lockUntilUtc));

                if (m_failFirstLock && !m_hasFailed)
                {
                    m_hasFailed = true;
                    throw new InvalidOperationException("Simulated lock failure");
                }

                return Task.CompletedTask;
            }

            public Task<DateTime?> GetObjectLockUntilAsync(string remotename, CancellationToken cancelToken)
                => Task.FromResult<DateTime?>(LockedVolumes.FirstOrDefault(x => x.Name == remotename).UntilUtc);

            public Task WaitForEmptyAsync(CancellationToken cancellationToken)
            {
                WaitedForEmpty = true;
                return Task.CompletedTask;
            }

            public Task WaitForEmptyAsync(LocalDatabase database, CancellationToken cancellationToken) => WaitForEmptyAsync(cancellationToken);

            public void Dispose() { }

            #region Unused interface members
            public Task PutAsync(VolumeWriterBase blockVolume, IndexVolumeWriter? indexVolume, Func<Task>? indexVolumeFinished, bool waitForComplete, Func<Task>? onDbUpdate, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task PutVerificationFileAsync(string remotename, TempFile tempFile, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task<IEnumerable<InterfaceFileEntry>> ListAsync(CancellationToken cancelToken) => throw new NotImplementedException();
            public TempFile DecryptFile(TempFile volume, string volume_name, Options options) => throw new NotImplementedException();
            public Task DeleteAsync(string remotename, long size, bool waitForComplete, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task<IQuotaInfo?> GetQuotaInfoAsync(CancellationToken cancelToken) => throw new NotImplementedException();
            public Task<(TempFile File, string Hash, long Size)> GetWithInfoAsync(string remotename, string hash, long size, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task<TempFile> GetAsync(string remotename, string hash, long size, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task<TempFile> GetDirectAsync(string remotename, string hash, long size, CancellationToken cancelToken) => throw new NotImplementedException();
            public IAsyncEnumerable<(TempFile File, string Hash, long Size, string Name)> GetFilesOverlappedAsync(IEnumerable<IRemoteVolume> volumes, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task FlushPendingMessagesAsync(LocalDatabase database, CancellationToken cancellationToken) => Task.CompletedTask;
            public void UpdateThrottleValues(long maxUploadPrSecond, long maxDownloadPrSecond) => throw new NotImplementedException();
            #endregion
        }

        [SetUp]
        public void SetUp()
        {
            File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "content");
        }

        [Test]
        [Category("LockHandler")]
        public async Task AppliesLocksForVolumesInFileset()
        {
            var options = new Dictionary<string, string>(TestOptions);
            using (var controller = new Controller("file://" + TARGETFOLDER, options, null))
            {
                controller.Backup([DATAFOLDER]);
            }

            var lockDbPath = Path.Combine(BASEFOLDER, $"locktest-{Guid.NewGuid():N}.sqlite");
            File.Copy(options["dbpath"], lockDbPath, true);

            await using var db = await LocalLockDatabase.CreateAsync(lockDbPath, null, CancellationToken.None).ConfigureAwait(false);

            var filesets = new List<KeyValuePair<long, DateTime>>();
            await foreach (var entry in db.FilesetTimes(CancellationToken.None).ConfigureAwait(false))
                filesets.Add(entry);

            var targetFileset = filesets.Last();
            var expectedVolumes = new List<string>();
            await foreach ((var volume, _) in db.GetRemoteVolumesDependingOnFilesets(new[] { targetFileset.Key }, CancellationToken.None).ConfigureAwait(false))
                expectedVolumes.Add(volume);

            var lockingOptions = new Options(new Dictionary<string, string?>(options.ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value))
            {
                ["dbpath"] = lockDbPath,
                ["remote-file-lock-duration"] = "1D",
            });

            var backend = new FakeLockingBackendManager();
            var handler = new SetLocksHandler(lockingOptions, new SetLockResults(), new[] { targetFileset.Value });

            await handler.RunAsync(backend, db).ConfigureAwait(false);

            CollectionAssert.AreEquivalent(expectedVolumes, backend.LockedVolumes.Select(x => x.Name));
            ClassicAssert.True(backend.WaitedForEmpty);
        }

        [Test]
        [Category("LockHandler")]
        public async Task ContinuesWhenLockingFails()
        {
            var options = new Dictionary<string, string>(TestOptions);
            using (var controller = new Controller("file://" + TARGETFOLDER, options, null))
            {
                controller.Backup([DATAFOLDER]);
            }

            var lockDbPath = Path.Combine(BASEFOLDER, $"locktest-{Guid.NewGuid():N}.sqlite");
            File.Copy(options["dbpath"], lockDbPath, true);

            await using var db = await LocalLockDatabase.CreateAsync(lockDbPath, null, CancellationToken.None).ConfigureAwait(false);

            var filesets = new List<KeyValuePair<long, DateTime>>();
            await foreach (var entry in db.FilesetTimes(CancellationToken.None).ConfigureAwait(false))
                filesets.Add(entry);

            var latestFileset = filesets.Last();
            var expectedVolumes = new List<string>();
            await foreach ((var volume, _) in db.GetRemoteVolumesDependingOnFilesets(new[] { latestFileset.Key }, CancellationToken.None).ConfigureAwait(false))
                expectedVolumes.Add(volume);

            var lockingOptions = new Options(new Dictionary<string, string?>(options.ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value))
            {
                ["dbpath"] = lockDbPath,
                ["remote-file-lock-duration"] = "1D",
            });

            var backend = new FakeLockingBackendManager(failFirstLock: true);
            var handler = new SetLocksHandler(lockingOptions, new SetLockResults(), new[] { latestFileset.Value });

            Assert.DoesNotThrowAsync(() => handler.RunAsync(backend, db));
            CollectionAssert.AreEquivalent(expectedVolumes, backend.LockedVolumes.Select(x => x.Name));
            ClassicAssert.True(backend.WaitedForEmpty);
        }
    }
}
