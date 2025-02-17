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
using System.CommandLine.NamingConventionBinder;
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
        /// <summary>
        /// Global configuration for the tool. Should be set after parsing the commandline arguments.
        /// </summary>
        private sealed record Config
        (
            // Arguments
            string Src,
            string Dst,

            // Options
            bool Confirm,
            bool DryRun,
            List<string> DstOptions,
            bool Force,
            List<string> GlobalOptions,
            string LogFile,
            string LogLevel,
            bool ParseArgumentsOnly,
            bool Progress,
            bool Retention,
            int Retry,
            List<string> SrcOptions,
            bool VerifyContents,
            bool VerifyGetAfterPut
        );

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
            var arg_src = new Argument<string>(name: "backend_src", description: "The source backend string");
            var arg_dst = new Argument<string>(name: "backend_dst", description: "The destination backend string");

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
            ")
            {
                arg_src,
                arg_dst,

                new Option<bool>(aliases: ["--confirm", "--yes", "-y"], description: "Automatically confirm the operation", getDefaultValue: () => false),
                new Option<bool>(aliases: ["--dry-run", "-d"], description: "Do not actually write or delete files. If not set here, the global options will be checked", getDefaultValue: () => false),
                OptionWithMultipleTokens(aliases: ["--dst-options"], description: "Options for the destination backend. Each option is a key-value pair separated by an equals sign, e.g. --dst-options key1=value1 key2=value2 [default: empty]", getDefaultValue: () => []),
                new Option<bool>(aliases: ["--force", "-f"], description: "Force the synchronization", getDefaultValue: () => false),
                OptionWithMultipleTokens(aliases: ["--global-options"], description: "Global options all backends. May be overridden by backend specific options (src-options, dst-options). Each option is a key-value pair separated by an equals sign, e.g. --global-options key1=value1 key2=value2 [default: empty]", getDefaultValue: () => []),
                new Option<string>(aliases: ["--log-file"], description: "The log file to write to. If not set here, global options will be checked [default: \"\"]", getDefaultValue: () => "") { Arity = ArgumentArity.ExactlyOne },
                new Option<string>(aliases: ["--log-level"], description: "The log level to use. If not set here, global options will be checked", getDefaultValue: () => "Information") { Arity = ArgumentArity.ExactlyOne },
                new Option<bool>(aliases: ["--parse-arguments-only"], description: "Only parse the arguments and then exit", getDefaultValue: () => false),
                new Option<bool>(aliases: ["--progress"], description: "Print progress to STDOUT", getDefaultValue: () => false),
                new Option<bool>(aliases: ["--retention"], description: "Toggles whether to keep old files. Any deletes will be renames instead", getDefaultValue: () => false),
                new Option<int>(aliases: ["--retry"], description: "Number of times to retry on errors", getDefaultValue: () => 3) { Arity = ArgumentArity.ExactlyOne },
                OptionWithMultipleTokens(aliases: ["--src-options"], description: "Options for the source backend. Each option is a key-value pair separated by an equals sign, e.g. --src-options key1=value1 key2=value2 [default: empty]", getDefaultValue: () => []),
                new Option<bool>(aliases: ["--verify-contents"], description: "Verify the contents of the files to decide whether the pre-existing destination files should be overwritten", getDefaultValue: () => false),
                new Option<bool>(aliases: ["--verify-get-after-put"], description: "Verify the files after uploading them to ensure that they were uploaded correctly", getDefaultValue: () => false),
            };

            root_cmd.Handler = CommandHandler.Create((string backend_src, string backend_dst, Config config) =>
            {
                var config_with_args = config with { Dst = backend_dst, Src = backend_src };

                return Run(config_with_args);
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
        private static async Task<int> Run(Config config)
        {
            // Unpack and parse the multi token options
            Dictionary<string, string> global_options = config.GlobalOptions
                .Select(x => x.Split("="))
                .ToDictionary(x => x[0], x => x[1]);

            // Parse the log level
            var log_level_parsed = Enum.TryParse<Duplicati.Library.Logging.LogMessageType>(config.LogLevel, true, out var log_level_enum);
            log_level_enum = log_level_parsed ? log_level_enum : Duplicati.Library.Logging.LogMessageType.Information;

            using var console_sink = new Duplicati.CommandLine.ConsoleOutput(Console.Out, global_options);
            using var multi_sink = new Duplicati.Library.Main.ControllerMultiLogTarget(console_sink, log_level_enum, null);

            // Parse the log file
            // The log file sink doesn't have to be disposed, as the multi_sink will take care of it
            Duplicati.Library.Logging.StreamLogDestination? log_file_sink = null;
            if (!string.IsNullOrEmpty(config.LogFile))
            {
                string log_file_dir = SystemIO.IO_OS.PathGetDirectoryName(config.LogFile);
                if (!string.IsNullOrEmpty(log_file_dir) && !SystemIO.IO_OS.DirectoryExists(log_file_dir))
                    SystemIO.IO_OS.DirectoryCreate(log_file_dir);
                log_file_sink = new Duplicati.Library.Logging.StreamLogDestination(config.LogFile);
            }
            multi_sink.AddTarget(log_file_sink, log_level_enum, null);

            // Start the logging scope
            using var _ = Duplicati.Library.Logging.Log.StartScope(multi_sink, log_level_enum);

            Dictionary<string, string> src_opts = config.SrcOptions
                .Select(x => x.Split("="))
                .ToDictionary(x => x[0], x => x[1]);

            Dictionary<string, string> dst_opts = config.DstOptions
                .Select(x => x.Split("="))
                .ToDictionary(x => x[0], x => x[1]);

            // Merge the global options into the source and destination options. The global options will be overridden by the source and destination options.
            foreach (var x in global_options)
            {
                if (!src_opts.ContainsKey(x.Key))
                    src_opts[x.Key] = x.Value;
                if (!dst_opts.ContainsKey(x.Key))
                    dst_opts[x.Key] = x.Value;
            }

            // Check if we only had to parse the arguments
            if (config.ParseArgumentsOnly)
            {
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Arguments parsed successfully; exiting.");
                return 0;
            }

            // Load the backends
            static Exception throw_message(string target)
            {
                var message = $"The {target} backend does not support streaming operations.";
                var ex = new Exception(message);
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", ex, message);
                return ex;
            }

            using var b1 = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(config.Src, src_opts);
            var b1s = b1 as IStreamingBackend ?? throw throw_message("source");

            using var b2 = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(config.Dst, dst_opts);
            var b2s = b2 as IStreamingBackend ?? throw throw_message("destination");

            // Prepare the operations
            var (to_copy, to_delete, to_verify) = await PrepareFileLists(b1s, b2s, config, CancellationToken.None);

            // Verify the files if requested. If the files are not verified, they will be deleted and copied again.
            long verified = 0, failed_verify = 0;
            if (config.VerifyContents)
            {
                // As this is a potentially slow operation, ask for confirmation of the verification)
                if (!config.Confirm)
                {
                    Console.WriteLine($"This will verify {to_verify.Count()} files before copying them. Do you want to continue? [y/N]");
                    var response = Console.ReadLine();
                    if (!response?.Equals("y", StringComparison.CurrentCultureIgnoreCase) ?? true)
                    {
                        Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Aborted");
                        return -1;
                    }
                }

                var not_verified = await VerifyAsync(b1s, b2s, to_verify, config);
                failed_verify = not_verified.Count();
                verified = to_verify.Count() - failed_verify;

                if (not_verified.Any())
                {
                    Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "rsync", null,
                        "{0} files failed verification. They will be deleted and copied again.",
                        not_verified.Count());
                    to_delete = to_delete.Concat(not_verified);
                    to_copy = to_copy.Concat(not_verified);
                }
            }

            Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync",
                "The remote synchronization plan is to {0} {1} files from {2}, then copy {3} files from {4} to {2}.",
                config.Retention ? "rename" : "delete",
                to_delete.Count(), b2s.DisplayName, to_copy.Count(), b1s.DisplayName);

            // As this is a potentially destructive operation, ask for confirmation
            if (!config.Confirm)
            {
                var delete_rename = config.Retention ? "Rename" : "Delete";
                Console.WriteLine($"This will perform the following actions (in order):");
                Console.WriteLine($"    - {delete_rename} {to_delete.Count()} files from {config.Dst}");
                Console.WriteLine($"    - Copy {to_copy.Count()} files from {config.Src} to {config.Dst}");
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
            long renamed = 0, deleted = 0;
            if (config.Retention)
            {
                renamed = await RenameAsync(b2, to_delete, config);
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync",
                    "Renamed {0} files in {1}", renamed, b2s.DisplayName);
            }
            else
            {
                deleted = await DeleteAsync(b2s, to_delete, config);
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync",
                    "Deleted {0} files from {1}", deleted, b2s.DisplayName);
            }

            // Copy the files
            var (copied, copy_errors) = await CopyAsync(b1s, b2s, to_copy, config);
            Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync",
                "Copied {0} files from {1} to {2}", copied, b1s, b2s);

            // If there are still errors, retry a few times
            if (copy_errors.Any())
            {
                Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "rsync", null,
                    "Could not copy {0} files.", copy_errors.Count());
                if (config.Retry > 0)
                {
                    Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync",
                        "Retrying {0} more times to copy the {1} files that failed",
                        config.Retry, copy_errors.Count());
                    for (int i = 0; i < config.Retry; i++)
                    {
                        await Task.Delay(5000); // Wait 5 seconds before retrying
                        (copied, copy_errors) = await CopyAsync(b1s, b2s, copy_errors, config);
                        Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync",
                            "Copied {0} files from {1} to {2}", copied, b1s, b2s);
                        if (!copy_errors.Any())
                            break;
                    }
                }

                if (copy_errors.Any())
                {
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", null,
                        "Could not copy {0} files. Not retrying any more.", copy_errors.Count());
                    return copy_errors.Count();
                }
            }

            // Results reporting
            if (verified > 0)
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync",
                    "Verified {0} files in {1} that didn't need to be copied",
                    verified, b2s.DisplayName);
            if (failed_verify > 0)
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync",
                    "Failed to verify {0} files in {1}, which were then attempted to be copied",
                    failed_verify, b2s.DisplayName);
            if (copied > 0)
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync",
                    "Copied {0} files from {1} to {2}", copied, b1s.DisplayName, b2s.DisplayName);
            if (deleted > 0)
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync",
                    "Deleted {0} files from {1}", deleted, b2s.DisplayName);
            if (renamed > 0)
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync",
                    "Renamed {0} files in {1}", renamed, b2s.DisplayName);

            Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync",
                "Remote synchronization completed successfully");

            return 0;
        }

        // TODO have concurrency parameters: uploaders, downloaders
        // TODO low memory mode, where things aren't kept in memory. Maybe utilize SQLite?
        // TODO For convenience, have the option to launch a "duplicati test" on the destination backend after the synchronization
        // TODO Save hash to minimize redownload
        // TODO Duplicati Results

        /// <summary>
        /// Copies the files from one backend to another.
        /// The files are copied one by one, and each file is verified after uploading if the verify flag is set.
        /// </summary>
        /// <param name="b_src">The source backend.</param>
        /// <param name="b_dst">The destination backend.</param>
        /// <param name="files">The files that will be copied.</param>
        /// <param name="config">The parsed configuration for the tool.</param>
        /// <returns>A tuple holding the number of succesful copies and a List of the files that failed.</returns>
        private static async Task<(long, IEnumerable<IFileEntry>)> CopyAsync(IStreamingBackend b_src, IStreamingBackend b_dst, IEnumerable<IFileEntry> files, Config config)
        {
            long successful_copies = 0;
            List<IFileEntry> errors = [];
            using var s_src = new MemoryStream();
            using var s_dst = new MemoryStream();
            long i = 0, n = files.Count();
            var sw_get_src = new System.Diagnostics.Stopwatch();
            var sw_put_dst = new System.Diagnostics.Stopwatch();
            var sw_get_dst = new System.Diagnostics.Stopwatch();
            var sw_get_cmp = new System.Diagnostics.Stopwatch();

            foreach (var f in files)
            {
                if (config.Progress)
                    Console.Write($"\rCopying: {i}/{n}");

                Duplicati.Library.Logging.Log.WriteVerboseMessage(LOGTAG, "rsync",
                    "Copying {0} from {1} to {2}", f.Name, b_src.DisplayName, b_dst.DisplayName);

                try
                {
                    sw_get_src.Start();
                    await b_src.GetAsync(f.Name, s_src, CancellationToken.None);
                    sw_get_src.Stop();
                    if (config.DryRun)
                    {
                        Duplicati.Library.Logging.Log.WriteDryrunMessage(LOGTAG, "rsync",
                            "Would write {0} bytes of {1} to {2}",
                            Duplicati.Library.Utility.Utility.FormatSizeString(s_src.Length),
                            f.Name, b_dst.DisplayName);
                    }
                    else
                    {
                        sw_put_dst.Start();
                        await b_dst.PutAsync(f.Name, s_src, CancellationToken.None);
                        sw_put_dst.Stop();
                        if (config.VerifyGetAfterPut)
                        {
                            sw_get_dst.Start();
                            await b_dst.GetAsync(f.Name, s_dst, CancellationToken.None);
                            sw_get_dst.Stop();

                            sw_get_cmp.Start();
                            string? err_string = null;
                            if (s_src.Length != s_dst.Length)
                            {
                                err_string = $"The sizes of the files do not match: {s_src.Length} != {s_dst.Length}.";
                            }

                            if (!s_src.ToArray().SequenceEqual(s_dst.ToArray()))
                            {
                                err_string = (err_string is null ? "" : err_string + " ") + "The contents of the files do not match.";
                            }
                            sw_get_cmp.Stop();

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
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", e,
                        "Error copying {0}: {1}", f.Name, e.Message);
                    errors.Add(f);
                }
                finally
                {
                    s_src.SetLength(0);
                    s_dst.SetLength(0);
                    i++;

                    // Stop any running timers
                    sw_get_src.Stop();
                    sw_put_dst.Stop();
                    sw_get_dst.Stop();
                    sw_get_cmp.Stop();
                }
            }

            if (config.Progress)
                Console.WriteLine($"\rCopying: {n}/{n}");

            Duplicati.Library.Logging.Log.WriteProfilingMessage(LOGTAG, "rsync",
                "Copy | Get source: {0} ms, Put destination: {1} ms, Get destination: {2} ms, Get compare: {3} ms",
                TimeSpan.FromMilliseconds(sw_get_src.ElapsedMilliseconds),
                TimeSpan.FromMilliseconds(sw_put_dst.ElapsedMilliseconds),
                TimeSpan.FromMilliseconds(sw_get_dst.ElapsedMilliseconds),
                TimeSpan.FromMilliseconds(sw_get_cmp.ElapsedMilliseconds));

            return (successful_copies, errors);
        }

        /// <summary>
        /// Deletes the files from a backend.
        /// </summary>
        /// <param name="b">The backend to delete the files from.</param>
        /// <param name="files">The files to delete.</param>
        /// <param name="config">The parsed configuration for the tool.</param>
        /// <returns>The number of successful deletions.</returns>
        private static async Task<long> DeleteAsync(IStreamingBackend b, IEnumerable<IFileEntry> files, Config config)
        {
            long successful_deletes = 0;
            long i = 0, n = files.Count();
            using var timer = new Duplicati.Library.Logging.Timer(LOGTAG, "rsync", "Delete operation");

            foreach (var f in files)
            {
                if (n > 1 && config.Progress)
                {
                    Console.Write($"\rDeleting: {i}/{n}");
                }

                Duplicati.Library.Logging.Log.WriteVerboseMessage(LOGTAG, "rsync",
                    "Deleting {0} from {1}", f.Name, b.DisplayName);

                try
                {
                    if (config.DryRun)
                    {
                        Duplicati.Library.Logging.Log.WriteDryrunMessage(LOGTAG, "rsync",
                            "Would delete {0} from {1}", f.Name, b.DisplayName);
                    }
                    else
                    {
                        await b.DeleteAsync(f.Name, CancellationToken.None);
                    }
                    successful_deletes++;
                }
                catch (Exception e)
                {
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", e,
                        "Error deleting {0}: {1}", f.Name, e.Message);
                }

                i++;
            }

            if (config.Progress)
                Console.WriteLine($"\rDeleting: {n}/{n}");
            return successful_deletes;
        }

        /// <summary>
        /// Creates an option that allows multiple tokens and multiple arguments per token.
        /// </summary>
        /// <param name="aliases">The aliases for the option.</param>
        /// <param name="description">The description for the option.</param>
        /// <returns>The created option.</returns>
        private static Option<List<string>> OptionWithMultipleTokens(string[] aliases, string description, Func<List<string>> getDefaultValue)
        {
            return new Option<List<string>>(aliases: aliases, description: description, getDefaultValue: getDefaultValue)
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
        /// <param name="config">The parsed configuration for the tool.</param>
        /// <returns>A tuple of Lists each holding the files to copy, delete and verify.</returns>
        private static async Task<(IEnumerable<IFileEntry>, IEnumerable<IFileEntry>, IEnumerable<IFileEntry>)> PrepareFileLists(IStreamingBackend b_src, IStreamingBackend b_dst, Config config, CancellationToken cancelToken)
        {
            IEnumerable<IFileEntry> files_src, files_dst;

            using (new Duplicati.Library.Logging.Timer(LOGTAG, "rsync", "Prepare | List source"))
                files_src = await b_src.ListAsync(cancelToken).ToListAsync(cancelToken).ConfigureAwait(false);

            using (new Duplicati.Library.Logging.Timer(LOGTAG, "rsync", "Prepare | List destination"))
                files_dst = await b_dst.ListAsync(cancelToken).ToListAsync(cancelToken).ConfigureAwait(false);

            // Shortcut for force
            if (config.Force)
            {
                return (files_src, files_dst, []);
            }

            // Shortcut for empty destination
            if (!files_dst.Any())
            {
                return (files_src, [], []);
            }

            Dictionary<string, IFileEntry> lookup_src, lookup_dst;
            using (new Duplicati.Library.Logging.Timer(LOGTAG, "rsync", "Prepare | Build lookup for source and destination"))
            {
                lookup_src = files_src.ToDictionary(x => x.Name);
                lookup_dst = files_dst.ToDictionary(x => x.Name);
            }

            var to_copy = new List<IFileEntry>();
            var to_delete = new HashSet<string>();
            var to_verify = new List<IFileEntry>();

            // Find all of the files in src that are not in dst, have a different size or have a more recent modification date
            using (new Duplicati.Library.Logging.Timer(LOGTAG, "rsync", "Prepare | Check the files that are present in source against destination"))
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
            using (new Duplicati.Library.Logging.Timer(LOGTAG, "rsync", "Prepare | Check the files that are present in destination against source"))
                foreach (var f_dst in files_dst)
                {
                    if (to_delete.Contains(f_dst.Name))
                        continue;

                    if (!lookup_src.ContainsKey(f_dst.Name))
                    {
                        to_delete.Add(f_dst.Name);
                    }
                }

            List<IFileEntry> to_delete_lookedup;
            using (new Duplicati.Library.Logging.Timer(LOGTAG, "rsync", "Prepare | Lookup the files to delete"))
                to_delete_lookedup = [.. to_delete.Select(x => lookup_dst[x])];

            return (to_copy, to_delete_lookedup, to_verify);
        }

        /// <summary>
        /// Renames the files in a backend.
        /// The renaming is done by deleting the file and re-uploading it with a new name.
        /// </summary>
        /// <param name="b">The backend to rename the files in.</param>
        /// <param name="files">The files to rename.</param>
        /// <param name="config">The parsed configuration for the tool.</param>
        /// <returns>The number of successful renames.</returns>
        private static async Task<long> RenameAsync(IBackend b, IEnumerable<IFileEntry> files, Config config)
        {
            long successful_renames = 0;
            string prefix = $"{System.DateTime.UtcNow:yyyyMMddHHmmss}.old";
            using var downloaded = new MemoryStream();
            long i = 0, n = files.Count();

            switch (b)
            {
                case IStreamingBackend sb:
                    {
                        var sw_get_src = new System.Diagnostics.Stopwatch();
                        var sw_put_dst = new System.Diagnostics.Stopwatch();
                        var sw_del_src = new System.Diagnostics.Stopwatch();

                        foreach (var f in files)
                        {
                            if (config.Progress)
                                Console.Write($"\rRenaming: {i}/{n}");

                            Duplicati.Library.Logging.Log.WriteVerboseMessage(LOGTAG, "rsync",
                                "Renaming {0} to {1}.{0} by deleting and re-uploading {2} bytes to {3}",
                                f.Name, prefix,
                                Duplicati.Library.Utility.Utility.FormatSizeString(downloaded.Length),
                                sb.DisplayName);

                            try
                            {
                                sw_get_src.Start();
                                await sb.GetAsync(f.Name, downloaded, CancellationToken.None);
                                sw_get_src.Stop();

                                if (config.DryRun)
                                {
                                    Duplicati.Library.Logging.Log.WriteDryrunMessage(LOGTAG, "rsync",
                                        "Would rename {0} to {1}.{0} by deleting and re-uploading {2} bytes to {3}",
                                        f.Name, prefix,
                                        Duplicati.Library.Utility.Utility.FormatSizeString(downloaded.Length),
                                        sb.DisplayName);
                                }
                                else
                                {
                                    sw_put_dst.Start();
                                    await sb.PutAsync($"{prefix}.{f.Name}", downloaded, CancellationToken.None);
                                    sw_put_dst.Stop();
                                    sw_del_src.Start();
                                    await sb.DeleteAsync(f.Name, CancellationToken.None);
                                    sw_del_src.Stop();
                                }
                                successful_renames++;
                            }
                            catch (Exception e)
                            {
                                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", e,
                                    "Error renaming {0}: {1}", f.Name, e.Message);
                            }
                            finally
                            {
                                // Reset the stream
                                downloaded.SetLength(0);

                                // Stop any running timers
                                sw_get_src.Stop();
                                sw_put_dst.Stop();
                                sw_del_src.Stop();
                            }

                            i++;
                        }

                        Duplicati.Library.Logging.Log.WriteProfilingMessage(LOGTAG, "rsync",
                            "Rename | Get source: {0} ms, Put destination: {1} ms, Delete source: {2} ms",
                            TimeSpan.FromMilliseconds(sw_get_src.ElapsedMilliseconds),
                            TimeSpan.FromMilliseconds(sw_put_dst.ElapsedMilliseconds),
                            TimeSpan.FromMilliseconds(sw_del_src.ElapsedMilliseconds));

                        if (config.Progress)
                            Console.WriteLine($"\rRenaming: {n}/{n}");

                        return successful_renames;
                    }
                case IRenameEnabledBackend rb:
                    {
                        var sw = new System.Diagnostics.Stopwatch();

                        foreach (var f in files)
                        {
                            if (config.Progress)
                                Console.Write($"\rRenaming: {i}/{n}");

                            Duplicati.Library.Logging.Log.WriteVerboseMessage(LOGTAG, "rsync",
                                "Renaming {0} to {1}.{0} by calling Rename on {2}",
                                f.Name, prefix, rb.DisplayName);

                            try
                            {
                                if (config.DryRun)
                                {
                                    Duplicati.Library.Logging.Log.WriteDryrunMessage(LOGTAG, "rsync",
                                        "Would rename {0} to {1}.{0} by calling Rename on {2}",
                                        f.Name, prefix, rb.DisplayName);
                                }
                                else
                                {
                                    sw.Start();
                                    await rb.RenameAsync(f.Name, $"{prefix}.{f.Name}", CancellationToken.None);
                                    sw.Stop();
                                }
                                successful_renames++;
                            }
                            catch (Exception e)
                            {
                                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", e,
                                    "Error renaming {0}: {1}", f.Name, e.Message);
                            }
                            finally
                            {
                                // Ensure the timer is stopped
                                sw.Stop();
                            }

                            i++;
                        }

                        Duplicati.Library.Logging.Log.WriteProfilingMessage(LOGTAG, "rsync",
                            "Rename: {0} ms",
                            TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds));

                        if (config.Progress)
                            Console.WriteLine($"\rRenaming: {n}/{n}");

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
        /// <param name="config">The parsed configuration for the tool.</param>
        /// <returns>A list of the files that failed verification.</returns>
        private static async Task<IEnumerable<IFileEntry>> VerifyAsync(IStreamingBackend b_src, IStreamingBackend b_dst, IEnumerable<IFileEntry> files, Config config)
        {
            var errors = new List<IFileEntry>();
            using var s_src = new MemoryStream();
            using var s_dst = new MemoryStream();
            long i = 0, n = files.Count();
            var sw_get = new System.Diagnostics.Stopwatch();
            var sw_cmp = new System.Diagnostics.Stopwatch();

            foreach (var f in files)
            {
                if (config.Progress)
                    Console.Write($"\rVerifying: {i}/{n}");

                Duplicati.Library.Logging.Log.WriteVerboseMessage(LOGTAG, "rsync",
                    "Verifying {0} by downloading and comparing {1} bytes from {2} and {3}",
                    f.Name,
                    Duplicati.Library.Utility.Utility.FormatSizeString(s_src.Length),
                    b_dst.DisplayName, b_src.DisplayName);

                try
                {
                    // Get both files
                    sw_get.Start();
                    var fs = b_src.GetAsync(f.Name, s_src, CancellationToken.None);
                    var ds = b_dst.GetAsync(f.Name, s_dst, CancellationToken.None);
                    await Task.WhenAll(fs, ds);
                    sw_get.Stop();

                    // Compare the contents
                    sw_cmp.Start();
                    if (s_src.Length != s_dst.Length || !s_src.ToArray().SequenceEqual(s_dst.ToArray()))
                    {
                        errors.Add(f);
                    }
                    sw_cmp.Stop();
                }
                catch (Exception e)
                {
                    errors.Add(f);
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", e,
                        "Error during verification of {0}: {1}", f.Name, e.Message);
                }
                finally
                {
                    // Reset the streams
                    s_src.SetLength(0);
                    s_dst.SetLength(0);

                    // Stop any running timers
                    sw_get.Stop();
                    sw_cmp.Stop();
                }

                i++;
            }

            Duplicati.Library.Logging.Log.WriteProfilingMessage(LOGTAG, "rsync",
                "Verify | Get: {0} ms, Compare: {1} ms",
                TimeSpan.FromMilliseconds(sw_get.ElapsedMilliseconds),
                TimeSpan.FromMilliseconds(sw_cmp.ElapsedMilliseconds));

            if (config.Progress)
                Console.WriteLine($"\rVerifying: {n}/{n}");

            return errors;
        }

    }
}
