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
        private const bool DEFAULT_VERIFY = false;

        public static async Task<int> Main(string[] args)
        {
            var src_arg = new Argument<string>(name: "backend_src", description: "The source backend string");
            var dst_arg = new Argument<string>(name: "backend_dst", description: "The destination backend string");
            var dry_run_opt = new Option<bool>(aliases: ["--dry-run", "-d"], description: "Do not actually write or delete files");
            var src_opts = OptionWithMultipleTokens(aliases: ["--src-options"], description: "Options for the source backend");
            var dst_opts = OptionWithMultipleTokens(aliases: ["--dst-options"], description: "Options for the destination backend");
            var verify_opt = new Option<bool>(aliases: ["--verify"], description: "Verify the files after copying", getDefaultValue: () => DEFAULT_VERIFY);

            var root_cmd = new RootCommand("Remote Synchronization Tool");
            root_cmd.AddArgument(src_arg);
            root_cmd.AddArgument(dst_arg);
            root_cmd.AddOption(dry_run_opt);
            root_cmd.AddOption(src_opts);
            root_cmd.AddOption(dst_opts);
            root_cmd.AddOption(verify_opt);

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
            var verify = options["verify"] as bool? ?? DEFAULT_VERIFY;
            var duplicati_options = new Dictionary<string, string>()
            {
                ["dry-run"] = dry_run.ToString()
            };
            Dictionary<string, string> src_opts = (options["src-options"] as List<string>)
                ?.Select(x => x.Split("="))
                .ToDictionary(x => x[0], x => x[1])
                ?? [];
            Dictionary<string, string> dst_opts = (options["dst-options"] as List<string>)
                ?.Select(x => x.Split("="))
                .ToDictionary(x => x[0], x => x[1])
                ?? [];
            foreach (var x in duplicati_options)
                src_opts[x.Key] = dst_opts[x.Key] = x.Value;

            using var b1 = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(src, src_opts);
            var b1s = b1 as IStreamingBackend;
            System.Diagnostics.Debug.Assert(b1s != null);

            using var b2 = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(dst, dst_opts);
            var b2s = b2 as IStreamingBackend;
            System.Diagnostics.Debug.Assert(b2s != null);

            var (to_copy, to_delete, to_verify) = PrepareFileLists(b1s, b2s);
            var deleted = await DeleteAsync(b2s, to_delete, dry_run);
            Console.WriteLine($"Deleted {deleted} files from {dst}");
            var (copied, copy_errors) = await CopyAsync(b1s, b2s, to_copy, dry_run, verify);
            Console.WriteLine($"Copied {copied} files from {src} to {dst}");

            if (copy_errors.Any())
            {
                Console.WriteLine($"Could not copy {copy_errors.Count()} files: {string.Join(", ", copy_errors)}");
                return copy_errors.Count();
            }

            if (verify)
            {
                var not_verified = await VerifyAsync(b1s, b2s, to_verify);
                Console.WriteLine($"Could not verify {not_verified.Count()} files: {string.Join(", ", not_verified)}");
                return not_verified.Count();
            }

            Console.WriteLine("Done");

            return 0;
        }

        // TODO maximum retention?: don't delete files, only rename old ones.
        // TODO have concurrency parameters: uploaders, downloaders
        // TODO low memory mode, where things aren't kept in memory. Maybe utilize SQLite?
        // TODO Force parameter
        // TODO Progress reporting
        // TODO Logging
        // TODO Retry on errors

        // check database consistency. I.e. have both databases, check that the block, volume, files, etc match up.
        // introduce these checks as a post processing step? Especially the database consistency check, as that is often recreated from the index files.
        // TODO This tool shouldn't handle it, but for convenience, it should support making the seperate call to the regular Duplicati on the destination backend, which alread carries this functionality.

        // Forcefully synchronize the remote backends
        private static async Task<(long, IEnumerable<string>)> CopyAsync(IStreamingBackend b_src, IStreamingBackend b_dst, IEnumerable<IFileEntry> files, bool dry_run, bool verify)
        {
            long successful_copies = 0;
            List<string> errors = [];
            using var s_src = new MemoryStream();
            using var s_dst = new MemoryStream();
            foreach (var f in files)
            {
                try
                {
                    await b_src.GetAsync(f.Name, s_src, CancellationToken.None);
                    if (dry_run)
                    {
                        Console.WriteLine($"Would write {s_src.Length} bytes of {f.Name} to {b_dst.DisplayName}");
                    }
                    else
                    {
                        await b_dst.PutAsync(f.Name, s_src, CancellationToken.None);
                        if (verify)
                        {
                            await b_dst.GetAsync(f.Name, s_dst, CancellationToken.None);
                            if (s_src.Length != s_dst.Length || !s_src.ToArray().SequenceEqual(s_dst.ToArray()))
                            {
                                Console.WriteLine($"Error verifying {f.Name}: The file was not copied correctly");
                                errors.Add(f.Name);
                            }
                            s_dst.SetLength(0);
                        }
                    }
                    s_src.SetLength(0);
                    successful_copies++;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error copying {f.Name}: {e.Message}");
                    errors.Add(f.Name);
                }
            }
            return (successful_copies, errors);
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

        private static Option<List<string>> OptionWithMultipleTokens(string[] aliases, string description)
        {
            return new Option<List<string>>(aliases: aliases, description: description)
            {
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true
            };
        }

        private static (IEnumerable<IFileEntry>, IEnumerable<IFileEntry>, IEnumerable<IFileEntry>) PrepareFileLists(IStreamingBackend b_src, IStreamingBackend b_dst)
        {
            var files_src = b_src.List();
            var files_dst = b_dst.List();

            // Shortcut for empty destination
            if (!files_dst.Any())
            {
                return (files_src, [], []);
            }

            var lookup_src = files_src.ToDictionary(x => x.Name);
            var lookup_dst = files_dst.ToDictionary(x => x.Name);

            var to_copy = new List<IFileEntry>();
            var to_delete = new HashSet<string>();
            var to_verify = new List<IFileEntry>();

            // Find all of the files in src that are not in dst, have a different size or have a more recent modification date
            foreach (var f_src in files_src)
            {
                if (lookup_dst.TryGetValue(f_src.Name, out var f_dst))
                {
                    if (f_src.Size != f_dst.Size || f_src.LastModification > f_dst.LastModification)
                    {
                        // The file is different, so we need to copy it
                        to_copy.Add(f_src);
                        to_delete.Add(f_dst.Name);
                    }
                    else
                    {
                        // The file seems to be the same, so we need to verify it if the user wants to
                        to_verify.Add(f_src);
                    }
                }
                else
                {
                    // The file is not in the destination, so we need to copy it
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

            return (to_copy, to_delete.Select(x => lookup_dst[x]), to_verify);
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

        // Post comparison
        private static async Task<IEnumerable<string>> VerifyAsync(IStreamingBackend b_src, IStreamingBackend b_dst, IEnumerable<IFileEntry> files)
        {
            var errors = new List<string>();
            using var s_src = new MemoryStream();
            using var s_dst = new MemoryStream();

            foreach (var f in files)
            {
                try
                {
                    // Get both files
                    var fs = b_src.GetAsync(f.Name, s_src, CancellationToken.None);
                    var ds = b_dst.GetAsync(f.Name, s_dst, CancellationToken.None);
                    await Task.WhenAll(fs, ds);

                    // Compare the contents
                    if (s_src.Length != s_dst.Length || !s_src.ToArray().SequenceEqual(s_dst.ToArray()))
                    {
                        errors.Add(f.Name);
                    }

                    // Reset the streams
                    s_src.SetLength(0);
                    s_dst.SetLength(0);
                }
                catch (Exception e)
                {
                    errors.Add($"{f.Name}");
                    Console.WriteLine($"Error verifying {f.Name}: {e.Message}");
                }
            }

            return errors;
        }

    }
}
