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
        // Default values for the options
        private const bool DEFAULT_DRY_RUN = false;
        private const bool DEFAULT_VERIFY = false;
        private const int DEFAULT_RETRY = 3;
        private const bool DEFAULT_FORCE = false;
        private const bool DEFAULT_RETENTION = false;
        private const bool DEFAULT_CONFIRM = false;

        public static async Task<int> Main(string[] args)
        {
            var src_arg = new Argument<string>(name: "backend_src", description: "The source backend string");
            var dst_arg = new Argument<string>(name: "backend_dst", description: "The destination backend string");
            var dry_run_opt = new Option<bool>(aliases: ["--dry-run", "-d"], description: "Do not actually write or delete files", getDefaultValue: () => DEFAULT_DRY_RUN);
            var src_opts = OptionWithMultipleTokens(aliases: ["--src-options"], description: "Options for the source backend");
            var dst_opts = OptionWithMultipleTokens(aliases: ["--dst-options"], description: "Options for the destination backend");
            var verify_opt = new Option<bool>(aliases: ["--verify"], description: "Verify the files after copying", getDefaultValue: () => DEFAULT_VERIFY);
            var retry_opt = new Option<int>(aliases: ["--retry"], description: "Number of times to retry on errors", getDefaultValue: () => DEFAULT_RETRY) { Arity = ArgumentArity.ExactlyOne };
            var force_opt = new Option<bool>(aliases: ["--force"], description: "Force the synchronization", getDefaultValue: () => DEFAULT_FORCE);
            var retention_opt = new Option<bool>(aliases: ["--retention"], description: "Toggles whether to keep old files. Any deletes will be renames instead", getDefaultValue: () => DEFAULT_RETENTION);
            var confirm_opt = new Option<bool>(aliases: ["--confirm"], description: "Automatically confirm the operation", getDefaultValue: () => DEFAULT_CONFIRM);
            var global_opts = OptionWithMultipleTokens(aliases: ["--global-options"], description: "Global options all backends. May be overridden by backend specific options (src-options, dst-options)");

            var root_cmd = new RootCommand("Remote Synchronization Tool");
            root_cmd.AddArgument(src_arg);
            root_cmd.AddArgument(dst_arg);
            root_cmd.AddOption(dry_run_opt);
            root_cmd.AddOption(src_opts);
            root_cmd.AddOption(dst_opts);
            root_cmd.AddOption(verify_opt);
            root_cmd.AddOption(retry_opt);
            root_cmd.AddOption(force_opt);
            root_cmd.AddOption(retention_opt);
            root_cmd.AddOption(confirm_opt);
            root_cmd.AddOption(global_opts);

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
            var dry_run = options["dry-run"] as bool? ?? DEFAULT_DRY_RUN;
            var verify = options["verify"] as bool? ?? DEFAULT_VERIFY;
            var retries = options["retry"] as int? ?? DEFAULT_RETRY;
            var force = options["force"] as bool? ?? DEFAULT_FORCE;
            var retention = options["retention"] as bool? ?? DEFAULT_RETENTION;
            var confirm = options["confirm"] as bool? ?? DEFAULT_CONFIRM;

            Dictionary<string, string> global_options = (options["global-options"] as List<string>)
                ?.Select(x => x.Split("="))
                .ToDictionary(x => x[0], x => x[1])
                ?? [];
            Dictionary<string, string> src_opts = (options["src-options"] as List<string>)
                ?.Select(x => x.Split("="))
                .ToDictionary(x => x[0], x => x[1])
                ?? [];
            Dictionary<string, string> dst_opts = (options["dst-options"] as List<string>)
                ?.Select(x => x.Split("="))
                .ToDictionary(x => x[0], x => x[1])
                ?? [];

            // Merge the global options into the source and destination options
            foreach (var x in global_options)
                src_opts[x.Key] = dst_opts[x.Key] = x.Value;

            using var b1 = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(src, src_opts);
            var b1s = b1 as IStreamingBackend;
            System.Diagnostics.Debug.Assert(b1s != null);

            using var b2 = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(dst, dst_opts);
            var b2s = b2 as IStreamingBackend;
            System.Diagnostics.Debug.Assert(b2s != null);

            var (to_copy, to_delete, to_verify) = PrepareFileLists(b1s, b2s, force);

            // As this is a potentially destructive operation, ask for confirmation
            if (!confirm)
            {
                var delete_rename = retention ? "Rename" : "Delete";
                Console.WriteLine($"This will perform the following actions (in order):");
                Console.WriteLine($"    - {delete_rename} {to_delete.Count()} files from {dst}");
                Console.WriteLine($"    - Copy {to_copy.Count()} files from {src} to {dst}");
                if (verify)
                    Console.WriteLine($"    - Download and verify {to_verify.Count()} files in {dst}");
                Console.WriteLine();
                Console.WriteLine("Do you want to continue? [y/N]");

                var response = Console.ReadLine();
                if (!response?.Equals("y", StringComparison.CurrentCultureIgnoreCase) ?? true)
                {
                    Console.WriteLine("Aborted");
                    return -1;
                }
            }

            // Delete or rename the files that are not needed
            if (retention)
            {
                var renamed = await RenameAsync(b2s, to_delete, dry_run);
                Console.WriteLine($"Renamed {renamed} files in {dst}");
            }
            else
            {
                var deleted = await DeleteAsync(b2s, to_delete, dry_run);
                Console.WriteLine($"Deleted {deleted} files from {dst}");
            }

            // Copy the files
            var (copied, copy_errors) = await CopyAsync(b1s, b2s, to_copy, dry_run, verify);
            Console.WriteLine($"Copied {copied} files from {src} to {dst}");

            // If there are still errors, retry a few times
            if (copy_errors.Any())
            {
                if (retries > 0)
                {
                    Console.WriteLine($"Retrying {retries} more times to copy the {copy_errors.Count()} files that failed");
                    for (int i = 0; i < retries; i++)
                    {
                        Thread.Sleep(5000); // Wait 5 seconds before retrying
                        (copied, copy_errors) = await CopyAsync(b1s, b2s, copy_errors, dry_run, verify);
                        Console.WriteLine($"Copied {copied} files from {src} to {dst}");
                        if (!copy_errors.Any())
                            break;
                    }
                }

                if (copy_errors.Any())
                {
                    Console.WriteLine($"Could not copy {copy_errors.Count()} files: {string.Join(", ", copy_errors)}");
                    return copy_errors.Count();
                }
            }

            // Verify the files if requested
            if (verify)
            {
                var not_verified = await VerifyAsync(b1s, b2s, to_verify);
                Console.WriteLine($"Could not verify {not_verified.Count()} files: {string.Join(", ", not_verified)}");
                return not_verified.Count();
            }

            Console.WriteLine($"Remote synchronization completed successfully");

            return 0;
        }

        // TODO have concurrency parameters: uploaders, downloaders
        // TODO low memory mode, where things aren't kept in memory. Maybe utilize SQLite?
        // TODO Progress reporting
        // TODO Logging

        // check database consistency. I.e. have both databases, check that the block, volume, files, etc match up.
        // introduce these checks as a post processing step? Especially the database consistency check, as that is often recreated from the index files.
        // TODO This tool shouldn't handle it, but for convenience, it should support making the seperate call to the regular Duplicati on the destination backend, which alread carries this functionality.

        // Forcefully synchronize the remote backends
        private static async Task<(long, IEnumerable<IFileEntry>)> CopyAsync(IStreamingBackend b_src, IStreamingBackend b_dst, IEnumerable<IFileEntry> files, bool dry_run, bool verify)
        {
            long successful_copies = 0;
            List<IFileEntry> errors = [];
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
                                errors.Add(f);
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
                    errors.Add(f);
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

        private static (IEnumerable<IFileEntry>, IEnumerable<IFileEntry>, IEnumerable<IFileEntry>) PrepareFileLists(IStreamingBackend b_src, IStreamingBackend b_dst, bool force)
        {
            var files_src = b_src.List();
            var files_dst = b_dst.List();

            // Shortcut for force
            if (force)
            {
                return (files_src, files_dst, []);
            }

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

        private static async Task<long> RenameAsync(IStreamingBackend b, IEnumerable<IFileEntry> files, bool dry_run)
        {
            long successful_renames = 0;
            string suffix = $"{System.DateTime.Now:yyyyMMddHHmmss}.old";
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
