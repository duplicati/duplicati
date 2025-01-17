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

            // Sync the first level to the second level
            long count = 0;
            using (var b1 = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend($"file://{l1}", options))
            {
                var b1s = b1 as IStreamingBackend;
                System.Diagnostics.Debug.Assert(b1s != null);
                using (var b2 = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend($"file://{l2}", options))
                {
                    var b2s = b2 as IStreamingBackend;
                    System.Diagnostics.Debug.Assert(b2s != null);
                    var files1 = b1s.List();
                    Console.WriteLine($"Found {files1.Count()} remote files in {l1}");
                    var files2 = b2s.List();
                    Console.WriteLine($"Found {files2.Count()} remote files in {l2}");
                    if (files1.Count() >= files2.Count())
                    {
                        foreach (var f1 in files1)
                        {
                            if (!files2.Any(f2 => f2.Name == f1.Name))
                            {
                                var s = new MemoryStream();
                                var s1 = b1s.GetAsync(f1.Name, s, CancellationToken.None);
                                s1.Wait();
                                var s2 = b2s.PutAsync(f1.Name, s, CancellationToken.None);
                                s2.Wait();
                                count++;
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Error: l1 has less files than l2 - something is wrong");
                    }
                }
            }
            Console.WriteLine($"Synced {count} files from {l1} to {l2}");

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
        }
    }
}
