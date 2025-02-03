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
    /// <summary>
    /// Remote synchronization tool.
    /// </summary>
    public class Program
    {
        // Default values for the options
        private const bool DEFAULT_DRY_RUN = false;
        private const bool DEFAULT_VERIFY_CONTENTS = false;
        private const bool DEFAULT_VERIFY_GET_AFTER_PUT = false;
        private const int DEFAULT_RETRY = 3;
        private const bool DEFAULT_FORCE = false;
        private const bool DEFAULT_RETENTION = false;
        private const bool DEFAULT_CONFIRM = false;
        private const string DEFAULT_LOG_LEVEL = "Information";
        private const string DEFAULT_LOG_FILE = "";
        private const bool DEFAULT_PROGRESS = false;
        private const bool DEFAULT_PARSE_ARGUMENTS_ONLY = false;

        /// <summary>
        /// Global configuration for the tool. Should be set after parsing the commandline arguments.
        /// </summary>
        private class GlobalConfig
        {
            /// <summary>
            /// Whether the tool should not actually write or delete files. Defaults to false.
            /// </summary>
            internal static bool DryRun = DEFAULT_DRY_RUN;
            /// <summary>
            /// Whether the tool should force the synchronization. Defaults to false.
            /// </summary>
            internal static bool Force = DEFAULT_FORCE;
            /// <summary>
            /// The log level to use. Defaults to "Information".
            /// </summary>
            internal static string LogLevel = DEFAULT_LOG_LEVEL;
            /// <summary>
            /// The log file to write to. Defaults to an empty string.
            /// </summary>
            internal static string LogFile = DEFAULT_LOG_FILE;
            /// <summary>
            /// Whether the tool should print progress to STDOUT. Defaults to false.
            /// </summary>
            internal static bool Progress = DEFAULT_PROGRESS;
            /// <summary>
            /// Whether the tool should verify the contents of the files to decide whether the pre-existing destination files should be overwritten. Defaults to false.
            /// </summary>
            internal static bool VerifyContents = DEFAULT_VERIFY_CONTENTS;
            /// <summary>
            /// Whether the tool should verify the files after uploading them to ensure that they were uploaded correctly. Defaults to false.
            /// </summary>
            internal static bool VerifyGetAfterPut = DEFAULT_VERIFY_GET_AFTER_PUT;
            /// <summary>
            /// Try to only parse the arguments and then exit. Defaults to false.
            /// </summary>
            internal static bool ParseArgumentsOnly = DEFAULT_PARSE_ARGUMENTS_ONLY;
        }

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
            var dry_run_opt = new Option<bool>(aliases: ["--dry-run", "-d"], description: "Do not actually write or delete files. If not set here, the global options will be checked", getDefaultValue: () => DEFAULT_DRY_RUN);
            var src_opts = OptionWithMultipleTokens(aliases: ["--src-options"], description: "Options for the source backend. Each option is a key-value pair separated by an equals sign, e.g. --src-options key1=value1 key2=value2");
            var dst_opts = OptionWithMultipleTokens(aliases: ["--dst-options"], description: "Options for the destination backend. Each option is a key-value pair separated by an equals sign, e.g. --dst-options key1=value1 key2=value2");
            var verify_contents_opt = new Option<bool>(aliases: ["--verify-contents"], description: "Verify the contents of the files to decide whether the pre-existing destination files should be overwritten", getDefaultValue: () => DEFAULT_VERIFY_CONTENTS);
            var verify_get_after_put_opt = new Option<bool>(aliases: ["--verify-get-after-put"], description: "Verify the files after uploading them to ensure that they were uploaded correctly", getDefaultValue: () => DEFAULT_VERIFY_GET_AFTER_PUT);
            var retry_opt = new Option<int>(aliases: ["--retry"], description: "Number of times to retry on errors", getDefaultValue: () => DEFAULT_RETRY) { Arity = ArgumentArity.ExactlyOne };
            var force_opt = new Option<bool>(aliases: ["--force"], description: "Force the synchronization", getDefaultValue: () => DEFAULT_FORCE);
            var retention_opt = new Option<bool>(aliases: ["--retention"], description: "Toggles whether to keep old files. Any deletes will be renames instead", getDefaultValue: () => DEFAULT_RETENTION);
            var confirm_opt = new Option<bool>(aliases: ["--confirm"], description: "Automatically confirm the operation", getDefaultValue: () => DEFAULT_CONFIRM);
            var global_opts = OptionWithMultipleTokens(aliases: ["--global-options"], description: "Global options all backends. May be overridden by backend specific options (src-options, dst-options). Each option is a key-value pair separated by an equals sign, e.g. --global-options key1=value1 key2=value2");
            var log_level_opt = new Option<string>(aliases: ["--log-level"], description: "The log level to use. If not set here, global options will be checked", getDefaultValue: () => DEFAULT_LOG_LEVEL) { Arity = ArgumentArity.ExactlyOne };
            var log_file_opt = new Option<string>(aliases: ["--log-file"], description: "The log file to write to. If not set here, global options will be checked", getDefaultValue: () => DEFAULT_LOG_FILE) { Arity = ArgumentArity.ExactlyOne };
            var progress_opt = new Option<bool>(aliases: ["--progress"], description: "Print progress to STDOUT", getDefaultValue: () => DEFAULT_PROGRESS);
            var parse_opt = new Option<bool>(aliases: ["--parse-arguments-only"], description: "Only parse the arguments and then exit", getDefaultValue: () => DEFAULT_PARSE_ARGUMENTS_ONLY);

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
            root_cmd.AddOption(verify_contents_opt);
            root_cmd.AddOption(verify_get_after_put_opt);
            root_cmd.AddOption(retry_opt);
            root_cmd.AddOption(force_opt);
            root_cmd.AddOption(retention_opt);
            root_cmd.AddOption(confirm_opt);
            root_cmd.AddOption(global_opts);
            root_cmd.AddOption(log_level_opt);
            root_cmd.AddOption(log_file_opt);
            root_cmd.AddOption(progress_opt);
            root_cmd.AddOption(parse_opt);

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
            GlobalConfig.Force = options["force"] as bool? ?? DEFAULT_FORCE;
            GlobalConfig.DryRun = options["dry-run"] as bool? ?? DEFAULT_DRY_RUN;
            GlobalConfig.LogLevel = options["log-level"] as string ?? DEFAULT_LOG_LEVEL;
            GlobalConfig.LogFile = options["log-file"] as string ?? DEFAULT_LOG_FILE;
            GlobalConfig.VerifyContents = options["verify-contents"] as bool? ?? DEFAULT_VERIFY_CONTENTS;
            GlobalConfig.VerifyGetAfterPut = options["verify-get-after-put"] as bool? ?? DEFAULT_VERIFY_GET_AFTER_PUT;
            var retries = options["retry"] as int? ?? DEFAULT_RETRY;
            var retention = options["retention"] as bool? ?? DEFAULT_RETENTION;
            var confirm = options["confirm"] as bool? ?? DEFAULT_CONFIRM;
            var parse_only = options["parse-arguments-only"] as bool? ?? DEFAULT_PARSE_ARGUMENTS_ONLY;

            // Unpack and parse the multi token options
            Dictionary<string, string> global_options = (options["global-options"] as List<string>)
                ?.Select(x => x.Split("="))
                .ToDictionary(x => x[0], x => x[1])
                ?? [];

            // Parse the log level
            var log_level = options["log-level"] as string ?? DEFAULT_LOG_LEVEL;
            var log_level_parsed = Enum.TryParse<Duplicati.Library.Logging.LogMessageType>(log_level, true, out var log_level_enum);
            log_level_enum = log_level_parsed ? log_level_enum : Duplicati.Library.Logging.LogMessageType.Information;

            using var console_sink = new Duplicati.CommandLine.ConsoleOutput(Console.Out, global_options);
            using var multi_sink = new Duplicati.Library.Main.ControllerMultiLogTarget(console_sink, log_level_enum, null);

            // Parse the log file
            var log_file = options["log-file"] as string ?? DEFAULT_LOG_FILE;
            // The log file sink doesn't have to be disposed, as the multi_sink will take care of it
            Duplicati.Library.Logging.StreamLogDestination? log_file_sink = null;
            if (!string.IsNullOrEmpty(log_file))
            {
                string log_file_dir = SystemIO.IO_OS.PathGetDirectoryName(log_file);
                if (!string.IsNullOrEmpty(log_file_dir) && !SystemIO.IO_OS.DirectoryExists(log_file_dir))
                    SystemIO.IO_OS.DirectoryCreate(log_file_dir);
                log_file_sink = new Duplicati.Library.Logging.StreamLogDestination(log_file);
            }
            multi_sink.AddTarget(log_file_sink, log_level_enum, null);

            // Start the logging scope
            using var _ = Duplicati.Library.Logging.Log.StartScope(multi_sink, log_level_enum);

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

            // Check if we only had to parse the arguments
            if (parse_only)
            {
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Arguments parsed successfully; exiting.");
                return 0;
            }

            // Load the backends
            using var b1 = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(src, src_opts);
            var b1s = b1 as IStreamingBackend;
            System.Diagnostics.Debug.Assert(b1s != null);

            using var b2 = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(dst, dst_opts);
            var b2s = b2 as IStreamingBackend;
            System.Diagnostics.Debug.Assert(b2s != null);

            // Prepare the operations
            var (to_copy, to_delete, to_verify) = PrepareFileLists(b1s, b2s);

            // Verify the files if requested. If the files are not verified, they will be deleted and copied again.
            long verified = 0, failed_verify = 0;
            if (GlobalConfig.VerifyContents)
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
                failed_verify = not_verified.Count();
                verified = to_verify.Count() - failed_verify;

                if (not_verified.Any())
                {
                    Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "rsync", null, "{0} files failed verification. They will be deleted and copied again.", not_verified.Count());
                    to_delete = to_delete.Concat(not_verified);
                    to_copy = to_copy.Concat(not_verified);
                }
            }

            Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "The remote synchronization plan is to {0} {1} files from {2}, then copy {3} files from {4} to {2}.", retention ? "rename" : "delete", to_delete.Count(), b2s.DisplayName, to_copy.Count(), b1s.DisplayName);

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
            long renamed = 0, deleted = 0;
            if (retention)
            {
                renamed = await RenameAsync(b2, to_delete);
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Renamed {0} files in {1}", renamed, b2s.DisplayName);
            }
            else
            {
                deleted = await DeleteAsync(b2s, to_delete);
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Deleted {0} files from {1}", deleted, b2s.DisplayName);
            }

            // Copy the files
            var (copied, copy_errors) = await CopyAsync(b1s, b2s, to_copy);
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
                        (copied, copy_errors) = await CopyAsync(b1s, b2s, copy_errors);
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

            // Results reporting
            if (verified > 0)
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Verified {0} files in {1} that didn't need to be copied", verified, b2s.DisplayName);
            if (failed_verify > 0)
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Failed to verify {0} files in {1}, which were then attempted to be copied", failed_verify, b2s.DisplayName);
            if (copied > 0)
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Copied {0} files from {1} to {2}", copied, b1s.DisplayName, b2s.DisplayName);
            if (deleted > 0)
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Deleted {0} files from {1}", deleted, b2s.DisplayName);
            if (renamed > 0)
                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Renamed {0} files in {1}", renamed, b2s.DisplayName);

            Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "rsync", "Remote synchronization completed successfully");

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
        /// <param name="dry_run">Flag for whether destructive actions (writes) should be printed rather than performed. Downloads (reads) are still being performed.</param>
        /// <param name="verify">Flag for whether to verify each upload to the destination backend.</param>
        /// <returns>A tuple holding the number of succesful copies and a List of the files that failed.</returns>
        private static async Task<(long, IEnumerable<IFileEntry>)> CopyAsync(IStreamingBackend b_src, IStreamingBackend b_dst, IEnumerable<IFileEntry> files)
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
                if (GlobalConfig.Progress)
                    Console.Write($"\rCopying: {i}/{n}");

                Duplicati.Library.Logging.Log.WriteVerboseMessage(LOGTAG, "rsync", "Copying {0} from {1} to {2}", f.Name, b_src.DisplayName, b_dst.DisplayName);

                try
                {
                    sw_get_src.Start();
                    await b_src.GetAsync(f.Name, s_src, CancellationToken.None);
                    sw_get_src.Stop();
                    if (GlobalConfig.DryRun)
                    {
                        Duplicati.Library.Logging.Log.WriteDryrunMessage(LOGTAG, "rsync", "Would write {0} bytes of {1} to {2}", s_src.Length, f.Name, b_dst.DisplayName);
                    }
                    else
                    {
                        sw_put_dst.Start();
                        await b_dst.PutAsync(f.Name, s_src, CancellationToken.None);
                        sw_put_dst.Stop();
                        if (GlobalConfig.VerifyGetAfterPut)
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
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", e, "Error copying {0}: {1}", f.Name, e.Message);
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

            if (GlobalConfig.Progress)
                Console.WriteLine($"\rCopying: {n}/{n}");

            Duplicati.Library.Logging.Log.WriteProfilingMessage(LOGTAG, "rsync", "Copy | Get source: {0} ms, Put destination: {1} ms, Get destination: {2} ms, Get compare: {3} ms", sw_get_src.ElapsedMilliseconds, sw_put_dst.ElapsedMilliseconds, sw_get_dst.ElapsedMilliseconds, sw_get_cmp.ElapsedMilliseconds);

            return (successful_copies, errors);
        }

        /// <summary>
        /// Deletes the files from a backend.
        /// </summary>
        /// <param name="b">The backend to delete the files from.</param>
        /// <param name="files">The files to delete.</param>
        /// <param name="dry_run">Flag for whether the deletion should be printed rather than performed.</param>
        /// <returns>The number of successful deletions.</returns>
        private static async Task<long> DeleteAsync(IStreamingBackend b, IEnumerable<IFileEntry> files)
        {
            long successful_deletes = 0;
            long i = 0, n = files.Count();
            var sw = new System.Diagnostics.Stopwatch();
            sw.Restart();

            foreach (var f in files)
            {
                if (n > 1 && GlobalConfig.Progress)
                {
                    Console.Write($"\rDeleting: {i}/{n}");
                }

                Duplicati.Library.Logging.Log.WriteVerboseMessage(LOGTAG, "rsync", "Deleting {0} from {1}", f.Name, b.DisplayName);

                try
                {
                    if (GlobalConfig.DryRun)
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

                i++;
            }

            sw.Stop();
            Duplicati.Library.Logging.Log.WriteProfilingMessage(LOGTAG, "rsync", "Delete: {0} ms", sw.ElapsedMilliseconds);

            if (GlobalConfig.Progress)
                Console.WriteLine($"\rDeleting: {n}/{n}");
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
        private static (IEnumerable<IFileEntry>, IEnumerable<IFileEntry>, IEnumerable<IFileEntry>) PrepareFileLists(IStreamingBackend b_src, IStreamingBackend b_dst)
        {
            var sw = new System.Diagnostics.Stopwatch();

            sw.Restart();
            var files_src = b_src.List();
            sw.Stop();
            Duplicati.Library.Logging.Log.WriteProfilingMessage(LOGTAG, "rsync", "Prepare | List source: {0} ms", sw.ElapsedMilliseconds);
            sw.Restart();
            var files_dst = b_dst.List();
            sw.Stop();
            Duplicati.Library.Logging.Log.WriteProfilingMessage(LOGTAG, "rsync", "Prepare | List destination: {0} ms", sw.ElapsedMilliseconds);

            // Shortcut for force
            if (GlobalConfig.Force)
            {
                return (files_src, files_dst, []);
            }

            // Shortcut for empty destination
            if (!files_dst.Any())
            {
                return (files_src, [], []);
            }

            sw.Restart();
            var lookup_src = files_src.ToDictionary(x => x.Name);
            var lookup_dst = files_dst.ToDictionary(x => x.Name);
            sw.Stop();
            Duplicati.Library.Logging.Log.WriteProfilingMessage(LOGTAG, "rsync", "Prepare | Build lookup for source and destination: {0} ms", sw.ElapsedMilliseconds);

            var to_copy = new List<IFileEntry>();
            var to_delete = new HashSet<string>();
            var to_verify = new List<IFileEntry>();

            // Find all of the files in src that are not in dst, have a different size or have a more recent modification date
            sw.Restart();
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
            sw.Stop();
            Duplicati.Library.Logging.Log.WriteProfilingMessage(LOGTAG, "rsync", "Prepare | Check the files that are present in source against destination: {0} ms", sw.ElapsedMilliseconds);

            // Find all of the files in dst that are not in src
            sw.Start();
            foreach (var f_dst in files_dst)
            {
                if (to_delete.Contains(f_dst.Name))
                    continue;

                if (!lookup_src.ContainsKey(f_dst.Name))
                {
                    to_delete.Add(f_dst.Name);
                }
            }
            sw.Stop();
            Duplicati.Library.Logging.Log.WriteProfilingMessage(LOGTAG, "rsync", "Prepare | Check the files that are present in destination against source: {0} ms", sw.ElapsedMilliseconds);

            sw.Restart();
            var to_delete_lookedup = to_delete.Select(x => lookup_dst[x]).ToList();
            sw.Stop();
            Duplicati.Library.Logging.Log.WriteProfilingMessage(LOGTAG, "rsync", "Prepare | Lookup the files to delete: {0} ms", sw.ElapsedMilliseconds);

            return (to_copy, to_delete_lookedup, to_verify);
        }

        /// <summary>
        /// Renames the files in a backend.
        /// The renaming is done by deleting the file and re-uploading it with a new name.
        /// </summary>
        /// <param name="b">The backend to rename the files in.</param>
        /// <param name="files">The files to rename.</param>
        /// <param name="dry_run">Flag for whether the renaming should be printed rather than performed.</param>
        /// <returns>The number of successful renames.</returns>
        private static async Task<long> RenameAsync(IBackend b, IEnumerable<IFileEntry> files)
        {
            long successful_renames = 0;
            string suffix = $"{System.DateTime.Now:yyyyMMddHHmmss}.old";
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
                            if (GlobalConfig.Progress)
                                Console.Write($"\rRenaming: {i}/{n}");

                            Duplicati.Library.Logging.Log.WriteVerboseMessage(LOGTAG, "rsync", "Renaming {0} to {0}.{1} by deleting and re-uploading {2} bytes to {3}", f.Name, suffix, downloaded.Length, sb.DisplayName);

                            try
                            {
                                sw_get_src.Start();
                                await sb.GetAsync(f.Name, downloaded, CancellationToken.None);
                                sw_get_src.Stop();

                                if (GlobalConfig.DryRun)
                                {
                                    Duplicati.Library.Logging.Log.WriteDryrunMessage(LOGTAG, "rsync", "Would rename {0} to {0}.{1} by deleting and re-uploading {2} bytes to {3}", f.Name, suffix, downloaded.Length, sb.DisplayName);
                                }
                                else
                                {
                                    sw_put_dst.Start();
                                    await sb.PutAsync($"{f.Name}.{suffix}", downloaded, CancellationToken.None);
                                    sw_put_dst.Stop();
                                    sw_del_src.Start();
                                    await sb.DeleteAsync(f.Name, CancellationToken.None);
                                    sw_del_src.Stop();
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

                                // Stop any running timers
                                sw_get_src.Stop();
                                sw_put_dst.Stop();
                                sw_del_src.Stop();
                            }

                            i++;
                        }

                        Duplicati.Library.Logging.Log.WriteProfilingMessage(LOGTAG, "rsync", "Rename | Get source: {0} ms, Put destination: {1} ms, Delete source: {2} ms", sw_get_src.ElapsedMilliseconds, sw_put_dst.ElapsedMilliseconds, sw_del_src.ElapsedMilliseconds);

                        if (GlobalConfig.Progress)
                            Console.WriteLine($"\rRenaming: {n}/{n}");

                        return successful_renames;
                    }
                case IRenameEnabledBackend rb:
                    {
                        var sw = new System.Diagnostics.Stopwatch();

                        foreach (var f in files)
                        {
                            if (GlobalConfig.Progress)
                                Console.Write($"\rRenaming: {i}/{n}");

                            Duplicati.Library.Logging.Log.WriteVerboseMessage(LOGTAG, "rsync", "Renaming {0} to {0}.{1} by calling Rename on {2}", f.Name, suffix, rb.DisplayName);

                            try
                            {
                                if (GlobalConfig.DryRun)
                                {
                                    Duplicati.Library.Logging.Log.WriteDryrunMessage(LOGTAG, "rsync", "Would rename {0} to {0}.{1} by calling Rename on {2}", f.Name, suffix, rb.DisplayName);
                                }
                                else
                                {
                                    sw.Start();
                                    await rb.RenameAsync(f.Name, $"{f.Name}.{suffix}", CancellationToken.None);
                                    sw.Stop();
                                }
                                successful_renames++;
                            }
                            catch (Exception e)
                            {
                                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", e, "Error renaming {0}: {1}", f.Name, e.Message);
                                sw.Stop();
                            }

                            i++;
                        }

                        Duplicati.Library.Logging.Log.WriteProfilingMessage(LOGTAG, "rsync", "Rename: {0} ms", sw.ElapsedMilliseconds);

                        if (GlobalConfig.Progress)
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
        /// <returns>A list of the files that failed verification.</returns>
        private static async Task<IEnumerable<IFileEntry>> VerifyAsync(IStreamingBackend b_src, IStreamingBackend b_dst, IEnumerable<IFileEntry> files)
        {
            var errors = new List<IFileEntry>();
            using var s_src = new MemoryStream();
            using var s_dst = new MemoryStream();
            long i = 0, n = files.Count();
            var sw_get = new System.Diagnostics.Stopwatch();
            var sw_cmp = new System.Diagnostics.Stopwatch();

            foreach (var f in files)
            {
                if (GlobalConfig.Progress)
                    Console.Write($"\rVerifying: {i}/{n}");

                Duplicati.Library.Logging.Log.WriteVerboseMessage(LOGTAG, "rsync", "Verifying {0} by downloading and comparing {1} bytes from {2} and {3}", f.Name, s_src.Length, b_dst.DisplayName, b_src.DisplayName);

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
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", e, "Error during verification of {0}: {1}", f.Name, e.Message);
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

            Duplicati.Library.Logging.Log.WriteProfilingMessage(LOGTAG, "rsync", "Verify | Get: {0} ms, Compare: {1} ms", sw_get.ElapsedMilliseconds, sw_cmp.ElapsedMilliseconds);

            if (GlobalConfig.Progress)
                Console.WriteLine($"\rVerifying: {n}/{n}");

            return errors;
        }

    }
}
