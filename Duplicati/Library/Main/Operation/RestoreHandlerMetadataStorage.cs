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
using System.IO;
using Duplicati.Library.Utility;
using System.Collections.Generic;
namespace Duplicati.Library.Main.Operation
{
    public class RestoreHandlerMetadataStorage : IDisposable
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<RestoreHandlerMetadataStorage>();
        private TempFile m_temp;
        private FileStream m_stream;
        private long m_entries;

        private long m_filepos;
        public RestoreHandlerMetadataStorage()
        {
            m_temp = new TempFile();
            m_stream = File.Open(m_temp, FileMode.Truncate, FileAccess.ReadWrite, FileShare.None);
        }

        public void Add(string path, Stream data)
        {
            var datalen = data.Length;

            if (datalen > Int32.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(datalen), "Metadata is larger than int32");

            var pathbytes = System.Text.Encoding.UTF8.GetBytes(path);
            var pathlen = BitConverter.GetBytes(pathbytes.LongLength);
            var entrylen = BitConverter.GetBytes(datalen);

            var totalsize = pathbytes.Length + pathlen.Length + entrylen.Length + datalen;

            m_stream.Position = m_filepos;
            m_stream.Write(pathlen, 0, pathlen.Length);
            m_stream.Write(pathbytes, 0, pathbytes.Length);
            m_stream.Write(entrylen, 0, entrylen.Length);
            data.CopyTo(m_stream);


            if (m_stream.Position != m_filepos + totalsize)
                throw new Exception("Bad file write!");

            m_filepos += totalsize;
            m_entries++;
        }

        private void CheckedRead(byte[] buffer, int bytesToRead)
        {
            if (Duplicati.Library.Utility.Utility.ForceStreamRead(m_stream, buffer, bytesToRead) != bytesToRead)
            {
                throw new Exception("Bad file read");
            }
        }

        public IEnumerable<KeyValuePair<string, Stream>> Records
        {
            get
            {
                long pos = 0;
                var bf = BitConverter.GetBytes(0L);
                var buf = new byte[8 * 1024];

                Logging.Log.WriteProfilingMessage(LOGTAG, "MetadataStorageSize", "The metadata storage file has {0} entries and takes up {1}", m_entries, Library.Utility.Utility.FormatSizeString(m_stream.Length));

                using(new Logging.Timer(LOGTAG, "ReadMetadata", "Read metadata from file"))
                    for(var e = 0L; e < m_entries; e++)
                    {
                        m_stream.Position = pos;
                        CheckedRead(bf, bf.Length);
                        var stringlen = BitConverter.ToInt64(bf, 0);

                        var strbuf = stringlen > buf.Length ? new byte[stringlen] : buf;
                        CheckedRead(strbuf, (int)stringlen);
                        var path = System.Text.Encoding.UTF8.GetString(strbuf, 0, (int)stringlen);

                        CheckedRead(bf, bf.Length);
                        var datalen = BitConverter.ToInt64(bf, 0);
                        if (datalen > Int32.MaxValue)
                            throw new ArgumentOutOfRangeException(nameof(datalen), "Metadata is larger than int32");

                        var databuf = datalen > buf.Length ? new byte[datalen] : buf;
                        CheckedRead(databuf, (int)datalen);

                        pos += datalen + stringlen + bf.Length + bf.Length;

                        yield return new KeyValuePair<string, Stream>(path, new MemoryStream(databuf, 0, (int)datalen));

                    }
            }
        }

        #region IDisposable implementation

        public void Dispose()
        {
            if (m_stream != null)
                try { m_stream.Dispose(); }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "DisposeError", ex, "Failed to dispose stream"); }
                finally { m_stream = null; }

            if (m_temp != null)
                try { m_temp.Dispose(); }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "DisposeError", ex, "Failed to temp file stream"); }
                finally { m_temp = null; }

        }

        #endregion
    }
}
