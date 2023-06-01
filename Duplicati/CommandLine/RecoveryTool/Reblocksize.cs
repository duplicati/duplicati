using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Duplicati.CommandLine.RecoveryTool
{
    /*
     * Operates on a local, unencrypted backup to change the blocksize while keeping history and metadata intact.
     * 
     * - Iterate over files in dlist filelist, starting from oldest backup version
     * - Keep all single block files and meta hashes the same
     * - For files with blocklists:
     *   - Open blocklist and find all blocks for that file
     *   - Combine n blocks into one new block and keep track of that mapping to new hashes
     *   - If the n blocks already exist in map, use that new block from an earlier version
     *   - If some or all blocks are different, copy data into a new block and save the hash in map
     *   - If there is only one block left, skip blocklist and directly use hash, otherwise create new blocklist
     * - Write new dlist, dblock, dindex with updated blocks and manifest to destination folder
     * - Recreate database should update local DB with new block size
     *
     * For easier usage:
     * - If there is no index file, create index from dindex files
     * - If source files are encrypted, decrypt to temporary files
     * - Possibility to encrypt destination files before copying
     * - If destination is not empty, read and index existing files to be able to continue an interrupted process
     */
    public static class Reblocksize
    {

        public static int Run(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count < 3 || args.Count > 4)
            {
                Console.WriteLine("Invalid argument count ({0} expected 3 or 4): {1}{2}", args.Count, Environment.NewLine, string.Join(Environment.NewLine, args));
                return 100;
            }

            var folder = Path.GetFullPath(args[1]);
            var outputFolder = Path.GetFullPath(args[2]);

            if (!Directory.Exists(folder))
            {
                Console.WriteLine("Folder not found: {0}", folder);
                return 100;
            }

            Directory.SetCurrentDirectory(folder);

            var listFiles = List.ParseListFiles(folder);

            bool encrypt = Library.Utility.Utility.ParseBoolOption(options, "encrypt");

            if (!options.ContainsKey("passphrase"))
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PASSPHRASE")))
                    options["passphrase"] = Environment.GetEnvironmentVariable("PASSPHRASE");

            if (encrypt)
            {
                if (!options.ContainsKey("passphrase"))
                {
                    Console.WriteLine("Password not specified for reencrypt");
                    return 100;
                }
            }

            Options opt = new Options(options);

            Console.WriteLine("Opening list files...");
            using (var tdir = DecryptListFiles(listFiles, opt))
            {
                string blocksize_str;
                options.TryGetValue("blocksize", out blocksize_str);
                string blockhash_str;
                options.TryGetValue("block-hash-algorithm", out blockhash_str);
                string filehash_str;
                options.TryGetValue("block-hash-algorithm", out filehash_str);
                long blocksize = string.IsNullOrWhiteSpace(blocksize_str) ? 0 : Sizeparser.ParseSize(blocksize_str);

                if (blocksize <= 0)
                {
                    Console.WriteLine("Invalid blocksize: {0}, try setting --blocksize manually", blocksize);
                    return 100;
                }

                var blockhasher = string.IsNullOrWhiteSpace(blockhash_str) ? null : Library.Utility.HashAlgorithmHelper.Create(blockhash_str);
                var filehasher = string.IsNullOrWhiteSpace(filehash_str) ? null : Library.Utility.HashAlgorithmHelper.Create(filehash_str);
                var hashsize = blockhasher.HashSize / 8;

                if (blockhasher == null)
                    throw new UserInformationException(string.Format("Block hash algorithm not valid: {0}", blockhash_str), "BlockHashAlgorithmNotSupported");
                if (filehasher == null)
                    throw new UserInformationException(string.Format("File hash algorithm not valid: {0}", filehash_str), "FileHashAlgorithmNotSupported");


                string ixfile;
                if (!options.TryGetValue("indexfile", out ixfile))
                {
                    ixfile = null;
                }
                string defaultIndex = "index.txt";

                BlockIndex blockIndex = new BlockIndex(new Options(options), (int)blocksize, hashsize);
                if (ixfile == null || !File.Exists(ixfile))
                {
                    Console.WriteLine("Creating block index from dindex volumes");
                    blockIndex.CreateFromIndexVolumes(folder);
                }
                if (blockIndex.BlockCount == 0)
                {
                    if (!File.Exists(ixfile ?? defaultIndex))
                    {
                        Console.WriteLine("No dindex files or recover index file found, need to use index command manually!");
                        return 100;
                    }
                    else
                    {
                        Console.WriteLine("Creating block index from index files. Warning: Does not index blocklists and operation will be less efficient!");
                        blockIndex.CreateFromIndexFile(ixfile ?? defaultIndex);
                    }
                }

                var hashesprblock = blocksize / (blockhasher.HashSize / 8);

                // Number of blocks to combine into one
                int nBlocks = 2;
                if (args.Count == 4)
                {
                    if (!int.TryParse(args[3], out nBlocks))
                    {
                        Console.WriteLine("Invalid argument {0}, expected integer number or blocks to combine", args[3]);
                        return 100;
                    }
                    if (nBlocks < 1)
                    {
                        Console.WriteLine("Blocksize multiplier must be >= 1");
                        return 100;
                    }
                    else if ((nBlocks * blocksize) > int.MaxValue)
                    {
                        Console.WriteLine("Blocksize would be too large!");
                        return 100;
                    }
                }

                var newOptions = new Dictionary<string, string>(options)
                {
                    ["blocksize"] = (nBlocks * blocksize).ToString() + "B",
                    ["no-encryption"] = (!encrypt).ToString()
                };

                if (!CheckDestinationFolder(outputFolder, listFiles, out DateTime[] existingSets, nBlocks * (int)blocksize, new Options(newOptions)))
                {
                    Console.WriteLine("The destination folder is not empty and not compatible with the current backup.");
                    Console.WriteLine("Choose a different destination or clear the existing files in the folder.");
                    return 100;
                }
                else if (existingSets.Length > 0)
                {
                    Console.WriteLine("Keeping {0} existing filesets", existingSets.Length);
                }

                IEnumerable<KeyValuePair<string, long>> existing = null;
                if (Directory.Exists(outputFolder) && Directory.EnumerateFiles(outputFolder).Count() > 0)
                {
                    existing = LoadExistingBlocks(outputFolder, new Options(newOptions), hashsize, out int blocks);

                    Console.WriteLine("Mapped {0} existing blocks in output folder", blocks);
                }

                Console.WriteLine("Changing Blocksize: {0} -> {1}", blocksize, nBlocks * blocksize);
                using (var mru = new BackupRewriter.CompressedFileMRUCache(options))
                {
                    Console.WriteLine("Building lookup table for file hashes");
                    blockIndex.Cache = mru;
                    using (var processor = new BackupRewriter(newOptions, blocksize, blockIndex, outputFolder))
                    {
                        if (existing != null)
                        {
                            processor.AddBlockEntries(existing);
                            // Free memory
                            existing = null;
                        }
                        // Remove files already in output
                        // Oldest first
                        listFiles = listFiles.Where(p => !existingSets.Contains(p.Key)).Reverse().ToArray();
                        if (listFiles.Length == 0)
                        {
                            Console.WriteLine("No sets to convert");
                        }
                        for (int i = 0; i < listFiles.Length; ++i)
                        {
                            var listFile = listFiles[i];
                            Console.WriteLine("Processing set {0}/{1} with timestamp {2}", i, listFiles.Length, listFile.Key.ToLocalTime());

                            // order by hashes first to improve lookup
                            var files = (from f in EnumerateDList(listFile.Value, options)
                                         let sf = new SortedFileEntry(f)
                                         orderby sf.AllHashes
                                         select sf).ToList();
                            Console.WriteLine("Optimizing file lookup order");
                            files = (from sf in files
                                     orderby sf.LookupKey(blockIndex)
                                     select sf).ToList();

                            processor.ProcessListFile(files,
                                EnumerateDListControlFiles(listFile.Value, options), listFile.Key);
                        }
                    }
                }
            }

            return 0;
        }

        private static bool CheckDestinationFolder(string outputFolder, KeyValuePair<DateTime, string>[] listFiles, out DateTime[] existingFiles, int targetBlocksize, Options outputOptions)
        {
            if (!Directory.Exists(outputFolder))
            {
                existingFiles = Array.Empty<DateTime>();
                return true;
            }
            KeyValuePair<DateTime, string>[] outputListFiles = List.ParseListFiles(outputFolder);
            // Check that all output dates are also in the input
            using (var tmpdir = DecryptListFiles(outputListFiles, outputOptions))
                foreach (var p in outputListFiles)
                {
                    int i = Array.FindIndex(listFiles, p1 => p.Key == p1.Key);
                    if (i == -1)
                    {
                        // Output contains date not in input, not compatible
                        existingFiles = null;
                        return false;
                    }
                    else
                    {
                        // Check that input and output contain are equal except for hashes
                        string outputFilePath = p.Value;
                        if (tmpdir == null)
                        {
                            // Not decrypted, add output folder path
                            Path.Combine(outputFolder, listFiles[i].Value);
                        }
                        if (!CheckListFilesCompatible(listFiles[i].Value, outputFilePath, targetBlocksize))
                        {
                            existingFiles = null;
                            return false;
                        }
                    }
                }
            // All existing files are in input
            existingFiles = outputListFiles.Select(p => p.Key).ToArray();
            return true;
        }

        private static bool CheckListFilesCompatible(string inputFile, string outputFile, int targetBlocksize)
        {
            Options inputOptions = new Options(new Dictionary<string, string>());
            VolumeReaderBase.UpdateOptionsFromManifest(Path.GetExtension(inputFile).Trim('.'), inputFile, inputOptions);
            Options outputOptions = new Options(new Dictionary<string, string>());
            VolumeReaderBase.UpdateOptionsFromManifest(Path.GetExtension(outputFile).Trim('.'), outputFile, outputOptions);
            if (outputOptions.Blocksize != targetBlocksize)
            {
                return false;
            }
            // Check that both files contain the same paths (could also check hashes, but this should be good enough)
            var inputFiles = from f in EnumerateDList(inputFile, inputOptions.RawOptions) orderby f.Path select f.Path;
            var outputFiles = from f in EnumerateDList(outputFile, outputOptions.RawOptions) orderby f.Path select f.Path;

            return inputFiles.SequenceEqual(outputFiles);
        }

        class BlockIndex : BackupRewriter.IHashLookupHelper
        {
            Dictionary<string, string> m_fileMap;
            Dictionary<string, string[]> m_blocklists;

            public BackupRewriter.CompressedFileMRUCache Cache { get; set; }
            private readonly Options m_options;
            private readonly int m_hashsize;
            private readonly int m_blocksize;
            public int BlockCount { get => m_fileMap.Count; }
            public bool HasCachedBlocklists { get => m_blocklists != null; }

            public BlockIndex(Options options, int blocksize, int hashsize)
            {
                m_options = options;
                m_blocksize = blocksize;
                m_hashsize = hashsize;
            }

            public void CreateFromIndexFile(string indexfile)
            {
                using (var reader = new StreamReader(indexfile))
                {
                    m_fileMap = new Dictionary<string, string>(StringComparer.Ordinal);
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line == null)
                            break;

                        if (line.Length == 0)
                            continue;

                        var ix = line.IndexOf(", ", StringComparison.Ordinal);
                        if (ix < 0)
                            Console.WriteLine("Failed to parse line starting at offset {0} in index file, string: {1}", reader.BaseStream.Position - line.Length - Environment.NewLine.Length, line);
                        m_fileMap.Add(line.Substring(0, ix), line.Substring(ix + 2));

                    }
                }
            }

            public void CreateFromIndexVolumes(string folder)
            {
                m_fileMap = new Dictionary<string, string>(StringComparer.Ordinal);
                m_blocklists = new Dictionary<string, string[]>(StringComparer.Ordinal);
                ProcessIndexSet(folder, m_options, m_hashsize, reader =>
                {
                    foreach (var a in reader.Volumes)
                    {
                        foreach (var block in a.Blocks)
                        {
                            if (m_fileMap.TryGetValue(block.Key, out string existing))
                            {
                                if (existing != a.Filename)
                                {
                                    throw new Exception(string.Format("Incompatible block in index volumes, hash {0}, file {1} vs {2}",
                                        block.Key, a.Filename, existing));
                                }
                            }
                            else
                            {
                                m_fileMap.Add(block.Key, a.Filename);
                            }
                        }
                    }
                    foreach (var blocklist in reader.BlockLists)
                    {
                        string[] arr = blocklist.Blocklist.ToArray();
                        if (m_blocklists.TryGetValue(blocklist.Hash, out string[] existing))
                        {
                            if (!arr.SequenceEqual(existing, StringComparer.Ordinal))
                            {
                                throw new Exception(string.Format("Incompatible blocklist in index volumes, hash {0}", blocklist.Hash));
                            }
                        }
                        else
                        {
                            m_blocklists.Add(blocklist.Hash, arr);
                        }
                    }
                });
            }

            public string HashLocation(string hash)
            {
                if (m_fileMap.TryGetValue(hash, out string value))
                {
                    return value;
                }
                return null;
            }

            public IEnumerable<string> ReadBlocklistHashes(string hash)
            {
                if (m_blocklists != null)
                {
                    if (m_blocklists.TryGetValue(hash, out string[] result))
                    {
                        foreach (string r in result)
                            yield return r;
                        yield break;
                    }
                    throw new Exception(string.Format("Unable to locate block with hash: {0}", hash));
                }
                // Fallback for no cached blocklists
                var bytes = ReadHash(hash);
                for (var i = 0; i < bytes.Length; i += m_hashsize)
                    yield return Convert.ToBase64String(bytes, i, m_hashsize);
            }

            public byte[] ReadHash(string hash)
            {
                string file = HashLocation(hash);
                if (file != null)
                {
                    using (var fs = Cache.ReadBlock(file, hash))
                    {
                        var buf = new byte[m_blocksize];
                        var l = Library.Utility.Utility.ForceStreamRead(fs, buf, buf.Length);
                        Array.Resize(ref buf, l);
                        return buf;
                    }
                }

                throw new Exception(string.Format("Unable to locate block with hash: {0}", hash));
            }

            public void WriteHash(Stream s, string hash)
            {
                var h = ReadHash(hash);
                s.Write(h, 0, h.Length);
            }
        }

        class SortedFileEntry : Library.Main.Volumes.IFileEntry
        {
            public FilelistEntryType Type => m_parent.Type;

            public string TypeString => m_parent.TypeString;

            public string Path => m_parent.Path;

            public string Hash => m_parent.Hash;

            public long Size => m_parent.Size;

            public DateTime Time => m_parent.Time;

            public string Metahash => m_parent.Metahash;

            public string Metablockhash => m_parent.Metablockhash;

            public long Metasize => m_parent.Metasize;

            public string Blockhash => m_parent.Blockhash;

            public long Blocksize => m_parent.Blocksize;

            public IEnumerable<string> BlocklistHashes => m_blocklistHashes;

            public IEnumerable<string> MetaBlocklistHashes => m_metaBlocklistHashes;

            public string AllHashes { get; private set; }

            private Library.Main.Volumes.IFileEntry m_parent;
            // Have to save a copy because the base file entry is one-time read only
            private readonly IEnumerable<string> m_blocklistHashes = null;
            private readonly IEnumerable<string> m_metaBlocklistHashes = null;
            private readonly IEnumerable<string> m_hashes;
            private string m_lookupKey;

            public SortedFileEntry(Library.Main.Volumes.IFileEntry f)
            {
                m_parent = f;
                m_hashes = Enumerable.Empty<string>();
                if (f.BlocklistHashes != null)
                {
                    m_blocklistHashes = f.BlocklistHashes.ToList();
                    m_hashes = m_hashes.Concat(m_blocklistHashes);
                }
                else if (f.Size > 0 && (f.Blockhash != null || f.Hash != null))
                {
                    m_hashes = m_hashes.Append(f.Blockhash ?? f.Hash);
                }
                if (f.MetaBlocklistHashes != null)
                {
                    m_metaBlocklistHashes = f.MetaBlocklistHashes.ToList();
                    m_hashes = m_hashes.Concat(m_metaBlocklistHashes);
                }
                else if (f.Metasize > 0 && (f.Metablockhash != null || f.Metahash != null))
                {
                    m_hashes = m_hashes.Append(f.Metablockhash ?? f.Metahash);
                }
                AllHashes = string.Join(" ", m_hashes);
            }

            public string LookupKey(BackupRewriter.IHashLookupHelper lookup)
            {
                if (m_lookupKey == null)
                {
                    // Sort lookup files to group accesses
                    IEnumerable<string> files = (from h in m_hashes select lookup.HashLocation(h) into f orderby f select f).Distinct();
                    if (m_blocklistHashes != null && lookup.HasCachedBlocklists)
                    {
                        // Also lookup blocklists
                        IEnumerable<string> blocklistHashes = (from h in m_blocklistHashes select lookup.ReadBlocklistHashes(h))
                                                                           .Aggregate((acc, l) => acc.Concat(l));
                        files = files.Union(from h in blocklistHashes select lookup.HashLocation(h)).OrderBy(s => s);
                    }
                    m_lookupKey = string.Join(" ", files);
                }
                return m_lookupKey;
            }
        }

        private static TempFolder DecryptListFiles(KeyValuePair<DateTime, string>[] listFiles, Options opt)
        {
            // Temporary folder for decrypted list files, if necessary
            TempFolder tmpDir = null;
            try
            {
                bool updatedOptions = false;
                for (int i = 0; i < listFiles.Length; ++i)
                {
                    var path = listFiles[i].Value;
                    var p = VolumeBase.ParseFilename(Path.GetFileName(path));
                    if (p.EncryptionModule != null)
                    {
                        using (var m = Library.DynamicLoader.EncryptionLoader.GetModule(p.EncryptionModule, opt.Passphrase, opt.RawOptions))
                        {
                            if (m == null)
                            {
                                throw new UserInformationException(string.Format("Encryption method not supported: {0}", p.EncryptionModule), "EncryptionMethodNotSupported");
                            }
                            if (tmpDir == null)
                            {
                                tmpDir = new TempFolder();
                            }
                            // Remove encryption from filename
                            string decryptFilename = Path.GetFileName(path);
                            decryptFilename = decryptFilename.Substring(0, decryptFilename.Length - m.FilenameExtension.Length - 1);
                            string decryptPath = Path.Combine(tmpDir, decryptFilename);
                            m.Decrypt(path, decryptPath);
                            listFiles[i] = new KeyValuePair<DateTime, string>(listFiles[i].Key, decryptPath);
                            path = decryptPath;
                        }
                    }
                    if (!updatedOptions)
                    {
                        VolumeReaderBase.UpdateOptionsFromManifest(p.CompressionModule, path, opt);
                        updatedOptions = true;
                    }
                }
            }
            catch
            {
                tmpDir?.Dispose();
                throw;
            }
            return tmpDir;
        }

        private static Dictionary<string, long> LoadExistingBlocks(string folder, Options options, long hashsize, out int blocks)
        {
            var existingBlocks = new Dictionary<string, long>(StringComparer.Ordinal);
            int b = 0;
            ProcessIndexSet(folder, options, hashsize,
                reader =>
                {
                    foreach (var a in reader.Volumes)
                    {
                        foreach (var block in a.Blocks)
                        {
                            existingBlocks.Add(block.Key, block.Value);
                            b++;
                        }
                    }
                }
                );
            blocks = b;
            return existingBlocks;
        }

        private static void ProcessIndexSet(string folder, Options options, long hashsize, Action<IndexVolumeReader> func)
        {
            var indexVolumes = (
                    from v in Directory.EnumerateFiles(folder)
                    let p = VolumeBase.ParseFilename(Path.GetFileName(v))
                    where p != null && p.FileType == RemoteVolumeType.Index
                    orderby p.Time descending
                    select new KeyValuePair<IParsedVolume, string>(p, v)).ToArray();

            if (indexVolumes.Length == 0)
            {
                return;
            }

            bool decrypt = indexVolumes.Any((p) => p.Key.EncryptionModule != null);
            using (var tf = decrypt ? new TempFile() : null)
                foreach (var v in indexVolumes)
                {
                    var path = v.Value;
                    var p = v.Key;
                    if (p.EncryptionModule != null)
                    {
                        // Decrypt to temp file
                        using (var m = Library.DynamicLoader.EncryptionLoader.GetModule(p.EncryptionModule, options.Passphrase, options.RawOptions))
                        {
                            if (m == null)
                            {
                                throw new UserInformationException(string.Format("Encryption method not supported: {0}", p.EncryptionModule), "EncryptionMethodNotSupported");
                            }
                            m.Decrypt(path, tf);
                            path = tf;
                        }
                    }
                    using (var reader = new IndexVolumeReader(p.CompressionModule, path, options, hashsize))
                        func(reader);
                }
        }

        private static void EncryptVolumes(Dictionary<string, string> options, string outputFolder)
        {
            options.Remove("no-encryption");
            Options opt = new Options(options);
            if (string.IsNullOrEmpty(opt.EncryptionModule))
            {
                Console.WriteLine("Encryption module set to none");
                return;
            }
            Console.WriteLine("Encrypting: {0}", opt.EncryptionModule);
            var localfiles =
               (from x in Directory.EnumerateFiles(outputFolder)
                let n = VolumeBase.ParseFilename(SystemIO.IO_OS.FileEntry(x))
                where n != null && n.Prefix == opt.Prefix
                select n).ToArray(); //ToArray() ensures that we do not remote-request it multiple times

            // Needs order (Files or Blocks) and Indexes as last because indexes content will be adjusted based on recompressed blocks
            var files = localfiles.Where(a => a.FileType == RemoteVolumeType.Files).ToArray();
            var blocks = localfiles.Where(a => a.FileType == RemoteVolumeType.Blocks).ToArray();
            var indexes = localfiles.Where(a => a.FileType == RemoteVolumeType.Index).ToArray();

            localfiles = files.Concat(blocks).ToArray().Concat(indexes).ToArray();


            int i = 0;
            foreach (var f in localfiles)
            {
                Console.Write("{0}/{1}: {2}", ++i, localfiles.Count(), f.File.Name);
                string localFileSource = Path.Combine(outputFolder, f.File.Name);
                string localFileTarget = localFileSource;
                File.Move(localFileSource, localFileSource + ".same");
                localFileSource += ".same";

                using (var localFileSourceStream = new System.IO.FileStream(localFileSource, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var cmOld = Library.DynamicLoader.CompressionLoader.GetModule(opt.CompressionModule, localFileSourceStream, ArchiveMode.Read, options))
                using (var localFileTargetStream = new FileStream(localFileTarget, FileMode.Create, FileAccess.Write, FileShare.Delete))
                using (var cmNew = Library.DynamicLoader.CompressionLoader.GetModule(opt.CompressionModule, localFileTargetStream, ArchiveMode.Write, options))
                    foreach (var cmfile in cmOld.ListFiles(""))
                    {
                        string cmfileNew = cmfile;
                        var cmFileVolume = VolumeBase.ParseFilename(cmfileNew);

                        if (f.FileType == RemoteVolumeType.Index && cmFileVolume != null && cmFileVolume.FileType == RemoteVolumeType.Blocks)
                        {
                            // Correct inner filename extension with encryption
                            if (cmfileNew.EndsWith("." + cmFileVolume.CompressionModule))
                            {
                                cmfileNew = cmfileNew + "." + opt.EncryptionModule;
                            }

                            //Because encryption changes blocks file sizes - needs to be updated
                            string textJSON;
                            using (var sourceStream = cmOld.OpenRead(cmfile))
                            using (var sourceStreamReader = new StreamReader(sourceStream))
                            {
                                textJSON = sourceStreamReader.ReadToEnd();
                                JToken token = JObject.Parse(textJSON);
                                var fileInfoBlocks = new FileInfo(Path.Combine(outputFolder, cmfileNew.Replace("vol/", "")));
                                var filehasher = HashAlgorithmHelper.Create(opt.FileHashAlgorithm);

                                using (var fileStream = fileInfoBlocks.Open(FileMode.Open))
                                {
                                    fileStream.Position = 0;
                                    token["volumehash"] = Convert.ToBase64String(filehasher.ComputeHash(fileStream));
                                    fileStream.Close();
                                }

                                token["volumesize"] = fileInfoBlocks.Length;
                                textJSON = token.ToString();
                            }

                            using (var sourceStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(textJSON)))
                            using (var cs = cmNew.CreateFile(cmfileNew, Library.Interface.CompressionHint.Compressible, cmOld.GetLastWriteTime(cmfile)))
                                Library.Utility.Utility.CopyStream(sourceStream, cs);

                        }
                        else
                        {
                            using (var sourceStream = cmOld.OpenRead(cmfile))
                            using (var cs = cmNew.CreateFile(cmfileNew, CompressionHint.Compressible, cmOld.GetLastWriteTime(cmfile)))
                                Library.Utility.Utility.CopyStream(sourceStream, cs);
                        }
                    }

                File.Delete(localFileSource);
                using (var m = Library.DynamicLoader.EncryptionLoader.GetModule(opt.EncryptionModule, opt.Passphrase, options))
                {
                    if (m == null)
                    {
                        throw new UserInformationException(string.Format("Encryption method not supported: {0}", opt.EncryptionModule), "EncryptionMethodNotSupported");
                    }
                    m.Encrypt(localFileTarget, localFileTarget + "." + m.FilenameExtension);
                    File.Delete(localFileTarget);
                    localFileTarget = localFileTarget + "." + m.FilenameExtension;
                }

                Console.WriteLine(" done!");
            }
            Console.WriteLine("Finished encrypting {0} files", i);
        }

        public static IEnumerable<Duplicati.Library.Main.Volumes.IFileEntry> EnumerateDList(string file, Dictionary<string, string> options)
        {
            var p = Library.Main.Volumes.VolumeBase.ParseFilename(Path.GetFileName(file));
            using (var fs = new System.IO.FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var cm = Library.DynamicLoader.CompressionLoader.GetModule(p.CompressionModule, fs, Library.Interface.ArchiveMode.Read, options))
            using (var filesetreader = new Library.Main.Volumes.FilesetVolumeReader(cm, new Duplicati.Library.Main.Options(options)))
                foreach (var f in filesetreader.Files)
                {
                    yield return f;
                }
        }

        public static IEnumerable<KeyValuePair<string, Stream>> EnumerateDListControlFiles(string file, Dictionary<string, string> options)
        {
            var p = Library.Main.Volumes.VolumeBase.ParseFilename(Path.GetFileName(file));
            using (var fs = new System.IO.FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var cm = Library.DynamicLoader.CompressionLoader.GetModule(p.CompressionModule, fs, Library.Interface.ArchiveMode.Read, options))
            using (var filesetreader = new Library.Main.Volumes.FilesetVolumeReader(cm, new Duplicati.Library.Main.Options(options)))
                foreach (var f in filesetreader.ControlFiles)
                {
                    yield return f;
                }
        }

    }
}
