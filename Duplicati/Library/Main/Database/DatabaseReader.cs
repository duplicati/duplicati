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
    public class DatabaseReader : IDisposable
    {
        /// <summary>
        /// The parent manager
        /// </summary>
        private readonly DatabaseConnectionManager m_manager;
        /// <summary>
        /// The command object
        /// </summary>
        private readonly IDataReader m_reader;

        /// <summary>
        /// The disposed flag
        /// </summary>
        private bool m_disposed = false;
        /// <summary>
        /// The release flag
        /// </summary>
        private bool m_release = false;

        /// <summary>
        /// Creates a new connection manager
        /// </summary>
        /// <param name="manager">The connection manager</param>
        /// <param name="reader">The reader object</param>
        /// <param name="release">True if the reader should be released when disposed</param>
        public DatabaseReader(DatabaseConnectionManager manager, IDataReader reader, bool release)
        {
            m_manager = manager;
            m_reader = reader;
            m_release = release;
        }

        /// <summary>
        /// Reads the next row
        /// </summary>
        /// <returns>True if a row was read, false if no more rows are available</returns>
        public bool Read()
            => m_reader.Read();

        public object GetValue(int index)
            => m_reader.GetValue(index);

        public bool IsDBNull(int index)
            => m_reader.IsDBNull(index);

        public int GetInt32(int index)
            => m_reader.GetInt32(index);

        public long GetInt64(int index)
            => m_reader.GetInt64(index);

        public string GetString(int index)
            => m_reader.GetString(index);

        public int GetValues(object[] values)
            => m_reader.GetValues(values);

        /// <inheritdoc/>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            if (m_disposed)
                return;

            m_disposed = true;
            m_reader.Dispose();
            if (m_release)
                m_manager.ReleaseReader(this);
        }

        ~DatabaseReader()
        {
            Dispose();
        }
    }
}
