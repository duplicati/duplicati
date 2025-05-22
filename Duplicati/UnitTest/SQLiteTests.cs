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
using System.Collections.Generic;
using System.Data;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class SQLiteTests : BasicSetupHelper
    {
        private static IDbConnection CreateDummyDatabase(string path)
        {
            var connection = SQLiteLoader.LoadConnection(path, 0);

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE TABLE TestTable (ID INTEGER PRIMARY KEY, Name TEXT)";
                command.ExecuteNonQuery();

                command.CommandText = "INSERT INTO TestTable (ID, Name) VALUES (1, 'Test1'), (2, 'Test2'), (3, 'Test3')";
                command.ExecuteNonQuery();
            }

            return connection;

        }

        [Test]
        [Category("SQLite")]
        public void TestEmptyTransaction()
        {
            using var tf = new TempFile();
            using var connection = CreateDummyDatabase(tf);

            using var t1 = connection.BeginTransaction();
            t1.Commit(); // No exception should be thrown

            using var t2 = connection.BeginTransaction();
            t2.Rollback(); // No exception should be thrown        
        }

        [Test]
        [Category("SQLite")]
        public void TestListExpansion()
        {
            using var tf = new TempFile();
            using var connection = CreateDummyDatabase(tf);
            using var rtr = new ReusableTransaction(connection);

            using (var command = connection.CreateCommand("SELECT COUNT(*) FROM TestTable WHERE ID IN (@List)"))
            {
                command.ExpandInClauseParameter("@List", new long[] { 1, 2, 3 });
                Assert.AreEqual(3, command.ExecuteScalarInt64());
            }

            using (var command = connection.CreateCommand("SELECT COUNT(*) FROM TestTable WHERE ID IN (@List)"))
            {
                command.ExpandInClauseParameter("@List", new long[] { 1, 2 });
                Assert.AreEqual(2, command.ExecuteScalarInt64());
            }

            using (var command = connection.CreateCommand("SELECT COUNT(*) FROM TestTable WHERE ID IN (@List)"))
            {
                command.ExpandInClauseParameter("@List", new long[] { 1 });
                Assert.AreEqual(1, command.ExecuteScalarInt64());
            }

            using (var command = connection.CreateCommand("SELECT COUNT(*) FROM TestTable WHERE ID IN (@List)"))
            {
                command.ExpandInClauseParameter("@List", new long[0]);
                Assert.AreEqual(0, command.ExecuteScalarInt64());
            }

            var list = new List<long>();
            for (var i = 0; i < 1000; i++)
                list.Add(i);

            using (var command = connection.CreateCommand("SELECT COUNT(*) FROM TestTable WHERE ID IN (@List)"))
            {
                using var tmplist = await TemporaryDbValueList.CreateAsync(connection, rtr, list);
                command.ExpandInClauseParameter("@List", list);
                Assert.AreEqual(3, command.ExecuteScalarInt64());
            }

            using (var command = connection.CreateCommand("SELECT COUNT(*) FROM TestTable WHERE ID IN (@List)"))
            {
                list.Remove(1);
                list.Remove(2);

                using var tmplist = await TemporaryDbValueList.CreateAsync(connection, rtr, list);
                command.ExpandInClauseParameter("@List", list);
                Assert.AreEqual(1, command.ExecuteScalarInt64());
            }
        }
    }
}
