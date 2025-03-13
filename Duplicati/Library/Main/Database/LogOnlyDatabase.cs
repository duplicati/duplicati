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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using static Duplicati.Library.Main.Database.DatabaseConnectionManager;


// Expose internal classes to UnitTests, so that Database classes can be tested
[assembly: InternalsVisibleTo("Duplicati.UnitTest")]

namespace Duplicati.Library.Main.Database
{
    internal class LogOnlyDatabase : IDisposable
    {
        protected readonly DatabaseConnectionManager m_manager;
        protected readonly long m_operationid = -1;

        private readonly DatabaseCommand m_insertremotelogCommand;

        /// <summary>
        /// Creates a new database instance
        /// </summary>
        public LogOnlyDatabase(DatabaseConnectionManager manager, long operationid)
        {
            m_manager = manager;
            m_insertremotelogCommand = m_manager.CreateCommand(@"INSERT INTO ""RemoteOperation"" (""OperationID"", ""Timestamp"", ""Operation"", ""Path"", ""Data"") VALUES (?, ?, ?, ?, ?)");

            m_operationid = operationid;
            if (m_operationid < 0)
            {
                // Get last operation
                using (var cmd = m_manager.CreateCommand())
                using (var rd = cmd.ExecuteReader(@"SELECT ""ID"", ""Timestamp"" FROM ""Operation"" ORDER BY ""Timestamp"" DESC LIMIT 1"))
                {
                    if (!rd.Read())
                        throw new Exception("LocalDatabase does not contain a previous operation.");
                    m_operationid = rd.GetInt64(0);
                }
            }
        }

        /// <summary>
        /// Log an operation performed on the remote backend
        /// </summary>
        /// <param name="operation">The operation performed</param>
        /// <param name="path">The path involved</param>
        /// <param name="data">Any data relating to the operation</param>
        public void LogRemoteOperation(string operation, string path, string data)
        {
            m_insertremotelogCommand.SetParameterValue(0, m_operationid);
            m_insertremotelogCommand.SetParameterValue(1, Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow));
            m_insertremotelogCommand.SetParameterValue(2, operation);
            m_insertremotelogCommand.SetParameterValue(3, path);
            m_insertremotelogCommand.SetParameterValue(4, data);
            m_insertremotelogCommand.ExecuteNonQuery();
        }

        private IEnumerable<KeyValuePair<string, string>> GetDbOptionList()
        {
            using (var cmd = m_manager.CreateCommand())
            using (var rd = cmd.ExecuteReader(@"SELECT ""Key"", ""Value"" FROM ""Configuration"" "))
                while (rd.Read())
                    yield return new KeyValuePair<string, string>(rd.GetValue(0).ToString(), rd.GetValue(1).ToString());
        }

        public IDictionary<string, string> GetDbOptions()
        {
            return GetDbOptionList().ToDictionary(x => x.Key, x => x.Value);
        }

        /// <summary>
        /// Updates a database option
        /// </summary>
        /// <param name="key">The key to update</param>
        /// <param name="value">The value to set</param>
        private void UpdateDbOption(string key, bool value)
        {
            var opts = GetDbOptions();

            if (value)
                opts[key] = "true";
            else
                opts.Remove(key);

            SetDbOptions(opts);
        }

        /// <summary>
        /// Flag indicating if a repair is in progress
        /// </summary>
        public bool RepairInProgress
        {
            get => GetDbOptions().ContainsKey("repair-in-progress");
            set => UpdateDbOption("repair-in-progress", value);
        }

        /// <summary>
        /// Flag indicating if a repair is in progress
        /// </summary>
        public bool PartiallyRecreated
        {
            get => GetDbOptions().ContainsKey("partially-recreated");
            set => UpdateDbOption("partially-recreated", value);
        }

        /// <summary>
        /// Cached value for the terminated with active uploads flag
        /// </summary>
        private bool? m_terminatedWithActiveUploadsCache;

        /// <summary>
        /// Flag indicating if the database can contain partial uploads
        /// </summary>
        public bool TerminatedWithActiveUploads
        {
            get
            {
                if (m_terminatedWithActiveUploadsCache == null)
                    m_terminatedWithActiveUploadsCache = GetDbOptions().ContainsKey("terminated-with-active-uploads");

                return m_terminatedWithActiveUploadsCache.Value;
            }
            set
            {
                if (m_terminatedWithActiveUploadsCache == value)
                    return;
                m_terminatedWithActiveUploadsCache = value;
                UpdateDbOption("terminated-with-active-uploads", value);
            }
        }

        /// <summary>
        /// Sets the database options
        /// </summary>
        /// <param name="options">The options to set</param>
        public void SetDbOptions(IDictionary<string, string> options)
        {
            using (var tr = m_manager.BeginTransaction())
            using (var cmd = m_manager.CreateCommand())
            {
                cmd.ExecuteNonQuery(@"DELETE FROM ""Configuration"" ");
                foreach (var kp in options)
                    cmd.ExecuteNonQuery(@"INSERT INTO ""Configuration"" (""Key"", ""Value"") VALUES (?, ?) ", kp.Key, kp.Value);

                tr.Commit();
            }
        }

        public void Dispose()
        {
            m_insertremotelogCommand.Dispose();
        }
    }
}
