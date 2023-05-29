using Duplicati.Library.Interface;
using Duplicati.Library.Main.Operation.Backup;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Main
{
    public class BackupRewriter : IDisposable
    {
        Options m_options;
        long m_oldBlocksize;
        long m_hashesprblock;
        HashLookupHelper m_lookup;
        BlockVolumeWriter m_blockvolume = null;
        FilesetVolumeWriter m_filesetvolume = null;
        IndexVolumeWriter m_indexVolume = null;
        long m_nextBlockvolumeId = 0;
        // Already saved blocks and sizes
        Dictionary<string, long> m_blocks = new Dictionary<string, long>();
        // Cache combined file hashes of large files and the combined blocklist hashes, because files are processed for each version.
        // Bool is true when list contains blocklists, false for a single block
        Dictionary<string, KeyValuePair<bool, List<string>>> m_fileBlocklists = new Dictionary<string, KeyValuePair<bool, List<string>>>();
        string m_outputPath;
        HashAlgorithm m_blockhasher;
        List<Tuple<string, byte[], int>> m_blocklistHashes;

        public BackupRewriter(Dictionary<string, string> options, long oldBlocksize, long hashesprblock, HashLookupHelper lookup, string outputPath)
        {
            m_options = new Library.Main.Options(options);
            m_oldBlocksize = oldBlocksize;
            m_hashesprblock = hashesprblock;
            m_lookup = lookup;
            m_outputPath = outputPath;
            m_blockhasher = HashAlgorithmHelper.Create(m_options.BlockHashAlgorithm);
        }
        public void ProcessListFile(IEnumerable<Volumes.IFileEntry> listFiles, IEnumerable<KeyValuePair<string, Stream>> controlFiles, DateTime timestamp)
        {
            m_filesetvolume = new FilesetVolumeWriter(m_options, timestamp);
            var i = 0L;
            List<string> errors = null;
            foreach (var f in listFiles)
            {
                try
                {
                    IEnumerable<string> blocklistHashes = null;
                    IEnumerable<string> metablockHash = null;
                    if (f.Type == FilelistEntryType.File)
                    {
                        Console.WriteLine("{0}: {1} ({2})", i, f.Path, Library.Utility.Utility.FormatSizeString(f.Size));
                        var hint = m_options.GetCompressionHintFromFilename(f.Path);

                        var blockhash = f.Blockhash ?? f.Hash;
                        AddOrCombineBlocks(f.BlocklistHashes, (int)f.Size, ref blocklistHashes, hint, ref blockhash);
                        var metahash = f.Metablockhash ?? f.Metahash;
                        AddOrCombineBlocks(f.MetaBlocklistHashes, (int)f.Metasize, ref metablockHash, CompressionHint.Default, ref metahash);
                        if (blockhash == f.Hash)
                        {
                            blockhash = null;
                        }
                        if (metahash == f.Metahash)
                        {
                            metahash = null;
                        }
                        m_filesetvolume.AddFile(f.Path, f.Hash, f.Size, f.Time, f.Metahash, f.Metasize,
                            metahash, blockhash, f.Blocksize, blocklistHashes, metablockHash);
                    }
                    else if (f.Type == FilelistEntryType.Folder || f.Type == FilelistEntryType.Symlink)
                    {
                        var metahash = f.Metablockhash ?? f.Metahash;
                        AddOrCombineBlocks(f.MetaBlocklistHashes, (int)f.Metasize, ref metablockHash, CompressionHint.Default, ref metahash);
                        if (metahash == f.Metahash)
                        {
                            metahash = null;
                        }
                        if (f.Type == FilelistEntryType.Folder)
                        {
                            m_filesetvolume.AddDirectory(f.Path, f.Metahash, f.Metasize, f.Metablockhash, metablockHash);
                        }
                        else
                        {
                            m_filesetvolume.AddSymlink(f.Path, f.Metahash, f.Metasize, f.Metablockhash, metablockHash);
                        }
                    }
                }
                catch (OperationAbortException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(" error: {0}", ex);
                    if (errors == null)
                    {
                        errors = new List<string>();
                    }
                    errors.Add(f.Path + ": " + ex.ToString());
                }
                i++;
            }
            foreach (var f in controlFiles)
            {
                m_filesetvolume.AddControlFile(f.Key, m_options.GetCompressionHintFromFilename(f.Key));
            }
            m_filesetvolume.Close();
            var setEntry = m_filesetvolume.CreateFileEntryForUpload(m_options);
            string path = Path.Combine(m_outputPath, Path.GetFileName(setEntry.RemoteFilename));
            if (!Directory.Exists(m_outputPath))
            {
                Directory.CreateDirectory(m_outputPath);
            }
            File.Copy(setEntry.LocalFilename, path);
            setEntry.DeleteLocalFile();
            Console.WriteLine("Copied {0}", path);
            if (errors != null)
            {
                throw new FileMissingException(string.Join(Environment.NewLine, errors));
            }
        }

        private void AddOrCombineBlocks(IEnumerable<string> listHashes, int size, ref IEnumerable<string> blocklistHashes, CompressionHint hint, ref string blockhash)
        {
            if (size == 0)
            {
                // Empty file, do not need to add blocks
                return;
            }
            else if (listHashes == null)
            {
                // Single block, no need to change
                AddExistingBlock(blockhash, 0, size, hint);
            }
            else
            {
                // Multiple blocks, need to combine
                // blockhash is hash of full file or metadata, check if already combined the same file
                if (m_fileBlocklists.TryGetValue(blockhash, out KeyValuePair<bool, List<string>> pair))
                {
                    if (!pair.Key)
                    {
                        // Combined into one
                        blockhash = pair.Value.First();
                        blocklistHashes = null;
                    }
                    else
                    {
                        // Full list
                        blocklistHashes = pair.Value;
                        blockhash = null;
                    }
                    return;
                }
                var hashes = CombineBlocks(listHashes, hint, out IEnumerable<string> combinedHashes);
                if (hashes.Count() == 1)
                {
                    // Combined into one
                    m_fileBlocklists[blockhash] = new KeyValuePair<bool, List<string>>(false, new List<string>()
                    {
                        hashes.First()
                    });
                    blockhash = hashes.First();
                    blocklistHashes = null;
                }
                else
                {
                    // Full list
                    m_fileBlocklists[blockhash] = new KeyValuePair<bool, List<string>>(false, new List<string>(combinedHashes));
                    blocklistHashes = combinedHashes;
                    blockhash = null;
                }
            }
        }

        private IEnumerable<string> CombineBlocks(IEnumerable<string> blocklistHashes, CompressionHint hint, out IEnumerable<string> blocklistHashesOut)
        {
            int blocksize = m_options.Blocksize;
            int nBlocks = blocksize / (int)m_oldBlocksize;
            var blhi = 0L;
            var currentBlocks = 0;
            var blocklistbuffer = new byte[blocksize];
            int blocklistoffset = 0;
            var buf = new byte[blocksize];
            List<string> blocklisthashes = new List<string>();
            List<string> hashcollector = new List<string>();
            MemoryStream s = new MemoryStream(buf);
            foreach (var blh in blocklistHashes)
            {
                var blockhashoffset = blhi * m_hashesprblock * m_oldBlocksize;
                try
                {
                    var bi = 0;
                    foreach (var h in m_lookup.ReadBlocklistHashes(blh))
                    {
                        try
                        {
                            s.Position = blockhashoffset + (currentBlocks * m_oldBlocksize);
                            m_lookup.WriteHash(s, h);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Failed to read hash: {0}{1}{2}", h, Environment.NewLine, ex);
                        }

                        bi++;
                        currentBlocks++;
                        if (currentBlocks == nBlocks)
                        {
                            FinishCombinedBlock(hint, blocksize, ref blocklistbuffer, ref blocklistoffset, buf, blocklisthashes, hashcollector, s);
                            currentBlocks = 0;
                        }
                    }
                }
                catch (OperationAbortException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to read Blocklist hash: {0}{1}{2}", blh, Environment.NewLine, ex);
                }

                blhi++;
            }
            if (currentBlocks > 0)
            {
                // Finish last block
                FinishCombinedBlock(hint, blocksize, ref blocklistbuffer, ref blocklistoffset, buf, blocklisthashes, hashcollector, s);
            }
            if (hashcollector.Count > 1)
            {
                var blkey = Convert.ToBase64String(m_blockhasher.ComputeHash(blocklistbuffer, 0, (int)blocklistoffset));
                blocklisthashes.Add(blkey);
                AddBlock(blkey, blocklistbuffer, 0, blocklistoffset, CompressionHint.Noncompressible, true);
            }
            blocklistHashesOut = blocklisthashes;
            return hashcollector;
        }

        private void FinishCombinedBlock(CompressionHint hint, int blocksize, ref byte[] blocklistbuffer, ref int blocklistoffset, byte[] buf, List<string> blocklisthashes, List<string> hashcollector, MemoryStream s)
        {
            // Finish block
            var hashdata = m_blockhasher.ComputeHash(buf, 0, (int)s.Position);
            var hashkey = Convert.ToBase64String(hashdata);

            // If we have too many hashes, flush the blocklist
            if (blocklistbuffer.Length - blocklistoffset < hashdata.Length)
            {
                var blkey = Convert.ToBase64String(m_blockhasher.ComputeHash(blocklistbuffer, 0, (int)blocklistoffset));
                blocklisthashes.Add(blkey);
                var dataBlock = new DataBlock()
                {
                    HashKey = blkey,
                    Data = blocklistbuffer,
                    Offset = 0,
                    Size = blocklistoffset,
                    Hint = CompressionHint.Noncompressible,
                    IsBlocklistHashes = true
                };
                AddBlock(dataBlock.HashKey, dataBlock.Data, dataBlock.Offset, (int)dataBlock.Size, dataBlock.Hint, dataBlock.IsBlocklistHashes);
                blocklistoffset = 0;
                blocklistbuffer = new byte[blocksize];
            }

            // Store the current hash in the blocklist
            Array.Copy(hashdata, 0, blocklistbuffer, blocklistoffset, hashdata.Length);
            blocklistoffset += hashdata.Length;
            hashcollector.Add(hashkey);

            AddBlock(hashkey, buf, 0, (int)s.Position, hint);
            Array.Clear(buf, 0, buf.Length);
            s.Position = 0;
        }

        private void AddExistingBlock(string hash, int offset, int size, CompressionHint hint)
        {
            AddBlock(hash, null, offset, size, hint);
        }
        private void AddBlock(string hash, byte[] data, int offset, int size, CompressionHint hint, bool isBlocklist = false)
        {
            // Start new block volume
            if (m_blockvolume == null)
            {
                m_blockvolume = new BlockVolumeWriter(m_options);
                m_blockvolume.VolumeID = m_nextBlockvolumeId;
                m_nextBlockvolumeId += 1;

                m_blocklistHashes = new List<Tuple<string, byte[], int>>();
                m_indexVolume = new IndexVolumeWriter(m_options);
                m_indexVolume.StartVolume(Path.GetFileName(m_blockvolume.RemoteFilename));
            }

            // Check if exists
            long exsize;
            bool blockExists = m_blocks.TryGetValue(hash, out exsize);
            if (blockExists && exsize != size)
            {
                throw new OperationAbortException(OperationAbortReason.Error, "Hash collision! There is no recovery implemented, the backup cannot be rewritten");
            }
            else if (!blockExists)
            {
                m_blockvolume.AddBlock(hash, data ?? m_lookup.ReadHash(hash), offset, size, hint);
                m_indexVolume.AddBlock(hash, size);
                if (isBlocklist && m_options.IndexfilePolicy == Options.IndexFileStrategy.Full)
                {
                    m_blocklistHashes.Add(new Tuple<string, byte[], int>(hash, (byte[])data.Clone(), size));
                }
                m_blocks.Add(hash, size);

                // If volume is full, copy to destination
                if (m_blockvolume.Filesize > m_options.VolumeSize - m_options.Blocksize)
                {
                    FinishVolumes();
                }
            }
        }

        private void FinishVolumes()
        {
            m_blockvolume.Close();
            if (!Directory.Exists(m_outputPath))
            {
                Directory.CreateDirectory(m_outputPath);
            }
            var blockEntry = m_blockvolume.CreateFileEntryForUpload(m_options);
            string path = Path.Combine(m_outputPath, Path.GetFileName(blockEntry.RemoteFilename));
            File.Copy(blockEntry.LocalFilename, path);
            Console.WriteLine("Copied {0}", path);
            blockEntry.DeleteLocalFile();

            m_indexVolume.FinishVolume(blockEntry.Hash, blockEntry.Size);

            if (m_options.IndexfilePolicy == Options.IndexFileStrategy.Full)
            {
                foreach (var b in m_blocklistHashes)
                {
                    m_indexVolume.WriteBlocklist(b.Item1, b.Item2, 0, b.Item3);
                }
            }
            m_indexVolume.Close();
            var indexEntry = m_indexVolume.CreateFileEntryForUpload(m_options);
            path = Path.Combine(m_outputPath, Path.GetFileName(indexEntry.RemoteFilename));
            File.Copy(indexEntry.LocalFilename, path);
            Console.WriteLine("Copied {0}", path);
            indexEntry.DeleteLocalFile();

            m_blockvolume = null;
            m_indexVolume = null;
        }

        public void Dispose()
        {
            if (m_blockvolume != null)
            {
                FinishVolumes();
            }
        }


        public class HashLookupHelper : IDisposable
        {
            private const int LOOKUP_TABLE_SIZE = 2048;

            private readonly CompressedFileMRUCache m_cache;
            private readonly int m_hashsize;
            private readonly int m_blocksize;
            private Stream m_indexfile;
            private readonly List<string> m_lookup = new List<string>();
            private readonly List<long> m_offsets = new List<long>();
            private readonly byte[] m_linebuf = new byte[128];
            private readonly byte[] m_newline = Environment.NewLine.Select(x => (byte)x).ToArray();

            public HashLookupHelper(string indexfile, CompressedFileMRUCache cache, int blocksize, int hashsize)
            {
                m_cache = cache;
                m_blocksize = blocksize;
                m_hashsize = hashsize;
                m_indexfile = File.OpenRead(indexfile);

                // Build index ....
                var hashes = 0L;
                string prev = null;
                m_indexfile.Position = 0;
                foreach (var n in AllFileLines())
                {
                    if (n.Key != prev)
                    {
                        hashes++;
                        prev = n.Key;
                    }
                }

                Console.WriteLine("Index file has {0} hashes in total", hashes);

                var lookuptablesize = Math.Max(1, Math.Min(LOOKUP_TABLE_SIZE, hashes) - 1);
                var lookupincrements = hashes / lookuptablesize;

                Console.WriteLine("Building lookup table with {0} entries, giving increments of {1}", lookuptablesize, lookupincrements);

                prev = null;
                var prevoff = 0L;
                var hc = 0L;
                m_indexfile.Position = 0;
                foreach (var n in AllFileLines())
                {
                    if (n.Key != prev)
                    {
                        if ((hc % lookupincrements) == 0)
                        {
                            m_lookup.Add(n.Key);
                            m_offsets.Add(prevoff);
                        }

                        hc++;
                        prev = n.Key;
                        prevoff = m_indexfile.Position;
                    }
                }
            }

            private string ReadNextLine()
            {
                var p = m_indexfile.Position;
                var max = m_indexfile.Read(m_linebuf, 0, m_linebuf.Length);
                if (max == 0)
                    return null;

                var lfi = 0;
                for (int i = 0; i < max; i++)
                    if (m_linebuf[i] == m_newline[lfi])
                    {
                        if (lfi == m_newline.Length - 1)
                        {
                            m_indexfile.Position = p + i + 1;
                            return System.Text.Encoding.Default.GetString(m_linebuf, 0, i - m_newline.Length + 1);

                        }
                        else
                        {
                            lfi++;
                        }
                    }
                    else
                    {
                        lfi = 0;
                    }

                throw new Exception(string.Format("Unexpected long line starting at offset {0}, read {1} bytes without a newline", p, m_linebuf.Length));
            }

            private IEnumerable<KeyValuePair<string, string>> AllFileLines()
            {
                while (true)
                {
                    var str = ReadNextLine();
                    if (str == null)
                        break;

                    if (str.Length == 0)
                        continue;

                    var ix = str.IndexOf(", ", StringComparison.Ordinal);
                    if (ix < 0)
                        Console.WriteLine("Failed to parse line starting at offset {0} in index file, string: {1}", m_indexfile.Position - str.Length - m_newline.Length, str);
                    yield return new KeyValuePair<string, string>(str.Substring(0, ix), str.Substring(ix + 2));
                }
            }

            public IEnumerable<string> ReadBlocklistHashes(string hash)
            {
                var bytes = ReadHash(hash);
                for (var i = 0; i < bytes.Length; i += m_hashsize)
                    yield return Convert.ToBase64String(bytes, i, m_hashsize);
            }

            public byte[] ReadHash(string hash)
            {
                var ix = m_lookup.BinarySearch(hash, StringComparer.Ordinal);
                if (ix < 0)
                {
                    ix = ~ix;
                    if (ix != 0)
                        ix--;
                }

                m_indexfile.Position = m_offsets[ix];

                foreach (var v in AllFileLines())
                {
                    if (v.Key == hash)
                        using (var fs = m_cache.ReadBlock(v.Value, hash))
                        {
                            var buf = new byte[m_blocksize];
                            var l = Duplicati.Library.Utility.Utility.ForceStreamRead(fs, buf, buf.Length);
                            Array.Resize(ref buf, l);

                            return buf;
                        }


                    if (StringComparer.Ordinal.Compare(hash, v.Key) < 0)
                        break;
                }

                throw new Exception(string.Format("Unable to locate block with hash: {0}", hash));
            }

            public void WriteHash(Stream sw, string hash)
            {
                var h = ReadHash(hash);
                sw.Write(h, 0, h.Length);
            }


            public void Dispose()
            {
                if (m_indexfile != null)
                    m_indexfile.Dispose();
                m_indexfile = null;
            }
        }

        public class CompressedFileMRUCache : IDisposable
        {
            private Dictionary<string, Library.Interface.ICompression> m_lookup = new Dictionary<string, Duplicati.Library.Interface.ICompression>();
            private Dictionary<string, Stream> m_streams = new Dictionary<string, Stream>();
            private List<string> m_mru = new List<string>();
            private readonly Dictionary<string, string> m_options;
            private Dictionary<string, TempFile> m_tempDecrypted = new Dictionary<string, TempFile>();

            private const int MAX_OPEN_ARCHIVES = 20;

            public CompressedFileMRUCache(Dictionary<string, string> options)
            {
                m_options = options;
            }

            public Stream ReadBlock(string filename, string hash)
            {
                ICompression cf;
                Stream stream;
                if (!m_lookup.TryGetValue(filename, out cf) || !m_streams.TryGetValue(filename, out stream))
                {
                    var p = VolumeBase.ParseFilename(filename);
                    var streamFile = filename;
                    if (p.EncryptionModule != null)
                    {
                        // Decrypt to temporary file
                        var tf = new TempFile();
                        using (var m = DynamicLoader.EncryptionLoader.GetModule(p.EncryptionModule, m_options["passphrase"], m_options))
                        {
                            if (m == null)
                            {
                                throw new UserInformationException(string.Format("Encryption method not supported: {0}", p.EncryptionModule), "EncryptionMethodNotSupported");
                            }
                            m.Decrypt(filename, tf);
                            streamFile = tf;
                            m_tempDecrypted[filename] = tf;
                        }

                    }
                    stream = new FileStream(streamFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                    cf = DynamicLoader.CompressionLoader.GetModule(p.CompressionModule, stream, ArchiveMode.Read, m_options);
                    if (cf == null)
                    {
                        stream.Dispose();
                        throw new Exception(string.Format("Unable to decompress {0}, no such compression module {1}", filename, Path.GetExtension(filename).Trim('.')));
                    }
                    m_lookup[filename] = cf;
                    m_streams[filename] = stream;
                }

                if (m_mru.Count > 0 && m_mru[0] != filename)
                {
                    m_mru.Remove(filename);
                    m_mru.Insert(0, filename);
                }

                while (m_mru.Count > MAX_OPEN_ARCHIVES)
                {
                    var f = m_mru.Last();
                    m_lookup[f].Dispose();
                    m_lookup.Remove(f);
                    m_streams[f].Dispose();
                    m_streams.Remove(f);
                    m_tempDecrypted[f].Dispose();
                    m_tempDecrypted.Remove(f);
                    m_mru.Remove(f);
                }

                return cf.OpenRead(Library.Utility.Utility.Base64PlainToBase64Url(hash));
            }

            #region IDisposable implementation

            public void Dispose()
            {
                foreach (var v in m_lookup.Values)
                    try { v.Dispose(); }
                    catch { }

                foreach (var v in m_streams.Values)
                    try { v.Dispose(); }
                    catch { }

                foreach (var v in m_tempDecrypted.Values)
                    try { v.Dispose(); }
                    catch { }

                m_lookup = null;
                m_streams = null;
                m_mru = null;
            }

            #endregion
        }
    }
}
