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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class SQLiteTests : BasicSetupHelper
    {
        private static async Task<SqliteConnection> CreateDummyDatabaseAsync(string path)
        {
            var connection = await SQLiteLoader.LoadConnectionAsync(path);

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE TABLE TestTable (ID INTEGER PRIMARY KEY, Name TEXT)";
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);

                command.CommandText = "INSERT INTO TestTable (ID, Name) VALUES (1, 'Test1'), (2, 'Test2'), (3, 'Test3')";
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            return connection;

        }

        [Test]
        [Category("SQLite")]
        public async Task TestEmptyTransactionAsync()
        {
            using var tf = new TempFile();
            using var connection = await CreateDummyDatabaseAsync(tf)
                .ConfigureAwait(false);

            using var t1 = connection.BeginTransaction();
            await t1.CommitAsync().ConfigureAwait(false); // No exception should be thrown

            using var t2 = connection.BeginTransaction();
            await t2.RollbackAsync().ConfigureAwait(false); // No exception should be thrown
        }

        [Test]
        [Category("SQLite")]
        public async Task TestListExpansionAsync()
        {
            using var tf = new TempFile();
            using var connection = await CreateDummyDatabaseAsync(tf).ConfigureAwait(false);
            using var rtr = new ReusableTransaction(connection);

            using (var command = connection.CreateCommand("SELECT COUNT(*) FROM TestTable WHERE ID IN (@List)"))
            {
                command.ExpandInClauseParameter("@List", new long[] { 1, 2, 3 });
                Assert.AreEqual(3, await command.ExecuteScalarInt64Async(CancellationToken.None).ConfigureAwait(false));
            }

            using (var command = connection.CreateCommand("SELECT COUNT(*) FROM TestTable WHERE ID IN (@List)"))
            {
                command.ExpandInClauseParameter("@List", new long[] { 1, 2 });
                Assert.AreEqual(2, await command.ExecuteScalarInt64Async(CancellationToken.None).ConfigureAwait(false));
            }

            using (var command = connection.CreateCommand("SELECT COUNT(*) FROM TestTable WHERE ID IN (@List)"))
            {
                command.ExpandInClauseParameter("@List", new long[] { 1 });
                Assert.AreEqual(1, await command.ExecuteScalarInt64Async(CancellationToken.None).ConfigureAwait(false));
            }

            using (var command = connection.CreateCommand("SELECT COUNT(*) FROM TestTable WHERE ID IN (@List)"))
            {
                command.ExpandInClauseParameter("@List", new long[0]);
                Assert.AreEqual(0, await command.ExecuteScalarInt64Async(CancellationToken.None).ConfigureAwait(false));
            }

            var list = new List<long>();
            for (var i = 0; i < 1000; i++)
                list.Add(i);

            using (var command = connection.CreateCommand("SELECT COUNT(*) FROM TestTable WHERE ID IN (@List)"))
            {
                using var tmplist = await TemporaryDbValueList.CreateAsync(connection, rtr, list, CancellationToken.None)
                    .ConfigureAwait(false);
                command.ExpandInClauseParameter("@List", list);
                Assert.AreEqual(3, await command.ExecuteScalarInt64Async(CancellationToken.None).ConfigureAwait(false));
            }

            using (var command = connection.CreateCommand("SELECT COUNT(*) FROM TestTable WHERE ID IN (@List)"))
            {
                list.Remove(1);
                list.Remove(2);

                using var tmplist = await TemporaryDbValueList.CreateAsync(connection, rtr, list, CancellationToken.None)
                    .ConfigureAwait(false);
                command.ExpandInClauseParameter("@List", list);
                Assert.AreEqual(1, await command.ExecuteScalarInt64Async(CancellationToken.None).ConfigureAwait(false));
            }
        }

        /// <summary>
        /// Registers a function that takes longer than the monitor threshold. It is not
        /// deterministic so it cannot be folded away while the statement is prepared, and
        /// because the reader steps once while it is being created, the wait lands inside the
        /// call under test rather than in the read loop.
        /// </summary>
        private static void AddSlowFunction(SqliteConnection connection, TimeSpan duration)
            => connection.CreateFunction<long>("slow_marker", () => { Thread.Sleep(duration); return 1L; }, isDeterministic: false);

        /// <summary>
        /// Collects the warnings written while the body runs.
        /// </summary>
        /// <remarks>
        /// The scope has to be established before the monitor is started: the log scope is held
        /// in an <see cref="System.Threading.AsyncLocal{T}"/> and the monitor reports from a
        /// <see cref="Timer"/> callback, which captures the execution context when it is created.
        /// Controller does it in this order for the same reason. The queue is concurrent because
        /// the timer thread writes while the test thread reads.
        /// </remarks>
        private static async Task<List<Library.Logging.LogEntry>> WarningsWhileAsync(TimeSpan threshold, Func<Task> body)
        {
            var captured = new System.Collections.Concurrent.ConcurrentQueue<Library.Logging.LogEntry>();
            using (Library.Logging.Log.StartScope(e => captured.Enqueue(e)))
            {
                using var monitor = SlowQueryMonitor.StartMonitoring(threshold);

                // A threshold below one second is read as a request to disable monitoring, so
                // this also guards against the positive test passing quietly.
                Assert.IsNotNull(monitor, "Slow query monitoring was not started");

                await body().ConfigureAwait(false);
            }

            return captured.Where(e => e.Level == Library.Logging.LogMessageType.Warning && e.Id == "SlowQueryDetected").ToList();
        }

        [Test]
        [Category("SQLite")]
        public async Task ASlowReaderIsReportedAsync()
        {
            using var tf = new TempFile();
            using var connection = await CreateDummyDatabaseAsync(tf).ConfigureAwait(false);
            AddSlowFunction(connection, TimeSpan.FromSeconds(2.5));

            var warnings = await WarningsWhileAsync(TimeSpan.FromSeconds(1), async () =>
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT slow_marker()";
                await using var rd = await cmd.ExecuteReaderAsync(writeLog: false, CancellationToken.None).ConfigureAwait(false);
            }).ConfigureAwait(false);

            Assert.IsNotEmpty(warnings, "The query ran for longer than the threshold but was not reported");
            Assert.IsTrue(warnings[0].FormattedMessage.Contains("slow_marker"), warnings[0].FormattedMessage);
        }

        [Test]
        [Category("SQLite")]
        public async Task AReaderRunThroughTheCommandsOwnMethodIsNotReportedAsync()
        {
            using var tf = new TempFile();
            using var connection = await CreateDummyDatabaseAsync(tf).ConfigureAwait(false);
            AddSlowFunction(connection, TimeSpan.FromSeconds(2.0));

            var warnings = await WarningsWhileAsync(TimeSpan.FromSeconds(1), async () =>
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT slow_marker()";
                // Deliberately the shape the rest of the codebase must not use. It binds to the
                // command's own method rather than to the extension, so the monitor never sees
                // it. This stays true even if a (SqliteCommand, CancellationToken) extension is
                // added later, because an instance method wins over an extension method.
                await using var rd = await cmd.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
            }).ConfigureAwait(false);

            Assert.IsEmpty(warnings, "The query was reported, so the shape is no longer the unmonitored one");
        }
    }
}
