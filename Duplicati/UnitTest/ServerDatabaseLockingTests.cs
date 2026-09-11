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
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Server.Database;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The server keeps one database connection and serializes access to it with a single lock.
    /// A path that skips the lock breaks the ones that take it: while a transaction is open on
    /// the connection, Microsoft.Data.Sqlite refuses any command that does not carry that same
    /// transaction, and the read helpers in <see cref="Connection"/> carry none. The reads then
    /// fail with "The transaction object is not associated with the same connection object as
    /// this command", which is what a REST request returned as a 500 during a CI run.
    /// </summary>
    [TestFixture]
    [Category("ServerDatabase")]
    public class ServerDatabaseLockingTests
    {
        /// <summary>How long a call is given to prove it is not blocked.</summary>
        private const int BlockedMilliseconds = 500;

        /// <summary>How long a call is given to finish once it is no longer blocked.</summary>
        private const int CompletionMilliseconds = 15000;

        private string _tempDataFolder = null!;
        private string _databasePath = null!;
        private Connection _connection = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-server-db-lock-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDataFolder);

            _databasePath = Path.Combine(_tempDataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);

            var dbConnection = await SQLiteLoader.LoadConnectionAsync(_databasePath);
            DatabaseUpgrader.UpgradeDatabase(dbConnection, _databasePath, typeof(Library.RestAPI.Database.DatabaseSchemaMarker));

            _connection = new Connection(dbConnection, true, null, _tempDataFolder, () => { });
        }

        [TearDown]
        public void TearDown()
        {
            _connection?.Dispose();

            if (Directory.Exists(_tempDataFolder))
            {
                try
                {
                    Directory.Delete(_tempDataFolder, true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        /// <summary>
        /// Runs <paramref name="call"/> on another thread while the database lock is held, and
        /// asserts that it waits for the lock rather than reaching the connection anyway.
        /// </summary>
        /// <param name="name">The name of the call, for the failure message.</param>
        /// <param name="call">The call to make.</param>
        private void AssertWaitsForTheDatabaseLock(string name, Action call)
        {
            var entered = new ManualResetEventSlim(false);
            Task worker;

            lock (_connection.m_lock)
            {
                worker = Task.Run(() =>
                {
                    entered.Set();
                    call();
                });

                Assert.IsTrue(entered.Wait(CompletionMilliseconds), $"{name} never started");
                Assert.IsFalse(worker.Wait(BlockedMilliseconds), $"{name} used the connection while the database lock was held by another thread");
            }

            Assert.IsTrue(worker.Wait(CompletionMilliseconds), $"{name} did not finish after the database lock was released");
            Assert.IsNull(worker.Exception, $"{name} failed: {worker.Exception}");
        }

        /// <summary>
        /// The log purge runs from a timer (Program.cs), so it is concurrent with every REST
        /// request. It opens a transaction on the shared connection, which is exactly the state
        /// the reads cannot survive.
        /// </summary>
        [Test]
        public void ThePurgeOfLogDataWaitsForTheDatabaseLock()
            => AssertWaitsForTheDatabaseLock("PurgeLogData", () => _connection.PurgeLogData(DateTime.Now));

        /// <summary>
        /// Registering a temporary file runs on the queue runner's worker thread, and writes
        /// without a transaction - but it reads the row id back in a second statement, so it
        /// needs the lock for its own sake as much as for everyone else's.
        /// </summary>
        [Test]
        public void RegisteringATempFileWaitsForTheDatabaseLock()
            => AssertWaitsForTheDatabaseLock("RegisterTempFile", () => _connection.RegisterTempFile("unittest", Path.Combine(_tempDataFolder, "tempfile.txt"), DateTime.Now.AddDays(1)));

        /// <summary>
        /// The symptom itself: a reader and the log purge running at the same time, which is what
        /// a timer tick during a REST request amounts to. Without the lock the reader fails with
        /// "The transaction object is not associated with the same connection object as this
        /// command" the first time it lands inside the purge's transaction.
        /// </summary>
        [Test]
        public void ReadingWhileTheLogIsPurgedReportsNoError()
        {
            for (var i = 0; i < 5; i++)
                _connection.LogError(null, "unittest", new Exception("unittest"));

            var errors = new ConcurrentQueue<Exception>();
            var deadline = DateTime.UtcNow.AddSeconds(2);

            void Hammer(Action call)
            {
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        call();
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue(ex);
                        return;
                    }
                }
            }

            var purging = Task.Run(() => Hammer(() => _connection.PurgeLogData(DateTime.Now)));
            var reading = Task.Run(() => Hammer(() =>
            {
                _ = _connection.Backups;
                _ = _connection.GetNotifications();
            }));

            Assert.IsTrue(Task.WaitAll([purging, reading], CompletionMilliseconds), "the workers did not finish");
            Assert.IsEmpty(errors, $"reading while the log was purged failed: {string.Join(Environment.NewLine, errors.Select(x => x.Message))}");
        }
    }
}
