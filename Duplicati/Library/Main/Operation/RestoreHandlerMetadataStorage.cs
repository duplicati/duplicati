//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
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
using System;using System.IO;using Duplicati.Library.Utility;using System.Collections.Generic;
namespace Duplicati.Library.Main.Operation
{
    public class RestoreHandlerMetadataStorage : IDisposable
    {        private TempFile m_temp;        private FileStream m_stream;        private long m_entries;        private long m_filepos;
        public RestoreHandlerMetadataStorage()
        {            m_temp = new TempFile();            m_stream = File.Open(m_temp, FileMode.Truncate, FileAccess.ReadWrite, FileShare.None);
        }        public void Add(string path, Stream data)        {            var datalen = data.Length;            if (datalen > Int32.MaxValue)                throw new ArgumentOutOfRangeException("Metadata is larger than int32");            var pathbytes = System.Text.Encoding.UTF8.GetBytes(path);            var pathlen = BitConverter.GetBytes(pathbytes.LongLength);            var entrylen = BitConverter.GetBytes(datalen);            var totalsize = pathbytes.Length + pathlen.Length + entrylen.Length + datalen;            m_stream.Position = m_filepos;            m_stream.Write(pathlen, 0, pathlen.Length);            m_stream.Write(pathbytes, 0, pathbytes.Length);            m_stream.Write(entrylen, 0, entrylen.Length);            data.CopyTo(m_stream);            if (m_stream.Position != m_filepos + totalsize)                throw new Exception("Bad file write!");            m_filepos += totalsize;            m_entries++;        }        private void CheckedRead(byte[] buffer, int offset, int count)        {            int r;            while (count > 0 && (r = m_stream.Read(buffer, offset, count)) > 0)            {                offset += r;                count -= r;            }            if (count != 0)                throw new Exception("Bad file read");        }        public IEnumerable<KeyValuePair<string, Stream>> Records        {            get            {                long pos = 0;                var bf = BitConverter.GetBytes(0L);                var buf = new byte[8 * 1024];                Logging.Log.WriteMessage(string.Format("The metadata storage file has {0} entries and takes up {1}", m_entries, Library.Utility.Utility.FormatSizeString(m_stream.Length)), Duplicati.Library.Logging.LogMessageType.Profiling);                using(new Logging.Timer("Read metadata from file"))                    for(var e = 0L; e < m_entries; e++)                    {                        m_stream.Position = pos;                        CheckedRead(bf, 0, bf.Length);                        var stringlen = BitConverter.ToInt64(bf, 0);                        var strbuf = stringlen > buf.Length ? new byte[stringlen] : buf;                        CheckedRead(strbuf, 0, (int)stringlen);                        var path = System.Text.Encoding.UTF8.GetString(strbuf, 0, (int)stringlen);                        CheckedRead(bf, 0, bf.Length);                        var datalen = BitConverter.ToInt64(bf, 0);                        if (datalen > Int32.MaxValue)                            throw new ArgumentOutOfRangeException("Metadata is larger than int32");                        var databuf = datalen > buf.Length ? new byte[datalen] : buf;                        CheckedRead(databuf, 0, (int)datalen);                        pos += datalen + stringlen + bf.Length + bf.Length;                        yield return new KeyValuePair<string, Stream>(path, new MemoryStream(databuf, 0, (int)datalen));                    }            }        }
        #region IDisposable implementation        public void Dispose()        {            if (m_stream != null)                try { m_stream.Dispose(); }                catch { }                finally { m_stream = null; }            if (m_temp != null)                try { m_temp.Dispose(); }                catch { }                finally { m_temp = null; }        }        #endregion    }
}

