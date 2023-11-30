using Duplicati.Library.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Volumes
{
    public abstract class VolumeReaderBase : VolumeBase, IDisposable
    {
        public bool IsFullBackup { get; set; }

        protected readonly bool m_disposeCompression = false;
        protected ICompression m_compression;
        protected Stream m_stream;
        
        private static ICompression LoadCompressor(string compressor, Stream stream, Options options)
        {
            var tmp = DynamicLoader.CompressionLoader.GetModule(compressor, stream, Interface.ArchiveMode.Read, options.RawOptions);
            if (tmp == null)
            {
                var name = "[stream]";
                if (stream is FileStream fileStream)
                    name = fileStream.Name;

                throw new Exception(string.Format("Unable to create {0} decompressor on file {1}", compressor, name));
            }

            return tmp;
        }

        private static ICompression LoadCompressor(string compressor, string file, Options options, out Stream stream)
        {
            stream = new System.IO.FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            return LoadCompressor(compressor, stream, options);
        }

        protected VolumeReaderBase(string compressor, string file, Options options)
            : base(options)
        {
            m_compression = LoadCompressor(compressor, file, options, out m_stream);

            ReadFileset();

            ReadManifests(options);
            
            m_disposeCompression = true;
        }

        protected VolumeReaderBase(ICompression compression, Options options)
            : base(options)
        {
            m_compression = compression;
            ReadManifests(options);
        }

        private void ReadFileset()
        {
            using (var s = m_compression.OpenRead(FILESET_FILENAME))
            {
                if (s == null)
                {
                    IsFullBackup = new FilesetData().IsFullBackup; // use default value
                }
                else
                {
                    using (var fs = new StreamReader(s, ENCODING))
                    {
                        FilesetData fileset = JsonConvert.DeserializeObject<FilesetData>(fs.ReadToEnd());
                        IsFullBackup = fileset.IsFullBackup;
                    }
                }
            }
        }

        private void ReadManifests(Options options)
        {
            if (options.DontReadManifests) return;

            using (var s = m_compression.OpenRead(MANIFEST_FILENAME))
            {
                if (s == null)
                {
                    throw new InvalidManifestException("No manifest file found in volume");
                }

                using (var fs = new StreamReader(s, ENCODING))
                {
                    ManifestData.VerifyManifest(fs.ReadToEnd(), m_blocksize, options.BlockHashAlgorithm, options.FileHashAlgorithm);
                }
            }
        }

        public static FilesetData GetFilesetData(string compressor, string file, Options options)
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var c = LoadCompressor(compressor, stream, options))
            using (var s = c.OpenRead(FILESET_FILENAME))
            {
                if (s == null)
                {
                    return new FilesetData(); // return default
                }

                using (var fs = new StreamReader(s, ENCODING))
                {
                    return JsonConvert.DeserializeObject<FilesetData>(fs.ReadToEnd());
                }
            }
        }

        public static void UpdateOptionsFromManifest(string compressor, string file, Options options)
        {
            using (var stream = new System.IO.FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var c = LoadCompressor(compressor, stream, options))
            using (var s = c.OpenRead(MANIFEST_FILENAME))
            using (var fs = new StreamReader(s, ENCODING))
            {
                var d = JsonConvert.DeserializeObject<ManifestData>(fs.ReadToEnd());
                if (d.Version > ManifestData.VERSION)
                    throw new InvalidManifestException("Version", d.Version.ToString(), ManifestData.VERSION.ToString());

                string n;

                if (!options.RawOptions.TryGetValue("blocksize", out n) || string.IsNullOrEmpty(n))
                    options.RawOptions["blocksize"] = d.Blocksize + "b";
                if (!options.RawOptions.TryGetValue("block-hash-algorithm", out n) || string.IsNullOrEmpty(n))
                    options.RawOptions["block-hash-algorithm"] = d.BlockHash;
                if (!options.RawOptions.TryGetValue("file-hash-algorithm", out n) || string.IsNullOrEmpty(n))
                    options.RawOptions["file-hash-algorithm"] = d.FileHash;
            }
        }

        public virtual void Dispose()
        {
            if (m_disposeCompression && m_compression != null)
            {
                m_compression.Dispose();
                m_compression = null;

                m_stream?.Dispose();
                m_stream = null;
            }
        }

        public static IEnumerable<string> ReadBlocklist(ICompression compression, string filename, long hashsize)
        {
            var buffer = new byte[hashsize];
            using (var fs = compression.OpenRead(filename))
            {
                int s;
				var read = 0L;
                while ((s = Library.Utility.Utility.ForceStreamRead(fs, buffer, buffer.Length)) != 0)
                {
                    if (s != buffer.Length)
						throw new InvalidDataException($"Premature End-of-stream encountered while reading blocklist hashes for {filename}. Got {s} bytes of {buffer.Length} at offset {read * buffer.Length}");

					read++;
                    yield return Convert.ToBase64String(buffer);
                }
            }
        }

        /// <summary>
        /// Read blocklist and check the hash. Throws InvalidDataException if not matching
        /// </summary>
        public static IList<string> ReadBlocklistVerified(ICompression compression, string filename, long hashsize, string hash, string blockHashAlgorithm)
        {
            List<string> blocks = new List<string>();
            var buffer = new byte[hashsize];
            var hashalg = HashAlgorithmHelper.Create(blockHashAlgorithm);
            using (var fs = compression.OpenRead(filename))
            {
                int s;
                var read = 0L;
                while ((s = Library.Utility.Utility.ForceStreamRead(fs, buffer, buffer.Length)) != 0)
                {
                    if (s != buffer.Length)
                        throw new InvalidDataException($"Premature End-of-stream encountered while reading blocklist hashes for {filename}. Got {s} bytes of {buffer.Length} at offset {read * buffer.Length}");

                    read++;
                    hashalg.TransformBlock(buffer, 0, s, buffer, 0);
                    blocks.Add(Convert.ToBase64String(buffer));
                }
            }
            hashalg.TransformFinalBlock(buffer, 0, 0);
            string calculatedHash = Convert.ToBase64String(hashalg.Hash);
            if (hash != calculatedHash)
            {
                throw new InvalidDataException($"Blocklist hash does not match: expected {hash}, got {calculatedHash}");
            }
            return blocks;
        }

        protected static object SkipJsonToken(JsonReader reader, JsonToken type)
        {
            if (!reader.Read() || reader.TokenType != type)
                throw new InvalidDataException(string.Format("Invalid JSON, expected \"{0}\", but got {1}, {2}", type, reader.TokenType, reader.Value));

            return reader.Value;
        }

        protected static object ReadJsonProperty(JsonReader reader, string propertyname, JsonToken type)
        {
            var p = SkipJsonToken(reader, JsonToken.PropertyName);
            if (p == null || p.ToString() != propertyname)
                throw new InvalidDataException(string.Format("Invalid JSON, expected property \"{0}\", but got {1}, {2}", propertyname, reader.TokenType, reader.Value));

            return SkipJsonToken(reader, type);
        }

        protected static string ReadJsonStringProperty(JsonReader reader, string propertyname)
        {
            return ReadJsonProperty(reader, propertyname, JsonToken.String).ToString();
        }

        protected static long ReadJsonInt64Property(JsonReader reader, string propertyname)
        {
            return Convert.ToInt64(ReadJsonProperty(reader, propertyname, JsonToken.Integer));
        }

        protected static DateTime ReadJsonDateTimeProperty(JsonReader reader, string propertyname)
        {
            return Library.Utility.Utility.DeserializeDateTime(ReadJsonStringProperty(reader, propertyname));
        }
    }
}
