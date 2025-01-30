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
    /// <summary>
    /// Remote synchronization tool.
    /// </summary>
    public class Program
    {
        // Default values for the options
        private const bool DEFAULT_DRY_RUN = false;
        private const bool DEFAULT_VERIFY = false;
        private const int DEFAULT_RETRY = 3;
        private const bool DEFAULT_FORCE = false;
        private const bool DEFAULT_RETENTION = false;
        private const bool DEFAULT_CONFIRM = false;

        /// <summary>
        /// The log tag for this tool.
        /// </summary>
        private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<Program>();

        /// <summary>
        /// Main entry point for the tool.
        /// </summary>
        /// <param name="args">The commandline arguments</param>
        /// <returns>0 on success, -1 on abort, and the number of errors encountered otherwise.</returns>
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

            var root_cmd = new RootCommand(@"Remote Synchronization Tool

This tool synchronizes two remote backends. The tool assumes that the intent is
to have the destination match the source.

If the destination has files that are not in the source, they will be deleted
(or renamed if the retention option is set).

If the destination has files that are in the source but are different in size or
has a newer timestamp, they will be overwritten by the source files.

If the force option is set, the destination will be overwritten by the source,
regardless of the state of the files. It will also skip the initial comparison,
and delete (or rename) all files in the destination.

If the verify option is set, the files will be downloaded and compared after
uploading to ensure that the files are correct. Files that already exist in the
destination will be verified before being overwritten (if they seemingly match).
            ");
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

        /// <summary>
        /// The main logic of the tool.
        /// </summary>
        /// <param name="src">The connection string for the source backend.</param>
        /// <param name="dst">The connection string for the destination backend.</param>
        /// <param name="options">Various options for the tool</param>
        /// <returns>The return code for the main entry; 0 on success.</returns>
        private static async Task<int> Run(string src, string dst, Dictionary<string, object?> options)
        {
            // Parse the known options for this tool
            var dry_run = options["dry-run"] as bool? ?? DEFAULT_DRY_RUN;
            var verify = options["verify"] as bool? ?? DEFAULT_VERIFY;
            var retries = options["retry"] as int? ?? DEFAULT_RETRY;
            var force = options["force"] as bool? ?? DEFAULT_FORCE;
            var retention = options["retention"] as bool? ?? DEFAULT_RETENTION;
            var confirm = options["confirm"] as bool? ?? DEFAULT_CONFIRM;

            // Unpack and parse the multi token options
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

            // Load the backends
            using var b1 = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(src, src_opts);
            var b1s = b1 as IStreamingBackend;
            System.Diagnostics.Debug.Assert(b1s != null);

            using var b2 = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(dst, dst_opts);
            var b2s = b2 as IStreamingBackend;
            System.Diagnostics.Debug.Assert(b2s != null);

            // Prepare the operations
            var (to_copy, to_delete, to_verify) = PrepareFileLists(b1s, b2s, force);

            // Verify the files if requested. If the files are not verified, they will be deleted and copied again.
            if (verify)
            {
                // As this is a potentially slow operation, ask for confirmation of the verification)
                if (!confirm)
                {
                    Console.WriteLine($"This will verify {to_verify.Count()} files before copying them. Do you want to continue? [y/N]");
                    var response = Console.ReadLine();
                    if (!response?.Equals("y", StringComparison.CurrentCultureIgnoreCase) ?? true)
                    {
                        Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Aborted");
                        return -1;
                    }
                }

                var not_verified = await VerifyAsync(b1s, b2s, to_verify);

                if (not_verified.Any())
                {
                    Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "rsync", null, "{0} files failed verification. They will be deleted and copied again.", not_verified.Count());
                    to_delete = to_delete.Concat(not_verified);
                    to_copy = to_copy.Concat(not_verified);
                }
            }

            // As this is a potentially destructive operation, ask for confirmation
            if (!confirm)
            {
                var delete_rename = retention ? "Rename" : "Delete";
                Console.WriteLine($"This will perform the following actions (in order):");
                Console.WriteLine($"    - {delete_rename} {to_delete.Count()} files from {dst}");
                Console.WriteLine($"    - Copy {to_copy.Count()} files from {src} to {dst}");
                Console.WriteLine();
                Console.WriteLine("Do you want to continue? [y/N]");

                var response = Console.ReadLine();
                if (!response?.Equals("y", StringComparison.CurrentCultureIgnoreCase) ?? true)
                {
                    Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Aborted");
                    return -1;
                }
            }

            // Delete or rename the files that are not needed
            if (retention)
            {
                var renamed = await RenameAsync(b2, to_delete, dry_run);
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Renamed {0} files in {1}", renamed, b2s.DisplayName);
            }
            else
            {
                var deleted = await DeleteAsync(b2s, to_delete, dry_run);
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Deleted {0} files from {1}", deleted, b2s.DisplayName);
            }

            // Copy the files
            var (copied, copy_errors) = await CopyAsync(b1s, b2s, to_copy, dry_run, verify);
            Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Copied {0} files from {1} to {2}", copied, b1s, b2s);

            // If there are still errors, retry a few times
            if (copy_errors.Any())
            {
                Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "rsync", null, "Could not copy {0} files.", copy_errors.Count());
                if (retries > 0)
                {
                    Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Retrying {0} more times to copy the {1} files that failed", retries, copy_errors.Count());
                    for (int i = 0; i < retries; i++)
                    {
                        Thread.Sleep(5000); // Wait 5 seconds before retrying
                        (copied, copy_errors) = await CopyAsync(b1s, b2s, copy_errors, dry_run, verify);
                        Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Copied {0} files from {1} to {2}", copied, b1s, b2s);
                        if (!copy_errors.Any())
                            break;
                    }
                }

                if (copy_errors.Any())
                {
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", null, "Could not copy {0} files. Not retrying any more.", copy_errors.Count());
                    return copy_errors.Count();
                }
            }

            Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Remote synchronization completed successfully");

            return 0;
        }

        // TODO have concurrency parameters: uploaders, downloaders
        // TODO low memory mode, where things aren't kept in memory. Maybe utilize SQLite?
        // TODO For convenience, have the option to launch a "duplicati test" on the destination backend after the synchronization
        // TODO Save hash to minimize redownload

        // TODO Profiling logging
        // TODO Progress reporting
        // TODO Duplicati Results
        // TODO Log-level and log-file

        /// <summary>
        /// Copies the files from one backend to another.
        /// The files are copied one by one, and each file is verified after uploading if the verify flag is set.
        /// </summary>
        /// <param name="b_src">The source backend.</param>
        /// <param name="b_dst">The destination backend.</param>
        /// <param name="files">The files that will be copied.</param>
        /// <param name="dry_run">Flag for whether destructive actions (writes) should be printed rather than performed. Downloads (reads) are still being performed.</param>
        /// <param name="verify">Flag for whether to verify each upload to the destination backend.</param>
        /// <returns>A tuple holding the number of succesful copies and a List of the files that failed.</returns>
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
                        Duplicati.Library.Logging.Log.WriteDryrunMessage(LOGTAG, "rsync", "Would write {0} bytes of {1} to {2}", s_src.Length, f.Name, b_dst.DisplayName);
                    }
                    else
                    {
                        await b_dst.PutAsync(f.Name, s_src, CancellationToken.None);
                        if (verify)
                        {
                            await b_dst.GetAsync(f.Name, s_dst, CancellationToken.None);

                            string? err_string = null;
                            if (s_src.Length != s_dst.Length)
                            {
                                err_string = $"The sizes of the files do not match: {s_src.Length} != {s_dst.Length}.";
                            }

                            if (!s_src.ToArray().SequenceEqual(s_dst.ToArray()))
                            {
                                err_string = (err_string is null ? "" : err_string + " ") + "The contents of the files do not match.";
                            }

                            if (err_string is not null)
                            {
                                throw new Exception(err_string);
                            }
                        }
                    }
                    successful_copies++;
                }
                catch (Exception e)
                {
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", e, "Error copying {0}: {1}", f.Name, e.Message);
                    errors.Add(f);
                }
                finally
                {
                    s_src.SetLength(0);
                    s_dst.SetLength(0);
                }
            }
            return (successful_copies, errors);
        }

        /// <summary>
        /// Deletes the files from a backend.
        /// </summary>
        /// <param name="b">The backend to delete the files from.</param>
        /// <param name="files">The files to delete.</param>
        /// <param name="dry_run">Flag for whether the deletion should be printed rather than performed.</param>
        /// <returns>The number of successful deletions.</returns>
        private static async Task<long> DeleteAsync(IStreamingBackend b, IEnumerable<IFileEntry> files, bool dry_run)
        {
            long successful_deletes = 0;
            foreach (var f in files)
            {
                try
                {
                    if (dry_run)
                    {
                        Duplicati.Library.Logging.Log.WriteDryrunMessage(LOGTAG, "rsync", "Would delete {0} from {1}", f.Name, b.DisplayName);
                    }
                    else
                    {
                        await b.DeleteAsync(f.Name, CancellationToken.None);
                    }
                    successful_deletes++;
                }
                catch (Exception e)
                {
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", e, "Error deleting {0}: {1}", f.Name, e.Message);
                }
            }
            return successful_deletes;
        }

        /// <summary>
        /// Creates an option that allows multiple tokens and multiple arguments per token.
        /// </summary>
        /// <param name="aliases">The aliases for the option.</param>
        /// <param name="description">The description for the option.</param>
        /// <returns>The created option.</returns>
        private static Option<List<string>> OptionWithMultipleTokens(string[] aliases, string description)
        {
            return new Option<List<string>>(aliases: aliases, description: description)
            {
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true
            };
        }

        /// <summary>
        /// Prepares the lists of files to copy, delete and verify.
        /// The files to copy are the files that are not in the destination, have a different size or have a more recent modification date.
        /// The files to delete are the files that are found in the destination but not found in the source.
        /// The files to verify are the files that are found in both the source and the destination, and that have the same size and modification date.
        /// </summary>
        /// <param name="b_src">The source backend.</param>
        /// <param name="b_dst">The destination backend.</param>
        /// <param name="force">Flag for whether to force the synchronization.</param>
        /// <returns>A tuple of Lists each holding the files to copy, delete and verify.</returns>
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

        /// <summary>
        /// Renames the files in a backend.
        /// The renaming is done by deleting the file and re-uploading it with a new name.
        /// </summary>
        /// <param name="b">The backend to rename the files in.</param>
        /// <param name="files">The files to rename.</param>
        /// <param name="dry_run">Flag for whether the renaming should be printed rather than performed.</param>
        /// <returns>The number of successful renames.</returns>
        private static async Task<long> RenameAsync(IBackend b, IEnumerable<IFileEntry> files, bool dry_run)
        {
            long successful_renames = 0;
            string suffix = $"{System.DateTime.Now:yyyyMMddHHmmss}.old";
            using var downloaded = new MemoryStream();
            switch (b)
            {
                case IStreamingBackend sb:
                    {
                        foreach (var f in files)
                        {
                            try
                            {
                                await sb.GetAsync(f.Name, downloaded, CancellationToken.None);
                                if (dry_run)
                                {
                                    Duplicati.Library.Logging.Log.WriteDryrunMessage(LOGTAG, "rsync", "Would rename {0} to {0}.{1} by deleting and re-uploading {2} bytes to {3}", f.Name, suffix, downloaded.Length, sb.DisplayName);
                                }
                                else
                                {
                                    await sb.PutAsync($"{f.Name}.{suffix}", downloaded, CancellationToken.None);
                                    await sb.DeleteAsync(f.Name, CancellationToken.None);
                                }
                                successful_renames++;
                            }
                            catch (Exception e)
                            {
                                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", e, "Error renaming {0}: {1}", f.Name, e.Message);
                            }
                            finally
                            {
                                // Reset the stream
                                downloaded.SetLength(0);
                            }
                        }
                        return successful_renames;
                    }
                case IRenameEnabledBackend rb:
                    {
                        foreach (var f in files)
                        {
                            try
                            {
                                if (dry_run)
                                {
                                    Duplicati.Library.Logging.Log.WriteDryrunMessage(LOGTAG, "rsync", "Would rename {0} to {0}.{1} by calling Rename on {2}", f.Name, suffix, rb.DisplayName);
                                }
                                else
                                {
                                    await rb.RenameAsync(f.Name, $"{f.Name}.{suffix}", CancellationToken.None);
                                }
                                successful_renames++;
                            }
                            catch (Exception e)
                            {
                                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", e, "Error renaming {0}: {1}", f.Name, e.Message);
                            }
                        }
                        return successful_renames;
                    }
                default:
                    throw new NotSupportedException("The backend does not support renaming");
            }
        }

        /// <summary>
        /// Verifies the files in the destination backend.
        /// The verification is done by downloading the files from the destination backend and comparing them to the source files.
        /// </summary>
        /// <param name="b_src">The source backend.</param>
        /// <param name="b_dst">The destination backend.</param>
        /// <param name="files">The files to verify.</param>
        /// <returns>A list of the files that failed verification.</returns>
        private static async Task<IEnumerable<IFileEntry>> VerifyAsync(IStreamingBackend b_src, IStreamingBackend b_dst, IEnumerable<IFileEntry> files)
        {
            var errors = new List<IFileEntry>();
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
                        errors.Add(f);
                    }
                }
                catch (Exception e)
                {
                    errors.Add(f);
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", e, "Error during verification of {0}: {1}", f.Name, e.Message);
                }
                finally
                {
                    // Reset the streams
                    s_src.SetLength(0);
                    s_dst.SetLength(0);
                }
            }

            return errors;
        }

    }
}
