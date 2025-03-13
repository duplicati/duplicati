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
using System.Data;

namespace Duplicati.Library.Main.Database;

public partial class DatabaseConnectionManager
{
    /// <summary>
    /// Implementation of a command object
    /// </summary>
    public class DatabaseCommand : IDisposable
    {
        /// <summary>
        /// The parent manager
        /// </summary>
        private readonly DatabaseConnectionManager m_manager;
        /// <summary>
        /// The command object
        /// </summary>
        private readonly IDbCommand m_command;

        /// <summary>
        /// Creates a new connection manager
        /// </summary>
        /// <param name="manager">The connection manager</param>
        public DatabaseCommand(DatabaseConnectionManager manager)
        {
            m_manager = manager;
            m_command = m_manager.Connection.CreateCommand();
        }

        /// <summary>
        /// Gets or sets the command text
        /// </summary>
        public string CommandText
        {
            get => m_command.CommandText;
            set => m_command.CommandText = value;
        }

        /// <summary>
        /// The parameters for the command
        /// </summary>
        public IDataParameterCollection Parameters => m_command.Parameters;

        /// <summary>
        /// Gets or sets the command timeout
        /// </summary>
        public int CommandTimeout
        {
            get => m_command.CommandTimeout;
            set => m_command.CommandTimeout = value;
        }

        /// <summary>
        /// Creates a new parameter
        /// </summary>
        /// <returns>The new parameter</returns>
        public IDbDataParameter CreateParameter()
        {
            return m_command.CreateParameter();
        }

        /// <summary>
        /// Adds a parameter to the command
        /// </summary>
        /// <param name="parameter">The parameter to add</param>
        public void AddParameter(IDbDataParameter parameter)
        {
            m_command.Parameters.Add(parameter);
        }

        /// <summary>
        /// Adds multiple parameter to the command
        /// </summary>
        /// <param name="count">The number of parameters to add</param>
        public void AddParameters(int count)
        {
            for (int i = 0; i < count; i++)
                m_command.Parameters.Add(m_command.CreateParameter());
        }

        /// <summary>
        /// Executes the command and returns the number of rows affected
        /// </summary>
        /// <returns>The number of rows affected</returns>
        public int ExecuteNonQuery()
             => m_manager.ExecuteNonQuery(m_command);

        /// <summary>
        /// Executes the command and returns the first column of the first row
        /// </summary>
        /// <returns>The first column of the first row</returns>
        public object? ExecuteScalar()
            => m_manager.ExecuteScalar(m_command);

        /// <summary>
        /// Executes the command and returns a reader
        /// </summary>
        /// <returns>The reader</returns>
        public DatabaseReader ExecuteReader()
            => m_manager.ExecuteReader(m_command);

        /// <summary>
        /// Executes the command and returns a reader that does not hold the read lock.
        /// This should only be used if there is a need to write data while enumerating a query,
        /// but beware that SQLite does not guarantee that the data read is not affected by the update.
        /// </summary>
        /// <returns>The reader</returns>
        public DatabaseReader ExecuteReaderWithoutLocks()
            => m_manager.ExecuteReaderWithoutLocks(m_command);

        /// <inheritdoc/>
        public void Dispose()
        {
            m_command.Dispose();
        }
    }
}
