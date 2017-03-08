// ZipStorer, by Jaime Olivares
// Website: http://github.com/jaime-olivares/zipstorer
// Version: 3.2.0 (January 20, 2017)

using System.Collections.Generic;
using System.Text;

namespace System.IO.Compression
{
    #if NETSTANDARD
    /// <summary>
    /// Extension method for covering missing Close() method in .Net Standard
    /// </summary>
    public static class StreamExtension
    {
        public static void Close(this Stream stream)
        {
            stream.Dispose(); 
            GC.SuppressFinalize(stream);
        }
    }
    #endif

    /// <summary>
    /// Unique class for compression/decompression file. Represents a Zip file.
    /// </summary>
    public class ZipStorer : IDisposable
    {
        /// <summary>
        /// Compression method enumeration
        /// </summary>
        public enum Compression : ushort 
        { 
            /// <summary>Uncompressed storage</summary> 
            Store = 0,
            /// <summary>Deflate compression method</summary>
            Deflate = 8 
        }

        /// <summary>
        /// Represents an entry in Zip file directory
        /// </summary>
        public struct ZipFileEntry
        {
            /// <summary>Compression method</summary>
            public Compression Method; 
            /// <summary>Full path and filename as stored in Zip</summary>
            public string FilenameInZip;
            /// <summary>Original file size</summary>
            public uint FileSize;
            /// <summary>Compressed file size</summary>
            public uint CompressedSize;
            /// <summary>Offset of header information inside Zip storage</summary>
            public uint HeaderOffset;
            /// <summary>Offset of file inside Zip storage</summary>
            public uint FileOffset;
            /// <summary>Size of header information</summary>
            public uint HeaderSize;
            /// <summary>32-bit checksum of entire file</summary>
            public uint Crc32;
            /// <summary>Last modification time of file</summary>
            public DateTime ModifyTime;
            /// <summary>User comment for file</summary>
            public string Comment;
            /// <summary>True if UTF8 encoding for filename and comments, false if default (CP 437)</summary>
            public bool EncodeUTF8;

            /// <summary>Overriden method</summary>
            /// <returns>Filename in Zip</returns>
            public override string ToString()
            {
                return this.FilenameInZip;
            }
        }

        #region Public fields
        /// <summary>True if UTF8 encoding for filename and comments, false if default (CP 437)</summary>
        public bool EncodeUTF8 = false;
        /// <summary>Force deflate algotithm even if it inflates the stored file. Off by default.</summary>
        public bool ForceDeflating = false;
        #endregion

        #region Private fields
        // List of files to store
        private List<ZipFileEntry> Files = new List<ZipFileEntry>();
        // Filename of storage file
        private string FileName;
        // Stream object of storage file
        private Stream ZipFileStream;
        // General comment
        private string Comment = "";
        // Central dir image
        private byte[] CentralDirImage = null;
        // Existing files in zip
        private ushort ExistingFiles = 0;
        // File access for Open method
        private FileAccess Access;
        // Static CRC32 Table
        private static readonly UInt32[] CrcTable = null;
        // Default filename encoder
        private static Encoding DefaultEncoding = Encoding.GetEncoding(437);
        #endregion

        #region Public methods
        // Static constructor. Just invoked once in order to create the CRC32 lookup table.
        static ZipStorer()
        {
            // Generate CRC32 table
            CrcTable = new UInt32[256];
            for (int i = 0; i < CrcTable.Length; i++)
            {
                UInt32 c = (UInt32)i;
                for (int j = 0; j < 8; j++)
                {
                    if ((c & 1) != 0)
                        c = 3988292384 ^ (c >> 1);
                    else
                        c >>= 1;
                }
                CrcTable[i] = c;
            }
        }
        /// <summary>
        /// Method to create a new storage file
        /// </summary>
        /// <param name="_filename">Full path of Zip file to create</param>
        /// <param name="_comment">General comment for Zip file</param>
        /// <returns>A valid ZipStorer object</returns>
        public static ZipStorer Create(string _filename, string _comment)
        {
            Stream stream = new FileStream(_filename, FileMode.Create, FileAccess.ReadWrite);

            ZipStorer zip = Create(stream, _comment);
            zip.Comment = _comment;
            zip.FileName = _filename;

            return zip;
        }
        /// <summary>
        /// Method to create a new zip storage in a stream
        /// </summary>
        /// <param name="_stream"></param>
        /// <param name="_comment"></param>
        /// <returns>A valid ZipStorer object</returns>
        public static ZipStorer Create(Stream _stream, string _comment)
        {
            ZipStorer zip = new ZipStorer();
            zip.Comment = _comment;
            zip.ZipFileStream = _stream;
            zip.Access = FileAccess.Write;

            return zip;
        }
        /// <summary>
        /// Method to open an existing storage file
        /// </summary>
        /// <param name="_filename">Full path of Zip file to open</param>
        /// <param name="_access">File access mode as used in FileStream constructor</param>
        /// <returns>A valid ZipStorer object</returns>
        public static ZipStorer Open(string _filename, FileAccess _access)
        {
            Stream stream = (Stream)new FileStream(_filename, FileMode.Open, _access == FileAccess.Read ? FileAccess.Read : FileAccess.ReadWrite);

            ZipStorer zip = Open(stream, _access);
            zip.FileName = _filename;

            return zip;
        }
        /// <summary>
        /// Method to open an existing storage from stream
        /// </summary>
        /// <param name="_stream">Already opened stream with zip contents</param>
        /// <param name="_access">File access mode for stream operations</param>
        /// <returns>A valid ZipStorer object</returns>
        public static ZipStorer Open(Stream _stream, FileAccess _access)
        {
            if (!_stream.CanSeek && _access != FileAccess.Read)
                throw new InvalidOperationException("Stream cannot seek");

            ZipStorer zip = new ZipStorer();
            //zip.FileName = _filename;
            zip.ZipFileStream = _stream;
            zip.Access = _access;

            if (zip.ReadFileInfo())
                return zip;

            throw new System.IO.InvalidDataException();
        }
        /// <summary>
        /// Add full contents of a file into the Zip storage
        /// </summary>
        /// <param name="_method">Compression method</param>
        /// <param name="_pathname">Full path of file to add to Zip storage</param>
        /// <param name="_filenameInZip">Filename and path as desired in Zip directory</param>
        /// <param name="_comment">Comment for stored file</param>        
        public void AddFile(Compression _method, string _pathname, string _filenameInZip, string _comment)
        {
            if (Access == FileAccess.Read)
                throw new InvalidOperationException("Writing is not alowed");

            using (var fs = new FileStream(_pathname, FileMode.Open, FileAccess.Read))
                AddStream(_method, _filenameInZip, fs, File.GetLastWriteTime(_pathname), _comment);
        }
        /// <summary>
        /// Add full contents of a stream into the Zip storage
        /// </summary>
        /// <param name="_method">Compression method</param>
        /// <param name="_filenameInZip">Filename and path as desired in Zip directory</param>
        /// <param name="_source">Stream object containing the data to store in Zip</param>
        /// <param name="_modTime">Modification time of the data to store</param>
        /// <param name="_comment">Comment for stored file</param>
        public void AddStream(Compression _method, string _filenameInZip, Stream _source, DateTime _modTime, string _comment)
        {
            var posStart = this.ZipFileStream.Position;
            var sourceStart = _source.CanSeek ? _source.Position : 0;

            using (var tg = Add(_method == Compression.Store ? CompressionLevel.NoCompression : CompressionLevel.Optimal, _filenameInZip, _modTime, _comment))
                _source.CopyTo(tg);

            var zfe = Files[Files.Count - 1];

            // Verify for real compression
            if (zfe.Method == Compression.Deflate && !this.ForceDeflating && _source.CanSeek && zfe.CompressedSize > zfe.FileSize)
            {
                // Start operation again with Store algorithm
                this.ZipFileStream.Position = posStart;
                this.ZipFileStream.SetLength(posStart);
                _source.Position = sourceStart;
                Files.RemoveAt(Files.Count - 1);

                using (var tg = Add(CompressionLevel.NoCompression, _filenameInZip, _modTime, _comment))
                    _source.CopyTo(tg);
            }
        }
        /// <summary>
        /// returns a writeable stream where the file contents can be written to
        /// </summary>
        /// <param name="_level">The compression level</param>
        /// <param name="_filenameInZip">Filename and path as desired in Zip directory</param>
        /// <param name="_modTime">Modification time of the data to store</param>
        /// <param name="_comment">Comment for stored file</param>
        /// <returns>A writeable stream</returns>
        public Stream Add(CompressionLevel _level, string _filenameInZip, DateTime _modTime, string _comment)
        {
            if (Access == FileAccess.Read)
                throw new InvalidOperationException("Writing is not alowed");

            // Prepare the fileinfo
            var zfe = new ZipFileEntry()
            {
                Method = _level == CompressionLevel.NoCompression ? Compression.Store : Compression.Deflate,
                EncodeUTF8 = this.EncodeUTF8,
                FilenameInZip = NormalizedFilename(_filenameInZip),
                Comment = _comment ?? "",
                Crc32 = 0,  // to be updated later
                HeaderOffset = (uint)this.ZipFileStream.Position,  // offset within file of the start of this local record
                ModifyTime = _modTime
            };

            // Write local header
            WriteLocalHeader(ref zfe);
            zfe.FileOffset = (uint)this.ZipFileStream.Position;

            // Select the destination
            var target =
                zfe.Method == Compression.Store
                ? this.ZipFileStream
                : new DeflateStream(this.ZipFileStream, _level, true);

            // Write file to zip (store)
            return new Crc32CalculatingStream(target, self =>
            {
                if (target != this.ZipFileStream)
                    target.Close();

                zfe.Crc32 = self.Crc32;
                zfe.FileSize = (uint)self.Length;
                zfe.CompressedSize = (uint)this.ZipFileStream.Position - zfe.FileOffset;

                this.UpdateCrcAndSizes(ref zfe);
                Files.Add(zfe);
            });
        }
        /// <summary>
        /// Updates central directory (if pertinent) and close the Zip storage
        /// </summary>
        /// <remarks>This is a required step, unless automatic dispose is used</remarks>
        public void Close()
        {
            if (this.Access != FileAccess.Read)
            {
                uint centralOffset = (uint)this.ZipFileStream.Position;
                uint centralSize = 0;

                if (this.CentralDirImage != null)
                    this.ZipFileStream.Write(CentralDirImage, 0, CentralDirImage.Length);

                for (int i = 0; i < Files.Count; i++)
                {
                    long pos = this.ZipFileStream.Position;
                    this.WriteCentralDirRecord(Files[i]);
                    centralSize += (uint)(this.ZipFileStream.Position - pos);
                }

                if (this.CentralDirImage != null)
                    this.WriteEndRecord(centralSize + (uint)CentralDirImage.Length, centralOffset);
                else
                    this.WriteEndRecord(centralSize, centralOffset);
            }

            if (this.ZipFileStream != null)
            {
                this.ZipFileStream.Flush();
                this.ZipFileStream.Dispose();
                this.ZipFileStream = null;
            }
        }
        /// <summary>
        /// Reads all streams in a forward-only manner
        /// </summary>
        /// <returns>A sequence of all items.</returns>
        public IEnumerable<ZipFileEntry> ForwardOnlyAccess()
        {
            var pos = this.ZipFileStream.Position = 0;
            while (true)
            {
                var zfe = ReadLocalHeader();
                yield return zfe;

                this.ZipFileStream.Position = zfe.FileOffset + zfe.CompressedSize;
            }
        }

        /// <summary>
        /// Read all the file records in the central directory 
        /// </summary>
        /// <returns>List of all entries in directory</returns>
        public List<ZipFileEntry> ReadCentralDir()
        {
            if (this.CentralDirImage == null)
                throw new InvalidOperationException("Central directory currently does not exist");

            List<ZipFileEntry> result = new List<ZipFileEntry>();

            for (int pointer = 0; pointer < this.CentralDirImage.Length; )
            {
                uint signature = BitConverter.ToUInt32(CentralDirImage, pointer);
                if (signature != 0x02014b50)
                    break;

                bool encodeUTF8 = (BitConverter.ToUInt16(CentralDirImage, pointer + 8) & 0x0800) != 0;
                ushort method = BitConverter.ToUInt16(CentralDirImage, pointer + 10);
                uint modifyTime = BitConverter.ToUInt32(CentralDirImage, pointer + 12);
                uint crc32 = BitConverter.ToUInt32(CentralDirImage, pointer + 16);
                uint comprSize = BitConverter.ToUInt32(CentralDirImage, pointer + 20);
                uint fileSize = BitConverter.ToUInt32(CentralDirImage, pointer + 24);
                ushort filenameSize = BitConverter.ToUInt16(CentralDirImage, pointer + 28);
                ushort extraSize = BitConverter.ToUInt16(CentralDirImage, pointer + 30);
                ushort commentSize = BitConverter.ToUInt16(CentralDirImage, pointer + 32);
                uint headerOffset = BitConverter.ToUInt32(CentralDirImage, pointer + 42);
                uint headerSize = (uint)( 46 + filenameSize + extraSize + commentSize);

                Encoding encoder = encodeUTF8 ? Encoding.UTF8 : DefaultEncoding;

                ZipFileEntry zfe = new ZipFileEntry();
                zfe.Method = (Compression)method;
                zfe.FilenameInZip = encoder.GetString(CentralDirImage, pointer + 46, filenameSize);
                zfe.FileOffset = GetFileOffset(headerOffset);
                zfe.FileSize = fileSize;
                zfe.CompressedSize = comprSize;
                zfe.HeaderOffset = headerOffset;
                zfe.HeaderSize = headerSize;
                zfe.Crc32 = crc32;
                zfe.ModifyTime = DosTimeToDateTime(modifyTime) ?? DateTime.Now;

                if (commentSize > 0)
                    zfe.Comment = encoder.GetString(CentralDirImage, pointer + 46 + filenameSize + extraSize, commentSize);

                result.Add(zfe);
                pointer += (46 + filenameSize + extraSize + commentSize);
            }

            return result;
        }
        /// <summary>
        /// Copy the contents of a stored file into a physical file
        /// </summary>
        /// <param name="_zfe">Entry information of file to extract</param>
        /// <param name="_filename">Name of file to store uncompressed data</param>
        /// <returns>True if success, false if not.</returns>
        /// <remarks>Unique compression methods are Store and Deflate</remarks>
        public bool ExtractFile(ZipFileEntry _zfe, string _filename)
        {
            // Make sure the parent directory exist
            string path = System.IO.Path.GetDirectoryName(_filename);

            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            // Check it is directory. If so, do nothing
            if (Directory.Exists(_filename))
                return true;

            try
            {
                // Open the archive first to avoid making zero byte files on failure
                using (var ss = Extract(_zfe))
                using (var fs = new FileStream(_filename, FileMode.Create, FileAccess.Write))
                    ss.CopyTo(fs);
            }
            catch
            {
                // Compatibility with previous API
                return false;
            }

            File.SetCreationTime(_filename, _zfe.ModifyTime);
            File.SetLastWriteTime(_filename, _zfe.ModifyTime);
            
            return true;
        }
        /// <summary>
        /// Copy the contents of a stored file into an opened stream
        /// </summary>
        /// <param name="_zfe">Entry information of file to extract</param>
        /// <param name="_stream">Stream to store the uncompressed data</param>
        /// <returns>True if success, false if not.</returns>
        /// <remarks>Unique compression methods are Store and Deflate</remarks>
        public bool ExtractFile(ZipFileEntry _zfe, Stream _stream)
        {
            if (!_stream.CanWrite)
                throw new InvalidOperationException("Stream cannot be written");

            try
            {
                using (var ss = Extract(_zfe))
                    ss.CopyTo(_stream);
                
                return true;
            }
            catch
            {
                // Compatibility with previous API
                return false;
            }
        }
        /// <summary>
        /// Opens the specified entry and returns a stream with the contents
        /// </summary>
        /// <returns>The stream with contents.</returns>
        /// <param name="zfe">The file entry.</param>
        public Stream Extract(ZipFileEntry zfe)
        {
            // check signature
            byte[] signature = new byte[4];
            this.ZipFileStream.Seek(zfe.HeaderOffset, SeekOrigin.Begin);
            this.ZipFileStream.Read(signature, 0, 4);
            if (BitConverter.ToUInt32(signature, 0) != 0x04034b50)
                throw new InvalidDataException($"Found signature {signature}, but expected {0x04034b50}");

            this.ZipFileStream.Seek(zfe.FileOffset, SeekOrigin.Begin);

            // Select input stream for inflating or just reading
            if (zfe.Method == Compression.Store)
                return new OffsetViewStream(
                    this.ZipFileStream,
                    zfe.FileOffset,
                    zfe.FileSize,
                    false
                );
            else if (zfe.Method == Compression.Deflate)
                return new OffsetViewStream(
                    new DeflateStream(this.ZipFileStream, CompressionMode.Decompress, true),
                    0, // The deflater handles the offset
                    zfe.FileSize,
                    true
                );
            else
                throw new InvalidDataException($"Unsupported compression method {zfe.Method}");
        }
        /// <summary>
        /// Copy the contents of a stored file into a byte array
        /// </summary>
        /// <param name="_zfe">Entry information of file to extract</param>
        /// <param name="_file">Byte array with uncompressed data</param>
        /// <returns>True if success, false if not.</returns>
        /// <remarks>Unique compression methods are Store and Deflate</remarks>
        public bool ExtractFile(ZipFileEntry _zfe, out byte[] _file)
        {
            var count = (int)_zfe.FileSize;
            var offset = 0;

            _file = new byte[count];

            try
            {
                using (var ss = Extract(_zfe))
                {
                    int read;
                    while (count > 0 && (read = ss.Read(_file, offset, count)) != 0)
                    {
                        count -= read;
                        offset += read;
                    }
                }

                return true;
            }
            catch
            {
                // Compatibility with previous API
                return false;
            }
        }
        /// <summary>
        /// Removes one of many files in storage. It creates a new Zip file.
        /// </summary>
        /// <param name="_zip">Reference to the current Zip object</param>
        /// <param name="_zfes">List of Entries to remove from storage</param>
        /// <returns>True if success, false if not</returns>
        /// <remarks>This method only works for storage of type FileStream</remarks>
        public static bool RemoveEntries(ref ZipStorer _zip, List<ZipFileEntry> _zfes)
        {
            if (!(_zip.ZipFileStream is FileStream))
                throw new InvalidOperationException("RemoveEntries is allowed just over streams of type FileStream");


            //Get full list of entries
            List<ZipFileEntry> fullList = _zip.ReadCentralDir();

            //In order to delete we need to create a copy of the zip file excluding the selected items
            string tempZipName = Path.GetTempFileName();
            string tempEntryName = Path.GetTempFileName();

            try
            {
                ZipStorer tempZip = ZipStorer.Create(tempZipName, string.Empty);

                foreach (ZipFileEntry zfe in fullList)
                {
                    if (!_zfes.Contains(zfe))
                    {
                        if (_zip.ExtractFile(zfe, tempEntryName))
                        {
                            tempZip.AddFile(zfe.Method, tempEntryName, zfe.FilenameInZip, zfe.Comment);
                        }
                    }
                }
                _zip.Close();
                tempZip.Close();

                File.Delete(_zip.FileName);
                File.Move(tempZipName, _zip.FileName);

                _zip = ZipStorer.Open(_zip.FileName, _zip.Access);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (File.Exists(tempZipName))
                    File.Delete(tempZipName);
                if (File.Exists(tempEntryName))
                    File.Delete(tempEntryName);
            }
            return true;
        }
        #endregion

        #region Private methods
        // Calculate the file offset by reading the corresponding local header
        private uint GetFileOffset(uint _headerOffset)
        {
            byte[] buffer = new byte[2];

            this.ZipFileStream.Seek(_headerOffset + 26, SeekOrigin.Begin);
            this.ZipFileStream.Read(buffer, 0, 2);
            ushort filenameSize = BitConverter.ToUInt16(buffer, 0);
            this.ZipFileStream.Read(buffer, 0, 2);
            ushort extraSize = BitConverter.ToUInt16(buffer, 0);

            return (uint)(30 + filenameSize + extraSize + _headerOffset);
        }
        /* Local file header:
            local file header signature     4 bytes  (0x04034b50)
            version needed to extract       2 bytes
            general purpose bit flag        2 bytes
            compression method              2 bytes
            last mod file time              2 bytes
            last mod file date              2 bytes
            crc-32                          4 bytes
            compressed size                 4 bytes
            uncompressed size               4 bytes
            filename length                 2 bytes
            extra field length              2 bytes

            filename (variable size)
            extra field (variable size)
        */
        private void WriteLocalHeader(ref ZipFileEntry _zfe)
        {
            long pos = this.ZipFileStream.Position;
            Encoding encoder = _zfe.EncodeUTF8 ? Encoding.UTF8 : DefaultEncoding;
            byte[] encodedFilename = encoder.GetBytes(_zfe.FilenameInZip);

            this.ZipFileStream.Write(new byte[] { 80, 75, 3, 4, 20, 0}, 0, 6); // No extra header
            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)(_zfe.EncodeUTF8 ? 0x0800 : 0)), 0, 2); // filename and comment encoding 
            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)_zfe.Method), 0, 2);  // zipping method
            this.ZipFileStream.Write(BitConverter.GetBytes(DateTimeToDosTime(_zfe.ModifyTime)), 0, 4); // zipping date and time
            this.ZipFileStream.Write(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, 0, 12); // unused CRC, un/compressed size, updated later
            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)encodedFilename.Length), 0, 2); // filename length
            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)0), 0, 2); // extra length

            this.ZipFileStream.Write(encodedFilename, 0, encodedFilename.Length);
            _zfe.HeaderSize = (uint)(this.ZipFileStream.Position - pos);
        }
        // Force reads the number of bytes, potentially split over several reads
        private void ForceRead(byte[] buffer, int offset, int count)
        {
            var read = 0;
            while (count > 0 && (read = this.ZipFileStream.Read(buffer, offset, count)) != 0)
            {
                count -= read;
                offset += read;
            }
            if (count != 0)
                throw new EndOfStreamException();
        }
        private ZipFileEntry ReadLocalHeader(bool seektonext = false)
        {
            var pos = this.ZipFileStream.Position;
            var buf = new byte[4 + 2 + 2 + 2 + 2 + 2 + 4 + 4 + 4 + 2 + 2];
            ForceRead(buf, 0, buf.Length);

            // Repeatedly look for 0x50 if we are seeking
            while (buf[0] != 80 || buf[1] != 75 || buf[2] != 3 || buf[3] != 4)
            {
                if (!seektonext)
                    throw new InvalidDataException($"Found record that was not a stream, pos: {pos}");

                var ix = Array.IndexOf(buf, 80, 1);
                if (ix < 0)
                {
                    pos += buf.Length;
                    ForceRead(buf, 0, buf.Length);
                    continue;
                }
                else
                {
                    pos += ix;
                    this.ZipFileStream.Position = pos;
                    ForceRead(buf, 0, buf.Length);
                }
            }

            var headerpos = pos;
            var gpf = BitConverter.ToUInt16(buf, 6);
            var method = BitConverter.ToUInt16(buf, 8);
            var ts = BitConverter.ToUInt32(buf, 10);

            var crc = BitConverter.ToUInt32(buf, 14);
            var compressed = BitConverter.ToUInt32(buf, 18);
            var rawsize = BitConverter.ToUInt32(buf, 22);

            var fnlen = BitConverter.ToUInt16(buf, 6);
            var extralen = BitConverter.ToUInt16(buf, 6);

            var encodeutf8 = (gpf & 0x0800) != 0;

            var fnbuf = new byte[fnlen];
            Encoding encoder = encodeutf8 ? Encoding.UTF8 : DefaultEncoding;
            ForceRead(fnbuf, 0, fnlen);
            var filename = encoder.GetString(fnbuf);

            pos += fnlen;

            if (extralen != 0)
            {
                var extrabuf = new byte[extralen];
                ForceRead(extrabuf, 0, extralen);
                pos += extralen;
            }

            return new ZipFileEntry()
            {
                EncodeUTF8 = encodeutf8,
                Method = (Compression)method,
                ModifyTime = DosTimeToDateTime(ts) ?? new DateTime(0),

                Crc32 = crc,
                FileSize = rawsize,
                CompressedSize = compressed,
                HeaderOffset = (uint)headerpos,
                FileOffset = (uint)this.ZipFileStream.Position,
                HeaderSize = (uint)(buf.Length + fnlen + extralen),
                Comment = string.Empty,
                FilenameInZip = filename
            };
        }
        /* Central directory's File header:
            central file header signature   4 bytes  (0x02014b50)
            version made by                 2 bytes
            version needed to extract       2 bytes
            general purpose bit flag        2 bytes
            compression method              2 bytes
            last mod file time              2 bytes
            last mod file date              2 bytes
            crc-32                          4 bytes
            compressed size                 4 bytes
            uncompressed size               4 bytes
            filename length                 2 bytes
            extra field length              2 bytes
            file comment length             2 bytes
            disk number start               2 bytes
            internal file attributes        2 bytes
            external file attributes        4 bytes
            relative offset of local header 4 bytes

            filename (variable size)
            extra field (variable size)
            file comment (variable size)
        */
        private void WriteCentralDirRecord(ZipFileEntry _zfe)
        {
            Encoding encoder = _zfe.EncodeUTF8 ? Encoding.UTF8 : DefaultEncoding;
            byte[] encodedFilename = encoder.GetBytes(_zfe.FilenameInZip);
            byte[] encodedComment = encoder.GetBytes(_zfe.Comment);

            this.ZipFileStream.Write(new byte[] { 80, 75, 1, 2, 23, 0xB, 20, 0 }, 0, 8);
            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)(_zfe.EncodeUTF8 ? 0x0800 : 0)), 0, 2); // filename and comment encoding 
            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)_zfe.Method), 0, 2);  // zipping method
            this.ZipFileStream.Write(BitConverter.GetBytes(DateTimeToDosTime(_zfe.ModifyTime)), 0, 4);  // zipping date and time
            this.ZipFileStream.Write(BitConverter.GetBytes(_zfe.Crc32), 0, 4); // file CRC
            this.ZipFileStream.Write(BitConverter.GetBytes(_zfe.CompressedSize), 0, 4); // compressed file size
            this.ZipFileStream.Write(BitConverter.GetBytes(_zfe.FileSize), 0, 4); // uncompressed file size
            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)encodedFilename.Length), 0, 2); // Filename in zip
            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)0), 0, 2); // extra length
            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)encodedComment.Length), 0, 2);

            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)0), 0, 2); // disk=0
            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)0), 0, 2); // file type: binary
            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)0), 0, 2); // Internal file attributes
            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)0x8100), 0, 2); // External file attributes (normal/readable)
            this.ZipFileStream.Write(BitConverter.GetBytes(_zfe.HeaderOffset), 0, 4);  // Offset of header

            this.ZipFileStream.Write(encodedFilename, 0, encodedFilename.Length);
            this.ZipFileStream.Write(encodedComment, 0, encodedComment.Length);
        }
        /* End of central dir record:
            end of central dir signature    4 bytes  (0x06054b50)
            number of this disk             2 bytes
            number of the disk with the
            start of the central directory  2 bytes
            total number of entries in
            the central dir on this disk    2 bytes
            total number of entries in
            the central dir                 2 bytes
            size of the central directory   4 bytes
            offset of start of central
            directory with respect to
            the starting disk number        4 bytes
            zipfile comment length          2 bytes
            zipfile comment (variable size)
        */
        private void WriteEndRecord(uint _size, uint _offset)
        {
            Encoding encoder = this.EncodeUTF8 ? Encoding.UTF8 : DefaultEncoding;
            byte[] encodedComment = encoder.GetBytes(this.Comment);

            this.ZipFileStream.Write(new byte[] { 80, 75, 5, 6, 0, 0, 0, 0 }, 0, 8);
            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)Files.Count+ExistingFiles), 0, 2);
            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)Files.Count+ExistingFiles), 0, 2);
            this.ZipFileStream.Write(BitConverter.GetBytes(_size), 0, 4);
            this.ZipFileStream.Write(BitConverter.GetBytes(_offset), 0, 4);
            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)encodedComment.Length), 0, 2);
            this.ZipFileStream.Write(encodedComment, 0, encodedComment.Length);
        }

        /* DOS Date and time:
            MS-DOS date. The date is a packed value with the following format. Bits Description 
                0-4 Day of the month (131) 
                5-8 Month (1 = January, 2 = February, and so on) 
                9-15 Year offset from 1980 (add 1980 to get actual year) 
            MS-DOS time. The time is a packed value with the following format. Bits Description 
                0-4 Second divided by 2 
                5-10 Minute (059) 
                11-15 Hour (023 on a 24-hour clock) 
        */
        private uint DateTimeToDosTime(DateTime _dt)
        {
            return (uint)(
                (_dt.Second / 2) | (_dt.Minute << 5) | (_dt.Hour << 11) | 
                (_dt.Day<<16) | (_dt.Month << 21) | ((_dt.Year - 1980) << 25));
        }
        private DateTime? DosTimeToDateTime(uint _dt)
        {
            int year = (int)(_dt >> 25) + 1980;
            int month = (int)(_dt >> 21) & 15;
            int day = (int)(_dt >> 16) & 31;
            int hours = (int)(_dt >> 11) & 31;
            int minutes = (int)(_dt >> 5) & 63;
            int seconds = (int)(_dt & 31) * 2;

            if (month==0 || day == 0)
                return null;

            return new DateTime(year, month, day, hours, minutes, seconds);
        }

        /* CRC32 algorithm
          The 'magic number' for the CRC is 0xdebb20e3.  
          The proper CRC pre and post conditioning
          is used, meaning that the CRC register is
          pre-conditioned with all ones (a starting value
          of 0xffffffff) and the value is post-conditioned by
          taking the one's complement of the CRC residual.
          If bit 3 of the general purpose flag is set, this
          field is set to zero in the local header and the correct
          value is put in the data descriptor and in the central
          directory.
        */
        private void UpdateCrcAndSizes(ref ZipFileEntry _zfe)
        {
            long lastPos = this.ZipFileStream.Position;  // remember position

            this.ZipFileStream.Position = _zfe.HeaderOffset + 8;
            this.ZipFileStream.Write(BitConverter.GetBytes((ushort)_zfe.Method), 0, 2);  // zipping method

            this.ZipFileStream.Position = _zfe.HeaderOffset + 14;
            this.ZipFileStream.Write(BitConverter.GetBytes(_zfe.Crc32), 0, 4);  // Update CRC
            this.ZipFileStream.Write(BitConverter.GetBytes(_zfe.CompressedSize), 0, 4);  // Compressed size
            this.ZipFileStream.Write(BitConverter.GetBytes(_zfe.FileSize), 0, 4);  // Uncompressed size

            this.ZipFileStream.Position = lastPos;  // restore position
        }
        // Replaces backslashes with slashes to store in zip header
        private string NormalizedFilename(string _filename)
        {
            string filename = _filename.Replace('\\', '/');

            int pos = filename.IndexOf(':');
            if (pos >= 0)
                filename = filename.Remove(0, pos + 1);

            return filename.Trim('/');
        }
        // Reads the end-of-central-directory record
        private bool ReadFileInfo()
        {
            if (this.ZipFileStream.Length < 22)
                return false;

            try
            {
                this.ZipFileStream.Seek(-17, SeekOrigin.End);
                BinaryReader br = new BinaryReader(this.ZipFileStream);
                do
                {
                    this.ZipFileStream.Seek(-5, SeekOrigin.Current);
                    UInt32 sig = br.ReadUInt32();
                    if (sig == 0x06054b50)
                    {
                        this.ZipFileStream.Seek(6, SeekOrigin.Current);

                        UInt16 entries = br.ReadUInt16();
                        Int32 centralSize = br.ReadInt32();
                        UInt32 centralDirOffset = br.ReadUInt32();
                        UInt16 commentSize = br.ReadUInt16();

                        // check if comment field is the very last data in file
                        if (this.ZipFileStream.Position + commentSize != this.ZipFileStream.Length)
                            return false;

                        // Copy entire central directory to a memory buffer
                        this.ExistingFiles = entries;
                        this.CentralDirImage = new byte[centralSize];
                        this.ZipFileStream.Seek(centralDirOffset, SeekOrigin.Begin);
                        this.ZipFileStream.Read(this.CentralDirImage, 0, centralSize);

                        // Leave the pointer at the begining of central dir, to append new files
                        this.ZipFileStream.Seek(centralDirOffset, SeekOrigin.Begin);
                        return true;
                    }
                } while (this.ZipFileStream.Position > 0);
            }
            catch { }

            return false;
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Closes the Zip file stream
        /// </summary>
        public void Dispose()
        {
            this.Close();
        }
        #endregion

        /// <summary>
        /// A stream that wraps a target stream and does
        /// on-the-fly Crc32 calculations
        /// </summary>
        private class Crc32CalculatingStream : Stream
        {
            /// <summary>
            /// The stream that data is written to
            /// </summary>
            private readonly Stream m_target;

            /// <summary>
            /// The current CRC32 value
            /// </summary>
            public UInt32 Crc32 { get; private set; } = 0 ^ 0xffffffff;

            /// <summary>
            /// The number of bytes written
            /// </summary>
            private long m_written = 0;

            /// <summary>
            /// An action to call once the stream is disposed
            /// </summary>
            private readonly Action<Crc32CalculatingStream> m_complete;
            /// <summary>
            /// A flag keeping track of the instance being disposed
            /// </summary>
            private bool m_isDisposed = false;

            /// <summary>
            /// Initializes a new instance of the <see cref="T:System.IO.Compression.ZipStorer.Crc32CalculatingStream"/> class.
            /// </summary>
            /// <param name="target">The stream where data is written to.</param>
            /// <param name="onComplete">A callback method to invoke when the stream is closed.</param>
            public Crc32CalculatingStream(Stream target, Action<Crc32CalculatingStream> onComplete)
            {
                m_target = target;
                m_complete = onComplete;
            }

            /// <summary>
            /// Gets a value indicating whether this
            /// <see cref="T:System.IO.Compression.ZipStorer.Crc32CalculatingStream"/> can be read.
            /// </summary>
            public override bool CanRead { get { return false; } }
            /// <summary>
            /// Gets a value indicating whether this
            /// <see cref="T:System.IO.Compression.ZipStorer.Crc32CalculatingStream"/> can seek.
            /// </summary>
            public override bool CanSeek { get { return false; } }
            /// <summary>
            /// Gets a value indicating whether this
            /// <see cref="T:System.IO.Compression.ZipStorer.Crc32CalculatingStream"/> can be written.
            /// </summary>
            public override bool CanWrite { get { return true; } }
            /// <summary>
            /// Gets the length of the stream.
            /// </summary>
            public override long Length { get { return m_written; } }
            /// <summary>
            /// Gets the position in the stream.
            /// </summary>
            public override long Position
            {
                get { return m_target.Position; }
                set { throw new NotSupportedException(); }
            }

            /// <summary>
            /// Flushes the current data
            /// </summary>
            public override void Flush()
            { m_target.Flush(); }

            /// <summary>
            /// Unsupported seeking operation
            /// </summary>
            /// <returns>Throws a <see cref="NotSupportedException"/>.</returns>
            /// <param name="offset">Unused offset.</param>
            /// <param name="origin">Unused origin.</param>
            public override long Seek(long offset, SeekOrigin origin)
            { throw new NotSupportedException(); }

            /// <summary>
            /// Unsupported, throws a <see cref="NotSupportedException"/>
            /// </summary>
            /// <param name="value">Unused value.</param>
            public override void SetLength(long value)
            { throw new NotSupportedException(); }

            /// <summary>
            /// Unsupported, throws a <see cref="NotSupportedException"/>
            /// </summary>
            /// <returns>Throws a <see cref="NotSupportedException"/>.</returns>
            /// <param name="buffer">Unused buffer.</param>
            /// <param name="offset">Unused offset.</param>
            /// <param name="count">Unused count.</param>
            public override int Read(byte[] buffer, int offset, int count)
            { throw new NotSupportedException(); }

            /// <summary>
            /// Writes the data from the buffer to the stream
            /// </summary>
            /// <param name="buffer">The buffer with data.</param>
            /// <param name="offset">The offset into the buffer.</param>
            /// <param name="count">The number of bytes to write.</param>
            public override void Write(byte[] buffer, int offset, int count)
            {
                m_target.Write(buffer, offset, count);
                UpdateCrc32(buffer, offset, count);
            }

            /// <summary>
            /// Writes the data from the buffer to the stream
            /// </summary>
            /// <returns>An awaitable task.</returns>
            /// <param name="buffer">The buffer with data.</param>
            /// <param name="offset">The offset into the buffer.</param>
            /// <param name="count">The number of bytes to write.</param>
            /// <param name="cancellationToken">The cancellation token.</param>
            public override Threading.Tasks.Task WriteAsync(byte[] buffer, int offset, int count, Threading.CancellationToken cancellationToken)
            {
                return m_target.WriteAsync(buffer, offset, count).ContinueWith(
                    _ => UpdateCrc32(buffer, offset, count), 
                    Threading.Tasks.TaskContinuationOptions.OnlyOnRanToCompletion
                );
            }

            /// <summary>
            /// Updates the crc32 value.
            /// </summary>
            /// <param name="buffer">The buffer with data.</param>
            /// <param name="offset">The offset into the buffer.</param>
            /// <param name="count">The number of bytes to write.</param>
            private void UpdateCrc32(byte[] buffer, int offset, int count)
            {
                m_written += count;
                for (uint i = (uint)offset; i < count + offset; i++)
                    Crc32 = ZipStorer.CrcTable[(Crc32 ^ buffer[i]) & 0xFF] ^ (Crc32 >> 8);
            }

            /// <summary>
            /// Disposes this instance and calls the completion method
            /// </summary>
            /// <param name="disposing">If set to <c>true</c> this call is from Dispose, otherwise it is from the destructor.</param>
            protected override void Dispose(bool disposing)
            {
                if (!m_isDisposed)
                {
                    Flush();
                    m_isDisposed = true;
                    Crc32 ^= 0xffffffff;
                    if (m_complete != null)
                        m_complete(this);

                    base.Dispose(disposing);
                }
            }
        }

        /// <summary>
        /// Represents a stream that is a part of the underlying stream
        /// </summary>
        private class OffsetViewStream : Stream
        {
            /// <summary>
            /// The offset into the source stream
            /// </summary>
            private readonly long m_offset;
            /// <summary>
            /// The length of this stream
            /// </summary>
            private readonly long m_length;
            /// <summary>
            /// The stream that provides the data
            /// </summary>
            private readonly Stream m_source;
            /// <summary>
            /// A flag indicating if the source stream is closed automatically
            /// </summary>
            private readonly bool m_close;
            /// <summary>
            /// The current position in the stream
            /// </summary>
            private long m_position;

            /// <summary>
            /// Initializes a new instance of the <see cref="T:System.IO.Compression.ZipStorer.OffsetViewStream"/> class.
            /// </summary>
            /// <param name="source">The stream that provides the data.</param>
            /// <param name="offset">The offset into the source stream.</param>
            /// <param name="length">The length of the source stream.</param>
            /// <param name="closeStream">If set to <c>true</c> close the source stream when disposing this instance.</param>
            public OffsetViewStream(Stream source, long offset, long length, bool closeStream)
            {
                m_source = source;
                m_offset = offset;
                m_length = length;
                m_close = closeStream;
                if (source.CanSeek)
                    source.Position = offset;
            }

            /// <summary>
            /// Gets a value indicating whether this
            /// <see cref="T:System.IO.Compression.ZipStorer.Crc32CalculatingStream"/> can be read.
            /// </summary>
            public override bool CanRead { get { return true; } }
            /// <summary>
            /// Gets a value indicating whether this
            /// <see cref="T:System.IO.Compression.ZipStorer.Crc32CalculatingStream"/> can seek.
            /// </summary>
            public override bool CanSeek { get { return m_source.CanSeek; } }
            /// <summary>
            /// Gets a value indicating whether this
            /// <see cref="T:System.IO.Compression.ZipStorer.Crc32CalculatingStream"/> can be written.
            /// </summary>
            public override bool CanWrite { get { return false; } }
            /// <summary>
            /// Gets the length of the stream.
            /// </summary>
            public override long Length { get { return m_length; } }
            /// <summary>
            /// Gets the position in the stream.
            /// </summary>
            public override long Position
            {
                get { return m_position; }
                set 
                {
                    if (value < 0 || value > m_length)
                        throw new ArgumentOutOfRangeException(nameof(value));
                    
                    m_position = m_source.Position = m_offset + value;
                }
            }

            /// <summary>
            /// Flushes the current data
            /// </summary>
            public override void Flush() { }

            /// <summary>
            /// Read data from the stream into the buffer.
            /// </summary>
            /// <returns>The number of bytes read.</returns>
            /// <param name="buffer">The buffer to read into.</param>
            /// <param name="offset">The offset into the buffer where writing starts.</param>
            /// <param name="count">The maximum number of bytes to read.</param>
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));
                
                count = (int)Math.Min(m_length - Position, count);
                var res = m_source.Read(buffer, offset, count);
                m_position += res;

                return res;
            }

            /// <summary>
            /// Read data from the stream into the buffer.
            /// </summary>
            /// <returns>An awaitable task with the number of bytes read.</returns>
            /// <param name="buffer">The buffer to read into.</param>
            /// <param name="offset">The offset into the buffer where writing starts.</param>
            /// <param name="count">The maximum number of bytes to read.</param>
            /// <param name="cancellationToken">The cancellation token.</param>
            public override Threading.Tasks.Task<int> ReadAsync(byte[] buffer, int offset, int count, Threading.CancellationToken cancellationToken)
            {
                if (count > 0)
                    throw new ArgumentOutOfRangeException(nameof(count));

                count = (int)Math.Min(m_length - Position, count);
                return m_source.ReadAsync(buffer, offset, count, cancellationToken).ContinueWith(t => {
                    m_position += t.Result;
                    return t.Result;
                }, Threading.Tasks.TaskContinuationOptions.OnlyOnRanToCompletion);
            }

            /// <summary>
            /// Seeks the stream to the new position
            /// </summary>
            /// <returns>The new position.</returns>
            /// <param name="offset">The number of bytes to move from the origin.</param>
            /// <param name="origin">The origin of the seek operation.</param>
            public override long Seek(long offset, SeekOrigin origin)
            {
                long newpos;
                if (origin == SeekOrigin.Begin)
                    newpos = offset;
                else if (origin == SeekOrigin.Current)
                    newpos = Position + offset;
                else if (origin == SeekOrigin.End)
                    newpos = m_length + offset;
                else
                    throw new ArgumentOutOfRangeException(nameof(origin));

                newpos = Math.Min(Math.Max(0, newpos), m_length - 1);
                return Position = newpos;
            }

            /// <summary>
            /// Unsupported, throws <see cref="NotSupportedException"/>.
            /// </summary>
            /// <param name="value">Unused value.</param>
            public override void SetLength(long value)
            { throw new NotSupportedException(); }

            /// <summary>
            /// Unsupported, throws <see cref="NotSupportedException"/>.
            /// </summary>
            /// <param name="buffer">Unused buffer.</param>
            /// <param name="offset">Unused offset.</param>
            /// <param name="count">Unused count.</param>
            public override void Write(byte[] buffer, int offset, int count)
            { throw new NotSupportedException(); }

            /// <summary>
            /// Disposes all resources
            /// </summary>
            /// <param name="disposing">If set to <c>true</c> this is called from Dispose, otherwise the call is from the destructor.</param>
            protected override void Dispose(bool disposing)
            {
                if (m_close)
                    m_source.Dispose();
                base.Dispose(disposing);
            }
        }

    }

}