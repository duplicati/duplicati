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

            string ixfile;
            options.TryGetValue("indexfile", out ixfile);
            if (string.IsNullOrWhiteSpace(ixfile))
                ixfile = "index.txt";

            var listFiles = List.ParseListFiles(folder);

            bool encrypt = Library.Utility.Utility.ParseBoolOption(options, "encrypt");

            if (!options.ContainsKey("passphrase"))
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PASSPHRASE")))
                    options["passphrase"] = Environment.GetEnvironmentVariable("PASSPHRASE");

            Options opt = new Options(options);

            using (var tdir = DecryptListFiles(listFiles, opt))
            {
                if (encrypt)
                {
                    if (!options.ContainsKey("passphrase"))
                    {
                        Console.WriteLine("Password not specified for reencrypt");
                        return 100;
                    }
                }

                ixfile = Path.GetFullPath(ixfile);

                string blocksize_str;
                options.TryGetValue("blocksize", out blocksize_str);
                string blockhash_str;
                options.TryGetValue("block-hash-algorithm", out blockhash_str);
                string filehash_str;
                options.TryGetValue("block-hash-algorithm", out filehash_str);
                long blocksize = string.IsNullOrWhiteSpace(blocksize_str) ? 0 : Library.Utility.Sizeparser.ParseSize(blocksize_str);

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


                if (!File.Exists(ixfile))
                {
                    Console.WriteLine("Index file not found, creating from index volumes");
                    if (!CreateIndexFile(folder, ixfile, opt, hashsize))
                    {
                        Console.WriteLine("No dindex files to create index, need to use index command manually!");
                        return 100;
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
                    ["no-encryption"] = "true"
                };
                Console.WriteLine("Changing Blocksize: {0} -> {1}", blocksize, nBlocks * blocksize);
                using (var mru = new BackupRewriter.CompressedFileMRUCache(options))
                {


                    Console.WriteLine("Building lookup table for file hashes");
                    using (BackupRewriter.HashLookupHelper lookup = new BackupRewriter.HashLookupHelper(ixfile, mru, (int)blocksize, blockhasher.HashSize / 8))
                    using (var processor = new BackupRewriter(newOptions, blocksize, hashesprblock, lookup, outputFolder))
                    {
                        // Oldest first
                        Array.Reverse(listFiles);
                        foreach (var listFile in listFiles)
                        {
                            Console.WriteLine("Processing set with timestamp {0}", listFile.Key.ToLocalTime());

                            processor.ProcessListFile(EnumerateDList(listFile.Value, options),
                                EnumerateDListControlFiles(listFile.Value, options), listFile.Key);
                        }
                    }
                }
            }

            if (encrypt)
            {
                EncryptVolumes(options, outputFolder);
            }
            return 0;
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

        private static bool CreateIndexFile(string folder, string ixfile, Options options, long hashsize)
        {
            var indexVolumes = (
                    from v in Directory.EnumerateFiles(folder)
                    let p = VolumeBase.ParseFilename(Path.GetFileName(v))
                    where p != null && p.FileType == RemoteVolumeType.Index
                    orderby p.Time descending
                    select new KeyValuePair<IParsedVolume, string>(p, v)).ToArray();

            if (indexVolumes.Length == 0)
            {
                return false;
            }
            SortedSet<string> sortedIndices = new SortedSet<string>(StringComparer.Ordinal);

            int blocks = 0;
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
                        foreach (var a in reader.Volumes)
                        {
                            foreach (var block in a.Blocks)
                            {
                                sortedIndices.Add($"{block.Key}, {a.Filename}");
                                blocks++;
                            }
                        }
                }
            using (var sw = new StreamWriter(ixfile))
            {
                foreach (string line in sortedIndices)
                {
                    sw.WriteLine(line);
                }
            }
            Console.WriteLine("{0} hashes indexed", blocks);
            return true;
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
