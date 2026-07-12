// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or 
// sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL 
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.WebserverCore.Services;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests for <see cref="DatabaseLockTracker"/>, which coordinates access to local
    /// backup database files between queued and immediate operations.
    /// </summary>
    [TestFixture]
    public class DatabaseLockTrackerTests
    {
        /// <summary>
        /// A simple value-returning async disposable used in tests.
        /// </summary>
        private static async Task DisposeAsync(IAsyncDisposable disposable)
            => await disposable.DisposeAsync().ConfigureAwait(false);

        [Test]
        public async Task TryAcquireReturnsLockWhenFree()
        {
            var tracker = new DatabaseLockTracker();
            var handle = tracker.TryAcquire("/tmp/test.sqlite");
            Assert.IsNotNull(handle, "TryAcquire should succeed when the database is not locked");
            await DisposeAsync(handle!);
        }

        [Test]
        public async Task TryAcquireReturnsNullWhenAlreadyLocked()
        {
            var tracker = new DatabaseLockTracker();
            var first = tracker.TryAcquire("/tmp/test.sqlite");
            Assert.IsNotNull(first, "First TryAcquire should succeed");

            var second = tracker.TryAcquire("/tmp/test.sqlite");
            Assert.IsNull(second, "Second TryAcquire should fail when the database is locked");

            await DisposeAsync(first!);
        }

        [Test]
        public async Task AcquireAsyncWaitsForExistingLock()
        {
            var tracker = new DatabaseLockTracker();
            var first = tracker.TryAcquire("/tmp/test.sqlite");
            Assert.IsNotNull(first);

            var acquireTask = tracker.AcquireAsync("/tmp/test.sqlite", CancellationToken.None);
            Assert.IsFalse(acquireTask.IsCompleted, "AcquireAsync should be waiting for the lock");

            await DisposeAsync(first!);
            var second = await acquireTask;
            Assert.IsNotNull(second, "AcquireAsync should complete after the lock is released");
            await DisposeAsync(second);
        }

        [Test]
        public async Task ReleaseAllowsSubsequentAcquire()
        {
            var tracker = new DatabaseLockTracker();
            var first = tracker.TryAcquire("/tmp/test.sqlite");
            Assert.IsNotNull(first);
            await DisposeAsync(first!);

            var second = tracker.TryAcquire("/tmp/test.sqlite");
            Assert.IsNotNull(second, "TryAcquire should succeed after the lock is released");
            await DisposeAsync(second!);
        }

        [Test]
        public async Task DifferentPathsAreIndependent()
        {
            var tracker = new DatabaseLockTracker();
            var lockA = tracker.TryAcquire("/tmp/a.sqlite");
            Assert.IsNotNull(lockA);

            var lockB = tracker.TryAcquire("/tmp/b.sqlite");
            Assert.IsNotNull(lockB, "TryAcquire on a different path should succeed independently");

            await DisposeAsync(lockA!);
            await DisposeAsync(lockB!);
        }

        [Test]
        public async Task AcquireAsyncCancelsWhenTokenFires()
        {
            var tracker = new DatabaseLockTracker();
            var first = tracker.TryAcquire("/tmp/test.sqlite");
            Assert.IsNotNull(first);

            using var cts = new CancellationTokenSource();
            var acquireTask = tracker.AcquireAsync("/tmp/test.sqlite", cts.Token);
            Assert.IsFalse(acquireTask.IsCompleted);

            cts.Cancel();

            Assert.ThrowsAsync<System.OperationCanceledException>(async () => await acquireTask);

            await DisposeAsync(first!);
        }

        [Test]
        public async Task DoubleDisposeIsSafe()
        {
            var tracker = new DatabaseLockTracker();
            var handle = tracker.TryAcquire("/tmp/test.sqlite");
            Assert.IsNotNull(handle);

            await DisposeAsync(handle!);
            await DisposeAsync(handle!);

            var next = tracker.TryAcquire("/tmp/test.sqlite");
            Assert.IsNotNull(next, "After double-dispose, the lock should be releasable exactly once");
            await DisposeAsync(next!);
        }

        [Test]
        public async Task TryAcquireOnLockedReleasesCorrectly()
        {
            var tracker = new DatabaseLockTracker();
            var first = tracker.TryAcquire("/tmp/test.sqlite");
            Assert.IsNotNull(first);

            var second = tracker.TryAcquire("/tmp/test.sqlite");
            Assert.IsNull(second);

            await DisposeAsync(first!);

            var third = tracker.TryAcquire("/tmp/test.sqlite");
            Assert.IsNotNull(third, "After releasing the first lock, TryAcquire should succeed");
            await DisposeAsync(third!);
        }

        [Test]
        public async Task RelativeAndAbsolutePathMapToSameLock()
        {
            var tracker = new DatabaseLockTracker();
            var absolute = tracker.TryAcquire(System.IO.Path.GetFullPath("/tmp/test.sqlite"));
            Assert.IsNotNull(absolute);

            var relative = tracker.TryAcquire("/tmp/test.sqlite");
            Assert.IsNull(relative, "Path normalization should map relative and absolute paths to the same lock");

            await DisposeAsync(absolute!);
        }

        [Test]
        public async Task AcquireAsyncPreservesOrder()
        {
            var tracker = new DatabaseLockTracker();
            var first = tracker.TryAcquire("/tmp/test.sqlite");
            Assert.IsNotNull(first);

            var order = new System.Collections.Generic.List<int>();

            var t2 = Task.Run(async () =>
            {
                await using var h = await tracker.AcquireAsync("/tmp/test.sqlite", CancellationToken.None);
                order.Add(2);
            });

            var t3 = Task.Run(async () =>
            {
                await using var h = await tracker.AcquireAsync("/tmp/test.sqlite", CancellationToken.None);
                order.Add(3);
            });

            await Task.Delay(50);
            order.Add(1);
            await DisposeAsync(first!);

            await Task.WhenAll(t2, t3);

            Assert.AreEqual(1, order[0], "First lock holder should complete first");
        }

        [Test]
        public void TryAcquireEmptyPathThrows()
        {
            var tracker = new DatabaseLockTracker();
            Assert.Throws<ArgumentException>(() => tracker.TryAcquire(""));
        }

        [Test]
        public void TryAcquireWhitespacePathThrows()
        {
            var tracker = new DatabaseLockTracker();
            Assert.Throws<ArgumentException>(() => tracker.TryAcquire("   "));
        }

        [Test]
        public void AcquireAsyncEmptyPathThrows()
        {
            var tracker = new DatabaseLockTracker();
            Assert.ThrowsAsync<ArgumentException>(async () => await tracker.AcquireAsync("", CancellationToken.None));
        }
    }
}
