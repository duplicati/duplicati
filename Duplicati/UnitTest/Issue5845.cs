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
using NUnit.Framework;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.DynamicLoader;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace Duplicati.UnitTest
{
    public class Issue5845 : BasicSetupHelper
    {
        private class ListSortingBackend : IStreamingBackend
        {
            public const string Key = "test-sorter-5845";
            public string DisplayName => "Test sorter";
            public string ProtocolKey => Key;
            public string Description => "Test sorter";
            public IList<ICommandLineArgument> SupportedCommands => backend.SupportedCommands;
            private readonly IStreamingBackend backend;

            public static string RealKey { get; set; }
            public static Func<IEnumerable<IFileEntry>, IEnumerable<IFileEntry>> Sorter { get; set; }

            public ListSortingBackend()
            {
                backend = (IStreamingBackend)BackendLoader.Backends.First(x => x.ProtocolKey == RealKey);
            }

            public ListSortingBackend(string url, Dictionary<string, string> opts)
            {
                backend = (IStreamingBackend)BackendLoader.GetBackend(RealKey + url.Substring(ProtocolKey.Length), opts);
            }

            public Task CreateFolderAsync(CancellationToken cancellationToken)
                => backend.CreateFolderAsync(cancellationToken);

            public Task DeleteAsync(string remotename, CancellationToken cancellationToken)
                => backend.DeleteAsync(remotename, cancellationToken);
            public void Dispose()
                => backend.Dispose();
            public Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
                => backend.GetAsync(remotename, stream, cancelToken);
            public Task GetAsync(string remotename, string filename, CancellationToken cancellationToken)
                => backend.GetAsync(remotename, filename, cancellationToken);
            public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
                => GetDNSNamesAsync(cancelToken);
            public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
                => Sorter(backend.ListAsync(cancellationToken).ToBlockingEnumerable()).ToAsyncEnumerable();

            public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
                => backend.PutAsync(remotename, stream, cancelToken);

            public Task PutAsync(string remotename, string filename, CancellationToken cancellationToken)
                => backend.PutAsync(remotename, filename, cancellationToken);

            public Task TestAsync(CancellationToken cancellationToken)
                => backend.TestAsync(cancellationToken);
        }

        [Test]
        [Category("Targeted")]
        public void TestDuplicatedBlocksInOrphanIndex()
        {
            var testopts = TestOptions;
            testopts.Add("no-encryption", "true");

            var opts = new Library.Main.Options(testopts);

            // Make a backup
            var data = new byte[1024 * 1024 * 10];
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new string[] { DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            var blockfilename = Directory.EnumerateFiles(TARGETFOLDER, $"*.dblock.{opts.CompressionModule}").First();
            var indexfilename = Directory.EnumerateFiles(TARGETFOLDER, $"*.dindex.{opts.CompressionModule}").First();

            (string indexfile, string blockfile) CreateDuplicatedFiles()
            {
                // Create a copy of the block file
                var newblockfilename = Path.Combine(TARGETFOLDER, Library.Main.Volumes.VolumeBase.GenerateFilename(Library.Main.RemoteVolumeType.Blocks, opts.Prefix, Library.Main.Volumes.VolumeWriterBase.GenerateGuid(), DateTime.Now, opts.CompressionModule, opts.EncryptionModule));
                if (File.Exists(newblockfilename))
                    File.Delete(newblockfilename);
                File.Copy(blockfilename, newblockfilename);

                // Create a duplicated index file
                var newindexfilename = Path.Combine(TARGETFOLDER, Library.Main.Volumes.VolumeBase.GenerateFilename(Library.Main.RemoteVolumeType.Index, opts.Prefix, Library.Main.Volumes.VolumeWriterBase.GenerateGuid(), DateTime.Now, opts.CompressionModule, opts.EncryptionModule));
                if (File.Exists(newindexfilename))
                    File.Delete(newindexfilename);

                using (var writer = new Library.Main.Volumes.IndexVolumeWriter(opts))
                using (var fs = File.OpenRead(indexfilename))
                using (var cmp = CompressionLoader.GetModule(opts.CompressionModule, fs, ArchiveMode.Read, testopts))
                using (var reader = new Library.Main.Volumes.IndexVolumeReader(cmp, opts, opts.BlockhashSize))
                {
                    foreach (var v in reader.Volumes)
                    {
                        writer.StartVolume(v.Filename == Path.GetFileName(blockfilename) ? Path.GetFileName(newblockfilename) : v.Filename);
                        foreach (var b in v.Blocks)
                            writer.AddBlock(b.Key, b.Value);
                        writer.FinishVolume(v.Hash, v.Length);
                    }

                    foreach (var bl in reader.BlockLists)
                        writer.WriteBlocklist(bl.Hash, bl.Data);

                    writer.Close();
                    File.Copy(writer.LocalFilename, newindexfilename);
                }

                return (newindexfilename, newblockfilename);
            }

            var set1 = CreateDuplicatedFiles();
            var set2 = CreateDuplicatedFiles();

            File.Delete(set1.blockfile);
            File.Delete(set2.blockfile);

            // Make sure the index files are returned in this order
            var requiredOrder = new[] {
                Path.GetFileName(set1.indexfile),
                Path.GetFileName(set2.indexfile),
                Path.GetFileName(indexfilename),
            };

            ListSortingBackend.RealKey = "file";
            ListSortingBackend.Sorter = files =>
            {
                var lst = files.ToList();
                var entries = requiredOrder.Select(x => lst.First(y => y.Name == x)).ToList();
                return lst.Except(entries).Concat(entries).ToList();
            };
            BackendLoader.AddBackend(new ListSortingBackend());

            // Delete the local database, and recreate
            File.Delete(DBFILE);
            using (var c = new Library.Main.Controller(ListSortingBackend.Key + "://" + TARGETFOLDER, testopts, null))
            {
                IRepairResults repairResults = c.Repair();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(3, repairResults.Warnings.Count());
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IListResults listResults = c.List();
                Assert.AreEqual(0, listResults.Errors.Count());
                Assert.AreEqual(0, listResults.Warnings.Count());
                Assert.AreEqual(listResults.Filesets.Count(), 1);
            }
        }

        [Test]
        [Category("Targeted")]
        public void TestReplicatedBlocksInOrphanIndex()
        {
            var replicas = 4;
            var goodExtras = 1;
            var testopts = TestOptions;
            testopts.Add("no-encryption", "true");

            var opts = new Library.Main.Options(testopts);

            // Make a backup
            var data = new byte[1024 * 1024 * 10];
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new string[] { DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            var blockfilename = Directory.EnumerateFiles(TARGETFOLDER, $"*.dblock.{opts.CompressionModule}").First();
            var indexfilename = Directory.EnumerateFiles(TARGETFOLDER, $"*.dindex.{opts.CompressionModule}").First();

            (string indexfile, string blockfile) CreateDuplicatedFiles()
            {
                // Create a copy of the block file
                var newblockfilename = Path.Combine(TARGETFOLDER, Library.Main.Volumes.VolumeBase.GenerateFilename(Library.Main.RemoteVolumeType.Blocks, opts.Prefix, Library.Main.Volumes.VolumeWriterBase.GenerateGuid(), DateTime.Now, opts.CompressionModule, opts.EncryptionModule));
                if (File.Exists(newblockfilename))
                    File.Delete(newblockfilename);
                File.Copy(blockfilename, newblockfilename);

                // Create a duplicated index file
                var newindexfilename = Path.Combine(TARGETFOLDER, Library.Main.Volumes.VolumeBase.GenerateFilename(Library.Main.RemoteVolumeType.Index, opts.Prefix, Library.Main.Volumes.VolumeWriterBase.GenerateGuid(), DateTime.Now, opts.CompressionModule, opts.EncryptionModule));
                if (File.Exists(newindexfilename))
                    File.Delete(newindexfilename);

                using (var writer = new Library.Main.Volumes.IndexVolumeWriter(opts))
                using (var fs = File.OpenRead(indexfilename))
                using (var cmp = CompressionLoader.GetModule(opts.CompressionModule, fs, ArchiveMode.Read, testopts))
                using (var reader = new Library.Main.Volumes.IndexVolumeReader(cmp, opts, opts.BlockhashSize))
                {
                    foreach (var v in reader.Volumes)
                    {
                        writer.StartVolume(v.Filename == Path.GetFileName(blockfilename) ? Path.GetFileName(newblockfilename) : v.Filename);
                        foreach (var b in v.Blocks)
                            writer.AddBlock(b.Key, b.Value);
                        writer.FinishVolume(v.Hash, v.Length);
                    }

                    foreach (var bl in reader.BlockLists)
                        writer.WriteBlocklist(bl.Hash, bl.Data);

                    writer.Close();
                    File.Copy(writer.LocalFilename, newindexfilename);
                }

                return (newindexfilename, newblockfilename);
            }

            var sets = Enumerable.Range(0, replicas).Select(x => CreateDuplicatedFiles()).ToList();
            sets.Skip(goodExtras).ToList().ForEach(x => File.Delete(x.blockfile));

            // Make sure the index files that point to non-existing blocks are returned first
            var mustBeLastOrder = sets.Take(goodExtras).Select(x => Path.GetFileName(x.indexfile))
                .Append(Path.GetFileName(indexfilename))
                .ToList();

            ListSortingBackend.RealKey = "file";
            ListSortingBackend.Sorter = files =>
            {
                var lst = files.ToList();
                var entries = mustBeLastOrder.Select(x => lst.First(y => y.Name == x)).ToList();
                return lst.Except(entries).Concat(entries).ToList();
            };
            BackendLoader.AddBackend(new ListSortingBackend());

            // Delete the local database, and recreate
            File.Delete(DBFILE);
            using (var c = new Library.Main.Controller(ListSortingBackend.Key + "://" + TARGETFOLDER, testopts, null))
            {
                IRepairResults repairResults = c.Repair();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(1 + replicas - goodExtras, repairResults.Warnings.Count());
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IListResults listResults = c.List();
                Assert.AreEqual(0, listResults.Errors.Count());
                Assert.AreEqual(0, listResults.Warnings.Count());
                Assert.AreEqual(1, listResults.Filesets.Count());
            }
        }
    }
}

