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

using System;
using System.Collections.Generic;
using Duplicati.Library.Interface;
using Duplicati.Library.Snapshots;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#pragma warning disable CA1416

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class WindowsSnapshotTests
    {
        private sealed class FakeSnapshotInfo(string snapshotDeviceObject) : ISnapshotInfo
        {
            public string SnapshotDeviceObject { get; } = snapshotDeviceObject;
        }

        private sealed class FakeSnapshotProvider : ISnapshotProvider
        {
            public Exception DoSnapshotSetException { get; set; }
            public string SnapshotDeviceObject { get; set; } = @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1";

            public int BackupCompleteCalls { get; private set; }
            public int DeleteSnapshotCalls { get; private set; }
            public int DisposeCalls { get; private set; }

            public void EnableWriterClasses(Guid[] guids) { }
            public void DisableWriterClasses(Guid[] guids) { }
            public void GatherWriterMetadata() { }
            public void FreeWriterMetadata() { }
            public void VerifyWriters(Guid[] guids) { }
            public ISnapshotInfo GetSnapshotProperties(Guid shadowId) => new FakeSnapshotInfo(SnapshotDeviceObject);
            public void StartSnapshotSet() { }
            public void PrepareForBackup() { }

            public void DoSnapshotSet()
            {
                if (DoSnapshotSetException != null)
                    throw DoSnapshotSetException;
            }

            public bool IsVolumeSupported(string drive) => true;
            public Guid AddToSnapshotSet(string drive) => Guid.NewGuid();
            public void BackupComplete() => BackupCompleteCalls++;
            public void DeleteSnapshot(Guid shadowId, bool forceDelete) => DeleteSnapshotCalls++;
            public IEnumerable<WriterMetaData> ParseWriterMetaData(Guid[] writers) => [];
            public void Dispose() => DisposeCalls++;
        }

        private static readonly string[] Sources = [@"C:\data"];

        [Test]
        public void FailedSnapshotCreationReleasesTheSnapshot()
        {
            var expected = new InvalidOperationException("DoSnapshotSet failed");
            var provider = new FakeSnapshotProvider { DoSnapshotSetException = expected };

            var actual = Assert.Throws<InvalidOperationException>(() =>
                new WindowsSnapshot(Sources, Array.Empty<string>(), new Dictionary<string, string>(), false, (_, _, _) => provider));

            Assert.AreSame(expected, actual);
            Assert.AreEqual(1, provider.BackupCompleteCalls);
            Assert.AreEqual(1, provider.DeleteSnapshotCalls);
            Assert.AreEqual(1, provider.DisposeCalls);
        }

        [Test]
        public void RetryWithSystemProviderReleasesTheFirstSnapshot()
        {
            var providers = new List<(Guid ProviderId, FakeSnapshotProvider Provider)>();
            ISnapshotProvider Factory(WindowsSnapshotProvider provider, TimeSpan timeout, Guid providerId)
            {
                var fake = new FakeSnapshotProvider();
                if (providers.Count == 0)
                    fake.SnapshotDeviceObject = string.Empty;
                providers.Add((providerId, fake));
                return fake;
            }

            using (new WindowsSnapshot(Sources, Array.Empty<string>(), new Dictionary<string, string>(), false, Factory))
            {
                Assert.AreEqual(2, providers.Count);
                Assert.AreEqual(WindowsSnapshot.MS_SOFTWARE_PROVIDER_ID, providers[1].ProviderId);
                Assert.AreEqual(1, providers[0].Provider.DeleteSnapshotCalls);
                Assert.AreEqual(1, providers[0].Provider.DisposeCalls);
                Assert.AreEqual(0, providers[1].Provider.DisposeCalls);
            }

            Assert.AreEqual(1, providers[1].Provider.DisposeCalls);
        }

        /// <summary>
        /// The error VSS gives when another program is creating a shadow copy
        /// </summary>
        private static Exception SnapshotSetInProgress()
            => new System.Runtime.InteropServices.COMException("The creation of a shadow copy is already in progress.", unchecked((int)0x80042316));

        /// <summary>
        /// Runs the test with a short wait between attempts
        /// </summary>
        private static void WithShortRetryDelay(Action test)
        {
            var delay = WindowsSnapshot.SnapshotInProgressRetryDelay;
            WindowsSnapshot.SnapshotInProgressRetryDelay = TimeSpan.FromMilliseconds(10);
            try
            {
                test();
            }
            finally
            {
                WindowsSnapshot.SnapshotInProgressRetryDelay = delay;
            }
        }

        /// <summary>
        /// Only one shadow copy can be created at a time, so a backup that started while another
        /// program was creating one failed (#5570). It must wait and try again.
        /// </summary>
        [Test]
        public void ASnapshotIsRetriedWhileAnotherIsInProgress()
        {
            WithShortRetryDelay(() =>
            {
                var providers = new List<FakeSnapshotProvider>();
                ISnapshotProvider Factory(WindowsSnapshotProvider provider, TimeSpan timeout, Guid providerId)
                {
                    var fake = new FakeSnapshotProvider { DoSnapshotSetException = providers.Count < 2 ? SnapshotSetInProgress() : null };
                    providers.Add(fake);
                    return fake;
                }

                using (new WindowsSnapshot(Sources, Array.Empty<string>(), new Dictionary<string, string>(), false, Factory))
                {
                    Assert.AreEqual(3, providers.Count);
                    Assert.AreEqual(1, providers[0].DisposeCalls);
                    Assert.AreEqual(1, providers[1].DisposeCalls);
                    Assert.AreEqual(0, providers[2].DisposeCalls);
                }
            });
        }

        /// <summary>
        /// The retries stop when the VSS timeout has passed, with the error from VSS
        /// </summary>
        [Test]
        public void ASnapshotStillInProgressFailsAfterTheTimeout()
        {
            WithShortRetryDelay(() =>
            {
                var providers = new List<FakeSnapshotProvider>();
                ISnapshotProvider Factory(WindowsSnapshotProvider provider, TimeSpan timeout, Guid providerId)
                {
                    var fake = new FakeSnapshotProvider { DoSnapshotSetException = SnapshotSetInProgress() };
                    providers.Add(fake);
                    return fake;
                }

                var options = new Dictionary<string, string> { ["vss-timeout"] = "1s" };
                var ex = Assert.Throws<System.Runtime.InteropServices.COMException>(() =>
                    new WindowsSnapshot(Sources, Array.Empty<string>(), options, false, Factory));

                Assert.AreEqual(unchecked((int)0x80042316), ex.HResult);
                Assert.Greater(providers.Count, 1, "the snapshot was not retried");
                foreach (var provider in providers)
                    Assert.AreEqual(1, provider.DisposeCalls);
            });
        }
    }
}
