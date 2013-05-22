//  Copyright (C) 2011, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;
using System.IO;

namespace Duplicati.Library.Utility
{
	/// <summary>
	/// Represents an enumerable list that can be appended to.
	/// If the use of the list exceeds the threshold, the list will
	/// switch from memory based to storage to file based storage.
	/// Typical usage of this list is for storing log messages,
	/// that occasionally grows and produces out-of-memory errors
	/// </summary>
	public class FileBackedList<T> : IEnumerable<T>, IDisposable
	{
        private class StreamEnumerator : IEnumerator<T>, System.Collections.IEnumerator
        {
            private Stream m_stream;
            private Func<Stream, long, T> m_deserialize;
            private long m_position;
            private readonly byte[] m_sizebuffer;
            private long m_expectedCount;
            private FileBackedList<T> m_parent;

            public StreamEnumerator(Stream stream, Func<Stream, long, T> deserialize, FileBackedList<T> parent)
            {
                m_stream = stream;
                m_deserialize = deserialize;
                m_sizebuffer = new byte[8];
                m_parent = parent;
                m_expectedCount = m_parent.Count;
                this.Reset();
            }

            public T Current
			{
				get
				{
					if (m_position < 0 || m_position >= m_stream.Length)
						return default(T);
					if (m_expectedCount != m_parent.Count)
						throw new Exception("Collection modified");
					m_stream.Position = m_position;
					m_stream.Read(m_sizebuffer, 0, m_sizebuffer.Length);					
					return m_deserialize(m_stream, BitConverter.ToInt64(m_sizebuffer, 0)); 
				}
            }

            public void Dispose()
            {
                m_stream = null;
            }

            object System.Collections.IEnumerator.Current
            {
                get { return this.Current; }
            }

            public bool MoveNext()
			{
				if (m_position >= m_stream.Length)
					return false;
					
				if (m_expectedCount != m_parent.Count)
					throw new Exception("Collection modified");
					
				if (m_position < 0)
				{
					m_position = 0;
					return m_expectedCount > 0;
				}
				else
				{
					m_stream.Position = m_position;
					m_stream.Read(m_sizebuffer, 0, m_sizebuffer.Length);
					m_position += m_sizebuffer.Length + BitConverter.ToInt64(m_sizebuffer, 0);
					return m_position < m_stream.Length;
				}				
            }

            public void Reset()
            {
                m_position = -1;
            }
        }

        private Library.Utility.TempFile m_file;
        private Stream m_stream;
        private long m_count;
        
        private Func<T, long> m_getSize;
        private Action<T, System.IO.Stream> m_serialize;
        private Func<System.IO.Stream, long, T> m_deserialize;
        
		public bool IsFileBacked { get { return !(m_stream is MemoryStream); } }
		public long SwitchToFileLimit { get; set; }
		
		public FileBackedList(Func<T, long> getSize, Action<T, Stream> serialize, Func<Stream, long, T> deserialize)
		{
            m_file = null;
            m_stream = new MemoryStream();
            m_count = 0;
            
            m_getSize = getSize;
            m_serialize = serialize;
            m_deserialize = deserialize;
            
            this.SwitchToFileLimit = 10 * 1024 * 1024;
		}

        public void Add(T value)
        {
        	long size = m_getSize(value);
            if (m_stream is MemoryStream && (m_stream.Length + size) > this.SwitchToFileLimit)
            {
                m_file = new Library.Utility.TempFile();
                Stream fs = System.IO.File.OpenWrite(m_file);
                m_stream.CopyTo(fs);
                m_stream.Dispose();

                m_stream = fs;
            }
            
        	m_stream.Write(BitConverter.GetBytes(size), 0, 8);
        	m_serialize(value, m_stream);

            m_count++;
        }

        public long Count { get { return m_count; } }

        public void Dispose()
        {
            if (m_stream != null)
            {
                m_stream.Dispose();
                m_stream = null;
            }

            if (m_file != null)
            {
                m_file.Dispose();
                m_file = null;
            }
        }		

        #region IEnumerable implementation

		public IEnumerator<T> GetEnumerator()
		{
            return new StreamEnumerator(m_stream, m_deserialize, this);
		}

		#endregion

		#region IEnumerable implementation

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return this.GetEnumerator();
		}

		#endregion
	}
	
	/// <summary>
	/// Represents an enumerable list that can be appended to.
	/// If the use of the list exceeds the threshold, the list will
	/// switch from memory based to storage to file based storage.
	/// Typical usage of this list is for storing log messages,
	/// that occasionally grows and produces out-of-memory errors
	/// </summary>
	public class FileBackedStringList : FileBackedList<string>
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="Duplicati.Library.Utility.FileBackedStringList"/> class.
		/// </summary>
		/// <param name="encoding">The text encoding to use, defaults to UTF8</param>
		public FileBackedStringList(System.Text.Encoding encoding = null)
			: base(
			(value) => (encoding ?? System.Text.Encoding.UTF8).GetByteCount(value),
			(value, stream) => {
				var data = (encoding ?? System.Text.Encoding.UTF8).GetBytes(value);
				stream.Write(data, 0, data.Length);
			}, 
			(stream, length) => {
				var buf = new byte[length];
				Utility.ForceStreamRead(stream, buf, buf.Length);
				return (encoding ?? System.Text.Encoding.UTF8).GetString(buf, 0, buf.Length);
			})
		{
		} 
	}
}

