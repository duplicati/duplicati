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

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace RemoteSynchronization
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var src_arg = new Argument<string>(name: "backend_src", description: "The source backend string");
            var dst_arg = new Argument<string>(name: "backend_dst", description: "The destination backend string");
            var dry_run_opt = new Option<bool>(aliases: ["--dry-run", "-d"], description: "Do not actually write or delete files");

            var root_cmd = new RootCommand("Remote Synchronization Tool");
            root_cmd.AddArgument(src_arg);
            root_cmd.AddArgument(dst_arg);
            root_cmd.AddOption(dry_run_opt);

            root_cmd.SetHandler((InvocationContext ctx) =>
            {
                var parsed = ctx.ParseResult;

                Dictionary<string, object?> options = parsed.CommandResult.Command.Options.ToDictionary(x => x.Name, x => parsed.GetValueForOption(x));

                Run(parsed.GetValueForArgument(src_arg), parsed.GetValueForArgument(dst_arg), options).Wait();
            });

            return await root_cmd.InvokeAsync(args);
        }

        private static async Task<int> Run(string src, string dst, Dictionary<string, object?> options)
        {
            var dry_run = options["dry-run"] as bool? ?? false;
            var duplicati_options = new Dictionary<string, string>()
            {
                ["dry-run"] = dry_run.ToString()
            };

            using var b1 = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(src, duplicati_options);
            var b1s = b1 as IStreamingBackend;
            System.Diagnostics.Debug.Assert(b1s != null);

            using var b2 = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(dst, duplicati_options);
            var b2s = b2 as IStreamingBackend;
            System.Diagnostics.Debug.Assert(b2s != null);

            var (to_copy, to_delete) = PrepareFileLists(b1s, b2s);
            var deleted = await DeleteAsync(b2s, to_delete, dry_run);
            Console.WriteLine($"Deleted {deleted} files from {dst}");
            var copied = await CopyAsync(b1s, b2s, to_copy, dry_run);
            Console.WriteLine($"Copied {copied} files from {src} to {dst}");

            return 0;
        }

        // TODO maximum retention?: don't delete files, only rename old ones.
        // TODO have concurrency parameters: uploaders, downloaders
        // TODO low memory mode, where things aren't kept in memory. Maybe utilize SQLite?
        // TODO Force parameter
        // TODO Progress reporting
        // TODO Logging

        // check database consistency. I.e. have both databases, check that the block, volume, files, etc match up.
        // introduce these checks as a post processing step? Especially the database consistency check, as that is often recreated from the index files.
        // TODO This tool shouldn't handle it, but for convenience, it should support making the seperate call to the regular Duplicati on the destination backend, which alread carries this functionality.

        // Forcefully synchronize the remote backends
        private static async Task<long> CopyAsync(IStreamingBackend b_src, IStreamingBackend b_dst, IEnumerable<IFileEntry> files, bool dry_run)
        {
            long successful_copies = 0;
            using var s = new MemoryStream();
            foreach (var f in files)
            {
                try
                {
                    await b_src.GetAsync(f.Name, s, CancellationToken.None);
                    if (dry_run)
                    {
                        Console.WriteLine($"Would write {s.Length} bytes of {f.Name} to {b_dst.DisplayName}");
                    }
                    else
                    {
                        await b_dst.PutAsync(f.Name, s, CancellationToken.None);
                    }
                    s.SetLength(0);
                    successful_copies++;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error copying {f.Name}: {e.Message}");
                }
            }
            return successful_copies;
        }

        private static async Task<long> DeleteAsync(IStreamingBackend b, IEnumerable<IFileEntry> files, bool dry_run)
        {
            long successful_deletes = 0;
            foreach (var f in files)
            {
                try
                {
                    if (dry_run)
                    {
                        Console.WriteLine($"Would delete {f.Name} from {b.DisplayName}");
                    }
                    else
                    {
                        await b.DeleteAsync(f.Name, CancellationToken.None);
                    }
                    successful_deletes++;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error deleting {f.Name}: {e.Message}");
                }
            }
            return successful_deletes;
        }

        private static (IEnumerable<IFileEntry>, IEnumerable<IFileEntry>) PrepareFileLists(IStreamingBackend b_src, IStreamingBackend b_dst)
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

        private static async Task<long> RenameAsync(IStreamingBackend b, IEnumerable<FileEntry> files, bool dry_run)
        {
            long successful_renames = 0;
            string suffix = $"{DateTime.Now:yyyyMMddHHmmss}.old";
            using var downloaded = new MemoryStream();
            foreach (var f in files)
            {
                try
                {
                    await b.GetAsync(f.Name, downloaded, CancellationToken.None);
                    if (dry_run)
                    {
                        Console.WriteLine($"Would rename {f.Name} to {f.Name}.{suffix} by deleting and re-uploading {downloaded.Length} bytes to {b.DisplayName}");
                    }
                    else
                    {
                        await b.PutAsync($"{f.Name}.{suffix}", downloaded, CancellationToken.None);
                        await b.DeleteAsync(f.Name, CancellationToken.None);
                    }
                    downloaded.SetLength(0);
                    successful_renames++;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error renaming {f.Name}: {e.Message}");
                }
            }
            return successful_renames;
        }

    }
}
