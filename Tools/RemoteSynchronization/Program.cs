using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Main;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System.IO;
using System.Threading;


namespace RemoteSynchronization
{
    class Program
    {
        static void Main(string[] args)
        {
            // TODO testing start
            var home = Environment.GetEnvironmentVariable("HOME");
            var base_path = $"{home}/git/duplicati-carl/rsync";
            var l1 = $"{base_path}/l1";
            var l2 = $"{base_path}/l2";
            var l1r = $"{base_path}/l1_restore";
            var l2r = $"{base_path}/l2_restore";
            var backup_path = $"{home}/tmp/adaptivecpp";

            Dictionary<string, string> options = new (){
                ["passphrase"] = "1234"
            };

            // Create the directories if they do not exist
            foreach (var p in new string[] { base_path, l1, l2, l1r, l2r })
            {
                if (!SystemIO.IO_OS.DirectoryExists(p))
                    SystemIO.IO_OS.DirectoryCreate(p);
            }

            // Backup the first level
            using (var c = new Controller($"file://{l1}", options, null))
            {
                var results = c.Backup([backup_path]);
                Console.WriteLine($"Backed up {results.AddedFiles} files to {l1}");
            }

            // TODO testing end

            // Sync the first level to the second level
            using (var b1 = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend($"file://{l1}", options))
            {
                var b1s = b1 as IStreamingBackend;
                System.Diagnostics.Debug.Assert(b1s != null);
                using (var b2 = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend($"file://{l2}", options))
                {
                    var b2s = b2 as IStreamingBackend;
                    System.Diagnostics.Debug.Assert(b2s != null);
                    var (to_copy, to_delete) = PrepareFileLists(b1s, b2s);
                    var copied = Copy(b1s, b2s, to_copy);
                    Console.WriteLine($"Copied {copied} files from {l1} to {l2}");
                }
            }

            // TODO testing start
            // Try to restore the first level
            options["restore-path"] = l1r;
            using (var c = new Controller($"file://{l1}", options, null))
            {
                var results = c.Restore([Path.Combine(backup_path, "*")]);
                Console.WriteLine($"Restored {results.RestoredFiles} files to {options["restore-path"]}");
            }

            // Try to restore the second level
            options["restore-path"] = l2r;
            using (var c = new Controller($"file://{l2}", options, null))
            {
                var results = c.Restore([]);
                Console.WriteLine($"Restored {results.RestoredFiles} files to {options["restore-path"]}");
            }
            // TODO testing end
        }

        // TODO check whether a backend is empty
        // TODO check whether the backend versions "match": src.version >= dst.version
        // TODO check whether the backend files are consistent: src.files.count >= dst.files.count
        // TODO handle files deleted from src, which should also be deleted in dst
        // TODO maximum retention?: don't delete files, only rename old ones.
        // TODO check database consistency. I.e. have both databases, check that the block, volume, files, etc match up.
        // TODO introduce these checks as a post processing step? Especially the database consistency check, as that is often recreated from the index files.

        // Forcefully synchronize the remote backends
        private static long Copy(IStreamingBackend b_src, IStreamingBackend b_dst, IEnumerable<IFileEntry> files)
        {
            long successful_copies = 0;
            foreach (var f in files)
            {
                try
                {
                    var s = new MemoryStream();
                    var s1 = b_src.GetAsync(f.Name, s, CancellationToken.None);
                    s1.Wait();
                    var s2 = b_dst.PutAsync(f.Name, s, CancellationToken.None);
                    s2.Wait();
                    successful_copies++;
                }
                catch (Exception e)
                {
                    // TODO log the error
                    // TODO return failed files count?
                    Console.WriteLine($"Error copying {f.Name}: {e.Message}");
                }
            }
            return successful_copies;
        }

        private static (IEnumerable<IFileEntry>,IEnumerable<IFileEntry>) PrepareFileLists(IStreamingBackend b_src, IStreamingBackend b_dst)
        {
            var files_src = b_src.List();
            var files_dst = b_dst.List();

            // Shortcut for empty destination
            if (!files_dst.Any())
            {
                return (files_src, []);
            }

            var lookup_src = files_src.ToDictionary(x => x.Name);
            var lookup_dst = files_dst.ToDictionary(x => x.Name);

            var to_copy = new List<IFileEntry>();
            var to_delete = new HashSet<string>();

            // Find all of the files in src that are not in dst, have a different size or have a more recent modification date
            foreach (var f_src in files_src)
            {
                if (lookup_dst.TryGetValue(f_src.Name, out var f_dst))
                {
                    if (f_src.Size != f_dst.Size || f_src.LastModification > f_dst.LastModification)
                    {
                        to_copy.Add(f_src);
                        to_delete.Add(f_dst.Name);
                    }
                }
                else
                {
                    to_copy.Add(f_src);
                }
            }

            // Find all of the files in dst that are not in src
            foreach (var f_dst in files_dst)
            {
                if (to_delete.Contains(f_dst.Name))
                    continue;

                if (!lookup_src.ContainsKey(f_dst.Name))
                {
                    to_delete.Add(f_dst.Name);
                }
            }

            return (to_copy, to_delete.Select(x => lookup_dst[x]));
        }
    }
}
