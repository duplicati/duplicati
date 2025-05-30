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
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;

namespace Duplicati.CommandLine
{
    public static class Commands
    {
        private class PeriodicOutput : IDisposable
        {
            public event Action<float, long, long, bool> WriteOutput;

            private readonly System.Threading.ManualResetEvent m_readyEvent;
            private readonly System.Threading.ManualResetEvent m_finishEvent;
            private readonly ConsoleOutput m_output;
            private readonly TimeSpan m_updateFrequency;
            private Task m_task;
            private readonly CancellationTokenSource m_cancellationTokenSource;

            public PeriodicOutput(ConsoleOutput messageSink, TimeSpan updateFrequency)
            {
                m_output = messageSink;
                m_readyEvent = new System.Threading.ManualResetEvent(false);
                m_finishEvent = new System.Threading.ManualResetEvent(false);
                m_updateFrequency = updateFrequency;
                m_cancellationTokenSource = new CancellationTokenSource();
                m_task = Task.Run(() => this.ThreadMain(m_cancellationTokenSource.Token), m_cancellationTokenSource.Token);
            }

            public void SetReady() { m_readyEvent.Set(); }
            public void SetFinished() { m_finishEvent.Set(); }

            public bool Join(TimeSpan wait)
            {
                if (m_task != null)
                    return this.m_task.Wait(wait);

                return true;
            }

            private void ThreadMain(CancellationToken cancellationToken)
            {
                m_readyEvent.WaitOne();
                if (m_finishEvent.WaitOne(TimeSpan.FromMilliseconds(10), true))
                    return;

                var last_count = -1L;
                var finished = false;

                while (!cancellationToken.IsCancellationRequested)
                {
                    float progress;
                    long filesprocessed;
                    long filesizeprocessed;
                    long filecount;
                    long filesize;
                    bool counting;
                    m_output.OperationProgress.UpdateOverall(out _, out progress, out filesprocessed, out filesizeprocessed, out filecount, out filesize, out counting);

                    var files = Math.Max(0, filecount - filesprocessed);
                    var size = Math.Max(0, filesize - filesizeprocessed);

                    if (finished)
                    {
                        files = 0;
                        size = 0;
                    }
                    else if (size > 0)
                        files = Math.Max(1, files);

                    if (last_count < 0 || files != last_count)
                        if (WriteOutput != null)
                            WriteOutput(progress, files, size, counting);

                    if (!counting)
                        last_count = files;

                    if (finished)
                        return;

                    finished = m_finishEvent.WaitOne(m_updateFrequency, true);
                }
            }

            public void Dispose()
            {
                if (m_task != null)
                {
                    try
                    {
                        m_finishEvent.Set();
                        m_readyEvent.Set();

                        if (m_task != null)
                        {
                            m_cancellationTokenSource.Cancel();
                            m_task.Wait(500);
                        }
                    }
                    finally
                    {
                        m_task = null;
                    }
                }
            }
        }

        public static int Examples(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            using (new Duplicati.Library.Main.LocaleChange(options))
                Duplicati.CommandLine.Help.PrintUsage(outwriter, "example", options);
            return 0;
        }

        public static int Help(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            using (new Duplicati.Library.Main.LocaleChange(options))
                Duplicati.CommandLine.Help.PrintUsage(outwriter, args.Count > 1 ? args[1] : "help", options);
            return 0;
        }

        public static int Affected(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            var fullresult = Duplicati.Library.Utility.Utility.ParseBoolOption(options, "full-result");
            var backend = args[0];
            args.RemoveAt(0);

            if (args.Count == 0)
            {
                outwriter.WriteLine("You must specify at least a remote filename");
                return 200;
            }

            // Support for not adding the --auth-username if possible
            string dbpath;
            options.TryGetValue("dbpath", out dbpath);
            if (string.IsNullOrEmpty(dbpath))
            {
                dbpath = Library.Main.CLIDatabaseLocator.GetDatabasePathForCLI(backend, new Library.Main.Options(options), false, true);
                if (dbpath != null)
                    options["dbpath"] = dbpath;
            }

            // Don't ask for passphrase if we have a local db
            if (!string.IsNullOrEmpty(dbpath) && System.IO.File.Exists(dbpath) && !options.ContainsKey("no-encryption") && !Duplicati.Library.Utility.Utility.ParseBoolOption(options, "no-local-db"))
            {
                string passphrase;
                options.TryGetValue("passphrase", out passphrase);
                if (string.IsNullOrEmpty(passphrase))
                    options["no-encryption"] = "true";
            }

            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller(backend, options, console))
            {
                setup(i);
                i.ListAffected(args, res =>
                {
                    if (res.Filesets != null && res.Filesets.Any())
                    {
                        outwriter.WriteLine("The following filesets are affected:");
                        foreach (var e in res.Filesets)
                            outwriter.WriteLine("{0}\t: {1}", e.Version, e.Time);
                        outwriter.WriteLine();
                    }

                    if (res.Files != null)
                    {
                        var filecount = res.Files.Count();
                        if (filecount == 0)
                        {
                            outwriter.WriteLine("No files are affected");
                        }
                        else
                        {
                            var c = 0;
                            outwriter.WriteLine("A total of {0} file(s) are affected:", filecount);
                            foreach (var file in res.Files)
                            {
                                c++;
                                outwriter.WriteLine(file.Path);
                                if (c > 10 && filecount > 10 && !fullresult)
                                {
                                    outwriter.WriteLine("  ... and {0} more (use --{1} to see all filenames)", filecount - c, "full-result");
                                    break;
                                }
                            }

                        }

                        outwriter.WriteLine();
                    }

                    if (res.LogMessages != null)
                    {
                        var logcount = res.LogMessages.Count();
                        if (logcount == 0 || (logcount > 10 && !fullresult))
                            outwriter.WriteLine("Found {0} related log messages (use --{1} to see the data)", res.Files.Count(), "full-result");
                        else
                        {
                            outwriter.WriteLine("The following related log messages were found:");
                            foreach (var log in res.LogMessages)
                                if (log.Message.Length > 100 && !fullresult)
                                    outwriter.WriteLine("{0}: {1}", log.Timestamp, log.Message.Substring(0, 96) + " ...");
                                else
                                    outwriter.WriteLine("{0}: {1}", log.Timestamp, log.Message);
                        }

                        outwriter.WriteLine();
                    }
                });



                return 0;
            }
        }

        /// <summary>
        /// For bare file names with no wildcards, replace argument with
        /// the equivalent of prefixing with "*/" so we search all folders.
        /// </summary>
        public static IEnumerable<string> PrefixArgsWithAsterisk(IEnumerable<string> argList) => argList.Select(PrefixArgWithAsterisk);

        /// <summary>
        /// For bare file names with no wildcards, return argument with
        /// the equivalent of prefixing with "*/" so we search all folders.
        /// </summary>
        private static string PrefixArgWithAsterisk(string arg)
        {
            var containsSeparators = ContainsDirectorySeparators(arg);
            var containsWildcards = ContainsWildcards(arg);

            if (!containsSeparators && arg.StartsWith("@", StringComparison.Ordinal))
            {
                // Convert to Regexp filter and prefix with ".*/"
                return $"[.*{Library.Utility.Utility.ConvertLiteralToRegExp(Util.DirectorySeparatorString + arg.Substring(1))}]";
            }
            else if (!containsSeparators && !containsWildcards && !arg.StartsWith("[", StringComparison.Ordinal))
            {
                // Prefix files with "*/"
                return "*" + Util.DirectorySeparatorString + arg;
            }
            else
            {
                return arg;
            }
        }

        /// <summary>
        /// For folders, replace argument with the equivalent of suffixing
        /// with "*" so we restore contents in the folder.
        /// </summary>
        public static IEnumerable<string> SuffixArgsWithAsterisk(IEnumerable<string> argList) => argList.Select(SuffixArgWithAsterisk);

        /// <summary>
        /// For folders, return argument with the equivalent of suffixing
        /// with "*" so we restore contents in the folder.
        /// </summary>
        private static string SuffixArgWithAsterisk(string arg)
        {
            var containsWildcards = ContainsWildcards(arg);
            var endsWithSeparator = arg.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal);

            if (endsWithSeparator && arg.StartsWith("@", StringComparison.Ordinal))
            {
                // Convert to Regexp filter and suffix with ".*"
                return $"[{Library.Utility.Utility.ConvertLiteralToRegExp(arg.Substring(1))}.*]";
            }
            else if (endsWithSeparator && !containsWildcards && !arg.StartsWith("[", StringComparison.Ordinal))
            {
                // Suffix with "*"
                return arg + "*";
            }
            else
            {
                return arg;
            }
        }

        private static readonly char[] pathSeparatorsCharacters = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

        /// <summary>
        /// Returns true if <paramref name="s"/> contains directory separator characters.
        /// </summary>
        public static bool ContainsDirectorySeparators(string s) => s.IndexOfAny(pathSeparatorsCharacters) >= 0;

        private static readonly char[] wildcardCharacters = new[] { '*', '?' };

        /// <summary>
        /// Returns true if <paramref name="s"/> contains wildcard characters.
        /// </summary>
        public static bool ContainsWildcards(string s) => s.IndexOfAny(wildcardCharacters) >= 0;

        private static void HandleDbAccessWithoutPassphrase(string backend, Dictionary<string, string> options)
        {
            // Support for not adding the --auth-username if possible
            string dbpath;
            options.TryGetValue("dbpath", out dbpath);
            if (string.IsNullOrEmpty(dbpath))
            {
                dbpath = Library.Main.CLIDatabaseLocator.GetDatabasePathForCLI(backend, new Duplicati.Library.Main.Options(options), false, true);
                if (dbpath != null)
                    options["dbpath"] = dbpath;
            }

            // Don't ask for passphrase if we have a local db
            if (!string.IsNullOrEmpty(dbpath) && System.IO.File.Exists(dbpath) && !options.ContainsKey("no-encryption") && !Duplicati.Library.Utility.Utility.ParseBoolOption(options, "no-local-db"))
            {
                string passphrase;
                options.TryGetValue("passphrase", out passphrase);
                if (string.IsNullOrEmpty(passphrase))
                {
                    string existing;
                    options.TryGetValue("disable-module", out existing);
                    if (string.IsNullOrWhiteSpace(existing))
                        options["disable-module"] = "console-password-input";
                    else
                        options["disable-module"] = string.Join(",", new string[] { existing, "console-password-input" });
                }
            }
        }

        public static int ListFilesets(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter _)
        {
            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller(args[0], options, console))
            {
                setup(i);
                var backend = args[0];
                args.RemoveAt(0);

                HandleDbAccessWithoutPassphrase(backend, options);

                if (args.Count != 0)
                {
                    outwriter.WriteLine("You cannot specify any arguments for the list-filesets command");
                    return 200;
                }

                if (options.ContainsKey("version"))
                {
                    outwriter.WriteLine("You cannot specify a version for the list-filesets command");
                    return 200;
                }

                if (options.ContainsKey("time"))
                {
                    outwriter.WriteLine("You cannot specify a time for the list-filesets command");
                    return 200;
                }

                var res = i.ListFilesets();
                outwriter.WriteLine("Listing filesets:");
                foreach (var e in res.Filesets)
                {
                    if (e.FileCount != null && e.FileSizes != null && e.IsFullBackup != null)
                        outwriter.WriteLine($"{e.Version}\t: {e.Time} ({e.FileCount.Value} files, {Library.Utility.Utility.FormatSizeString(e.FileSizes.Value)}){(e.IsFullBackup.Value ? "" : ", partial")}");
                    else
                        outwriter.WriteLine($"{e.Version}\t: {e.Time}");
                }
            }

            return 0;
        }

        public static int ListFolderContent(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter _)
        {
            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller(args[0], options, console))
            {
                setup(i);
                var backend = args[0];
                args.RemoveAt(0);

                HandleDbAccessWithoutPassphrase(backend, options);

                if (options.ContainsKey("all-versions"))
                {
                    outwriter.WriteLine("You cannot specify all-versions for the list-folder command");
                    return 200;
                }

                var res = i.ListFolder(args.ToArray(), 0, 0);
                outwriter.WriteLine("Folder contents:");
                foreach (var e in res.Entries.Items)
                {
                    if (e.IsDirectory)
                        outwriter.WriteLine($"{e.Path}: (directory)");
                    else if (e.IsSymlink)
                        outwriter.WriteLine($"{e.Path}: (symlink)");
                    else
                        outwriter.WriteLine($"{e.Path}: ({Library.Utility.Utility.FormatSizeString(e.Size)})");
                }
            }

            return 0;
        }

        public static int ListFileVersions(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter _)
        {
            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller(args[0], options, console))
            {
                setup(i);
                var backend = args[0];
                args.RemoveAt(0);

                HandleDbAccessWithoutPassphrase(backend, options);

                var res = i.ListFileVersions(args.ToArray(), 0, 0);
                outwriter.WriteLine("File versions:");
                var prevFile = string.Empty;
                foreach (var e in res.FileVersions.Items)
                {
                    if (e.Path != prevFile)
                    {
                        outwriter.WriteLine($"{e.Path}:");
                        prevFile = e.Path;
                    }
                    if (e.IsDirectory)
                        outwriter.WriteLine($"  [{e.Version}] {e.Time}: (directory)");
                    else if (e.IsSymlink)
                        outwriter.WriteLine($"  [{e.Version}] {e.Time}: (symlink)");
                    else
                        outwriter.WriteLine($"  [{e.Version}] {e.Time}: ({Library.Utility.Utility.FormatSizeString(e.Size)})");
                }
            }

            return 0;
        }

        public static int SearchEntries(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller(args[0], options, console))
            {
                setup(i);
                var backend = args[0];
                args.RemoveAt(0);

                HandleDbAccessWithoutPassphrase(backend, options);

                var res = i.SearchEntries(args.ToArray(), filter, 0, 0);
                outwriter.WriteLine("File versions:");
                var prevFile = string.Empty;
                foreach (var e in res.FileVersions.Items)
                {
                    if (e.Path != prevFile)
                    {
                        outwriter.WriteLine($"{e.Path}:");
                        prevFile = e.Path;
                    }
                    if (e.IsDirectory)
                        outwriter.WriteLine($"  [{e.Version}] {e.Time}: (directory)");
                    else if (e.IsSymlink)
                        outwriter.WriteLine($"  [{e.Version}] {e.Time}: (symlink)");
                    else
                        outwriter.WriteLine($"  [{e.Version}] {e.Time}: ({Library.Utility.Utility.FormatSizeString(e.Size)})");
                }
            }

            return 0;
        }

        public static int List(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            filter = filter ?? new Duplicati.Library.Utility.FilterExpression();
            if (Duplicati.Library.Utility.Utility.ParseBoolOption(options, "list-sets-only"))
                filter = new Duplicati.Library.Utility.FilterExpression();

            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller(args[0], options, console))
            {
                setup(i);
                var backend = args[0];
                args.RemoveAt(0);

                if (args.Count == 1)
                {
                    long v;
                    if (long.TryParse(args[0], out v))
                    {
                        if (!options.ContainsKey("version"))
                        {
                            args.RemoveAt(0);
                            args.Add("*");
                            options["version"] = v.ToString();
                        }
                    }
                    else if (!ContainsWildcards(args[0]) && !args[0].StartsWith("[", StringComparison.Ordinal) && !args[0].StartsWith("@", StringComparison.Ordinal))
                    {
                        try
                        {
                            var t = Library.Utility.Timeparser.ParseTimeInterval(args[0], DateTime.Now, true);
                            args.RemoveAt(0);
                            args.Add("*");
                            options["time"] = t.ToString();
                        }
                        catch
                        {
                        }
                    }
                }

                args = PrefixArgsWithAsterisk(args).ToList();

                HandleDbAccessWithoutPassphrase(backend, options);

                bool controlFiles = Library.Utility.Utility.ParseBoolOption(options, "control-files");
                options.Remove("control-files");

                var res = controlFiles ? i.ListControlFiles(args, filter) : i.List(args, filter);

                //If there are no files matching, and we are looking for one or more files,
                // try again with all-versions set
                var compareFilter = Library.Utility.JoinedFilterExpression.Join(new Library.Utility.FilterExpression(args), filter);
                var isRequestForFiles =
                    !controlFiles && res.Filesets.Any() &&
                    (res.Files == null || !res.Files.Any()) &&
                    !compareFilter.Empty;

                if (isRequestForFiles && !Library.Utility.Utility.ParseBoolOption(options, "all-versions"))
                {
                    outwriter.WriteLine("No files matching, looking in all versions");
                    options["all-versions"] = "true";
                    options.Remove("time");
                    options.Remove("version");
                    res = i.List(args, filter);
                }

                if (res.Filesets.Any() && (res.Files == null || !res.Files.Any()) && compareFilter.Empty)
                {
                    outwriter.WriteLine("Listing filesets:");

                    foreach (var e in res.Filesets)
                    {
                        if (e.FileCount >= 0)
                            outwriter.WriteLine("{0}\t: {1} ({2} files, {3})", e.Version, e.Time, e.FileCount, Library.Utility.Utility.FormatSizeString(e.FileSizes));
                        else
                            outwriter.WriteLine("{0}\t: {1}", e.Version, e.Time);
                    }
                }
                else if (isRequestForFiles)
                {
                    outwriter.WriteLine("No files matched expression");
                }
                else
                {
                    if (!res.Filesets.Any())
                    {
                        outwriter.WriteLine("No time or version matched a fileset");
                    }
                    else if (res.Files == null || !res.Files.Any())
                    {
                        outwriter.WriteLine("Found {0} filesets, but no files matched", res.Filesets.Count());
                    }
                    else if (res.Filesets.Count() == 1)
                    {
                        var f = res.Filesets.First();
                        outwriter.WriteLine("Listing contents {0} ({1}):", f.Version, f.Time);
                        foreach (var e in res.Files)
                            outwriter.WriteLine("{0} {1}", e.Path, e.Path.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal) ? "" : "(" + Library.Utility.Utility.FormatSizeString(e.Sizes.First()) + ")");
                    }
                    else
                    {
                        outwriter.WriteLine("Listing files and versions:");
                        foreach (var e in res.Files)
                        {
                            outwriter.WriteLine(e.Path);
                            foreach (var nx in res.Filesets.Zip(e.Sizes, (a, b) => new { Index = a.Version, Time = a.Time, Size = b }))
                                outwriter.WriteLine("{0}\t: {1} {2}", nx.Index, nx.Time, nx.Size < 0 ? " - " : Library.Utility.Utility.FormatSizeString(nx.Size));

                            outwriter.WriteLine();
                        }

                    }
                }
            }

            return 0;
        }

        public static int Delete(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            var requiredOptions = new string[] { "keep-time", "keep-versions", "version", "retention-policy" };

            if (!options.Keys.Any(x => requiredOptions.Contains(x, StringComparer.OrdinalIgnoreCase)))
            {
                outwriter.WriteLine(Strings.Program.DeleteCommandNeedsOptions("delete", requiredOptions));
                return 200;
            }

            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller(args[0], options, console))
            {
                setup(i);
                args.RemoveAt(0);
                var res = i.Delete();

                if (!res.DeletedSets.Any())
                {
                    outwriter.WriteLine(Strings.Program.NoFilesetsMatching);
                }
                else
                {
                    if (res.Dryrun)
                        outwriter.WriteLine(Strings.Program.WouldDeleteBackups);
                    else
                        outwriter.WriteLine(Strings.Program.DeletedBackups);

                    foreach (var f in res.DeletedSets)
                        outwriter.WriteLine("{0}: {1}", f.Item1, f.Item2);
                }
            }

            return 0;

        }

        public static int Repair(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(outwriter, args, 1);

            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Duplicati.Library.Main.Controller(args[0], options, console))
            {
                setup(i);
                i.Repair(filter);
            }

            return 0;
        }

        public static int Restore(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count < 1)
                return PrintWrongNumberOfArguments(outwriter, args, 1);

            string backend = args[0];
            args.RemoveAt(0);

            bool controlFiles = Library.Utility.Utility.ParseBoolOption(options, "control-files");
            options.Remove("control-files");

            args = SuffixArgsWithAsterisk(PrefixArgsWithAsterisk(args)).ToList();

            using (var output = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller(backend, options, output))
            {
                output.MessageEvent(string.Format("Restore started at {0}", DateTime.Now));
                setup(i);
                if (controlFiles)
                {
                    var res = i.RestoreControlFiles(args.ToArray(), filter);
                    output.MessageEvent("Restore control files completed:");
                    foreach (var s in res.Files)
                        outwriter.WriteLine(s);
                }
                else
                {
                    using (var periodicOutput = new PeriodicOutput(output, TimeSpan.FromSeconds(5)))
                    {
                        output.PhaseChanged += (phase, previousPhase) =>
                        {
                            switch (phase)
                            {
                                case Duplicati.Library.Main.OperationPhase.Restore_PreRestoreVerify:
                                    output.MessageEvent("Checking remote backup ...");
                                    break;
                                case Duplicati.Library.Main.OperationPhase.Restore_ScanForExistingFiles:
                                    output.MessageEvent("Checking existing target files ...");
                                    break;
                                case Duplicati.Library.Main.OperationPhase.Restore_PatchWithLocalBlocks:
                                    output.MessageEvent("Updating target files with local data ...");
                                    break;
                                case Duplicati.Library.Main.OperationPhase.Restore_PostRestoreVerify:
                                    periodicOutput.SetFinished();
                                    periodicOutput.Join(TimeSpan.FromMilliseconds(100));
                                    output.MessageEvent("Verifying restored files ...");
                                    break;
                                case Duplicati.Library.Main.OperationPhase.Restore_ScanForLocalBlocks:
                                    output.MessageEvent("Scanning local files for needed data ...");
                                    break;
                                case Duplicati.Library.Main.OperationPhase.Restore_CreateTargetFolders:
                                    periodicOutput.SetReady();
                                    break;
                            }
                        };

                        periodicOutput.WriteOutput += (progress, files, size, counting) =>
                        {
                            output.MessageEvent(string.Format("  {0} files need to be restored ({1})", files, Library.Utility.Utility.FormatSizeString(size)));
                        };

                        var res = i.Restore(args.ToArray(), filter);
                        string restorePath;
                        options.TryGetValue("restore-path", out restorePath);

                        output.MessageEvent(string.Format("Restored {0} ({1}) files to {2}", res.RestoredFiles, Library.Utility.Utility.FormatSizeString(res.SizeOfRestoredFiles), string.IsNullOrEmpty(restorePath) ? "original path" : restorePath));
                        output.MessageEvent(string.Format("Duration of restore: {0:hh\\:mm\\:ss}", res.Duration));

                        if (output.FullResults)
                            Library.Utility.Utility.PrintSerializeObject(res, outwriter);

                        if (res.Warnings.Any())
                            return 2;
                    }
                }
            }

            return 0;
        }

        public static int Backup(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count < 2)
                return PrintWrongNumberOfArguments(outwriter, args, 2);

            var backend = args[0];
            args.RemoveAt(0);
            var dirs = args.ToArray();
            Library.Interface.IBackupResults result;
            using (var output = new ConsoleOutput(outwriter, options))
            {
                using (var periodicOutput = new PeriodicOutput(output, TimeSpan.FromSeconds(5)))
                {
                    if ((new Duplicati.Library.Main.Options(options)).DisableOnBattery && (Duplicati.Library.Utility.Power.PowerSupply.GetSource() == Duplicati.Library.Utility.Power.PowerSupply.Source.Battery))
                    {
                        output.MessageEvent("The \"disable-on-battery\" option only affects scheduled backups and is ignored by backups run manually or from the command line.");
                    }

                    output.MessageEvent(string.Format("Backup started at {0}", DateTime.Now));

                    output.PhaseChanged += (phase, previousPhase) =>
                    {
                        if (previousPhase == Duplicati.Library.Main.OperationPhase.Backup_PostBackupTest)
                            output.MessageEvent("Remote backup verification completed");

                        switch (phase)
                        {
                            case Duplicati.Library.Main.OperationPhase.Backup_ProcessingFiles:
                                output.MessageEvent("Scanning source files ...");
                                periodicOutput.SetReady();
                                break;
                            case Duplicati.Library.Main.OperationPhase.Backup_Finalize:
                                periodicOutput.SetFinished();
                                break;
                            case Duplicati.Library.Main.OperationPhase.Backup_PreBackupVerify:
                                output.MessageEvent("Checking remote backup ...");
                                break;
                            case Duplicati.Library.Main.OperationPhase.Backup_PostBackupVerify:
                                output.MessageEvent("Checking remote backup ...");
                                break;
                            case Duplicati.Library.Main.OperationPhase.Backup_PostBackupTest:
                                output.MessageEvent("Verifying remote backup ...");
                                break;
                            case Duplicati.Library.Main.OperationPhase.Backup_Compact:
                                output.MessageEvent("Compacting remote backup ...");
                                break;
                        }
                    };

                    periodicOutput.WriteOutput += (progress, files, size, counting) =>
                    {
                        output.MessageEvent(string.Format("  {0} files need to be examined ({1}){2}", files, Library.Utility.Utility.FormatSizeString(size), counting ? " (still counting)" : ""));
                    };

                    using (var i = new Library.Main.Controller(backend, options, output))
                    {
                        setup(i);
                        result = i.Backup(dirs, filter);
                    }
                }

                if (output.FullResults)
                {
                    Library.Utility.Utility.PrintSerializeObject(result, outwriter);
                    outwriter.WriteLine();
                }

                var parsedStats = result.BackendStatistics as Duplicati.Library.Interface.IParsedBackendStatistics;
                output.MessageEvent(string.Format("  Duration of backup: {0:c}", result.Duration));
                if (parsedStats != null)
                {
                    if (parsedStats.KnownFileCount > 0)
                    {
                        output.MessageEvent(string.Format("  Remote files: {0}", parsedStats.KnownFileCount));
                        output.MessageEvent(string.Format("  Remote filesets: {0}", parsedStats.KnownFilesets));
                        output.MessageEvent(string.Format("  Remote size: {0}", Library.Utility.Utility.FormatSizeString(parsedStats.KnownFileSize)));
                    }

                    if (parsedStats.TotalQuotaSpace >= 0)
                    {
                        output.MessageEvent(string.Format("  Total remote quota: {0}", Library.Utility.Utility.FormatSizeString(parsedStats.TotalQuotaSpace)));
                    }

                    if (parsedStats.FreeQuotaSpace >= 0)
                    {
                        output.MessageEvent(string.Format("  Available remote quota: {0}", Library.Utility.Utility.FormatSizeString(parsedStats.FreeQuotaSpace)));
                    }
                }

                output.MessageEvent(string.Format("  Files added: {0}", result.AddedFiles));
                output.MessageEvent(string.Format("  Files deleted: {0}", result.DeletedFiles));
                output.MessageEvent(string.Format("  Files changed: {0}", result.ModifiedFiles));

                output.MessageEvent(string.Format("  Data uploaded: {0}", Library.Utility.Utility.FormatSizeString(result.BackendStatistics.BytesUploaded)));
                output.MessageEvent(string.Format("  Data downloaded: {0}", Library.Utility.Utility.FormatSizeString(result.BackendStatistics.BytesDownloaded)));

                if (result.ExaminedFiles == 0 && (filter != null && !filter.Empty))
                    output.MessageEvent("No files were processed. If this was not intentional you may want to use the \"test-filters\" command");

                output.MessageEvent("Backup completed successfully!");

                //Interrupted = 50
                if (result.PartialBackup)
                    return 50;

                //Completed with errors = 3
                if (result.ParsedResult == Library.Interface.ParsedResultType.Error)
                    return 3;

                //Completed with warnings = 2
                if (result.ParsedResult == Library.Interface.ParsedResultType.Warning)
                    return 2;

                //Success, but no upload = 1
                if (result.BackendStatistics.BytesUploaded == 0)
                    return 1;

                return 0;
            }
        }

        public static int Compact(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(outwriter, args, 1);

            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller(args[0], options, console))
            {
                setup(i);
                i.Compact();
            }

            return 0;
        }

        public static int Test(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            var fullResults = Duplicati.Library.Utility.Utility.ParseBoolOption(options, "full-result");
            if (args.Count != 1 && args.Count != 2)
                return PrintWrongNumberOfArguments(outwriter, args, 1);

            var tests = 1L;
            if (args.Count == 2)
            {
                if (new string[] { "all", "everything" }.Contains(args[1], StringComparer.OrdinalIgnoreCase))
                    tests = long.MaxValue;
                else
                    tests = Convert.ToInt64(args[1]);
            }

            Library.Interface.ITestResults result;
            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller(args[0], options, console))
            {
                setup(i);
                result = i.Test(tests);
            }

            var totalFiles = result.Verifications.Count();
            if (totalFiles == 0)
            {
                outwriter.WriteLine("No files examined, is the remote destination is empty?");
                return 100;
            }
            else
            {
                var filtered = from n in result.Verifications where n.Value.Any() select n;
                if (!filtered.Any())
                {
                    outwriter.WriteLine("Examined {0} files and found no errors", totalFiles);
                    return 0;
                }
                else
                {
                    foreach (var n in result.Verifications)
                    {
                        var changecount = n.Value.Count();
                        if (changecount == 0)
                        {
                            if (fullResults)
                                outwriter.WriteLine("{0}: No errors", n.Key);
                        }
                        else
                        {
                            outwriter.WriteLine("{0}: {1} errors", n.Key, changecount);
                            var count = 0;
                            foreach (var c in n.Value)
                            {
                                count++;
                                outwriter.WriteLine("\t{0}: {1}", c.Key, c.Value);
                                if (!fullResults && count == 10 && changecount > 10)
                                {
                                    outwriter.WriteLine("\t... and {0} more", changecount - count);
                                    break;
                                }
                            }

                            outwriter.WriteLine();
                        }
                    }

                    return 3;
                }
            }
        }

        private static int PrintWrongNumberOfArguments(TextWriter outwriter, List<string> args, int expected)
        {
            outwriter.WriteLine(Strings.Program.WrongNumberOfCommandsError_v2(args.Count, expected, args.Select(n => "\"" + n + "\"").ToArray()));
            return 200;
        }

        public static int PrintInvalidCommand(TextWriter outwriter, string command)
        {
            outwriter.WriteLine(Strings.Program.InvalidCommandError(command));
            return 200;
        }

        public static int CreateBugReport(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            // Support for not adding the --auth-username if possible
            string dbpath = null;
            options.TryGetValue("dbpath", out dbpath);
            if (string.IsNullOrEmpty(dbpath))
            {
                if (args.Count > 0)
                    dbpath = Library.Main.CLIDatabaseLocator.GetDatabasePathForCLI(args[0], new Duplicati.Library.Main.Options(options), false, true);

                if (dbpath == null)
                {
                    outwriter.WriteLine("No local database found, please add --{0}", "dbpath");
                    return 100;
                }
                else
                    options["dbpath"] = dbpath;

            }

            if (args.Count == 0)
                args = new List<string>(new string[] { "file://unused", "report" });
            else if (args.Count == 1)
                args.Add("report");

            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller(args[0], options, console))
            {
                setup(i);
                i.CreateLogDatabase(args[1]);
            }

            outwriter.WriteLine("Completed!");
            outwriter.WriteLine();
            outwriter.WriteLine("Please examine the log table of the database to see that no filenames are accidentially left over.");
            outwriter.WriteLine("If you are concerned that your filenames may contain sensitive information,");
            outwriter.WriteLine(" do not attach the database to an issue!!!");
            outwriter.WriteLine();

            return 0;
        }

        public static int ListChanges(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            var fullresult = Duplicati.Library.Utility.Utility.ParseBoolOption(options, "full-result");

            if (args.Count < 1)
                return PrintWrongNumberOfArguments(outwriter, args, 1);

            // Support for not adding the --auth-username if possible
            string dbpath;
            options.TryGetValue("dbpath", out dbpath);
            if (string.IsNullOrEmpty(dbpath))
            {
                dbpath = Library.Main.CLIDatabaseLocator.GetDatabasePathForCLI(args[0], new Duplicati.Library.Main.Options(options), false, true);
                if (dbpath != null)
                    options["dbpath"] = dbpath;
            }

            // Don't ask for passphrase if we have a local db
            if (!string.IsNullOrEmpty(dbpath) && System.IO.File.Exists(dbpath) && !options.ContainsKey("no-encryption") && !Duplicati.Library.Utility.Utility.ParseBoolOption(options, "no-local-db"))
            {
                string passphrase;
                options.TryGetValue("passphrase", out passphrase);
                if (string.IsNullOrEmpty(passphrase))
                    options["no-encryption"] = "true";
            }

            Action<Duplicati.Library.Interface.IListChangesResults, IEnumerable<Tuple<Library.Interface.ListChangesChangeType, Library.Interface.ListChangesElementType, string>>> handler =
                (result, items) =>
                {
                    outwriter.WriteLine("Listing changes");
                    outwriter.WriteLine("  {0}: {1}", result.BaseVersionIndex, result.BaseVersionTimestamp);
                    outwriter.WriteLine("  {0}: {1}", result.CompareVersionIndex, result.CompareVersionTimestamp);
                    outwriter.WriteLine();

                    outwriter.WriteLine("Size of backup {0}: {1}", result.BaseVersionIndex, Library.Utility.Utility.FormatSizeString(result.PreviousSize));

                    if (items != null)
                    {
                        outwriter.WriteLine();

                        var added = result.ChangeDetails.Where(x => x.Item1 == Library.Interface.ListChangesChangeType.Added);
                        var deleted = result.ChangeDetails.Where(x => x.Item1 == Library.Interface.ListChangesChangeType.Deleted);
                        var modified = result.ChangeDetails.Where(x => x.Item1 == Library.Interface.ListChangesChangeType.Modified);

                        var count = added.Count();
                        if (count > 0)
                        {
                            var c = 0;
                            outwriter.WriteLine("  {0} added entries:", count);
                            foreach (var n in added)
                            {
                                c++;
                                outwriter.WriteLine("  + {0}", n.Item3);
                                if (c > 10 && count > 10 && !fullresult)
                                {
                                    outwriter.WriteLine("  ... and {0} more", count - c);
                                    break;
                                }
                            }
                            outwriter.WriteLine();
                        }
                        count = modified.Count();
                        if (count > 0)
                        {
                            var c = 0;
                            outwriter.WriteLine("  {0} modified entries:", count);
                            foreach (var n in modified)
                            {
                                c++;
                                outwriter.WriteLine("  ~ {0}", n.Item3);
                                if (c > 10 && count > 10 && !fullresult)
                                {
                                    outwriter.WriteLine("  ... and {0} more", count - c);
                                    break;
                                }
                            }
                            outwriter.WriteLine();
                        }
                        count = deleted.Count();
                        if (count > 0)
                        {
                            var c = 0;
                            outwriter.WriteLine("{0} deleted entries:", count);
                            foreach (var n in deleted)
                            {
                                c++;
                                outwriter.WriteLine("  - {0}", n.Item3);
                                if (c > 10 && count > 10 && !fullresult)
                                {
                                    outwriter.WriteLine("  ... and {0} more", count - c);
                                    break;
                                }
                            }
                            outwriter.WriteLine();
                        }

                        outwriter.WriteLine();
                    }


                    if (result.AddedFolders > 0)
                        outwriter.WriteLine("  Added folders:     {0}", result.AddedFolders);
                    if (result.AddedSymlinks > 0)
                        outwriter.WriteLine("  Added symlinks:    {0}", result.AddedSymlinks);
                    if (result.AddedFiles > 0)
                        outwriter.WriteLine("  Added files:       {0}", result.AddedFiles);
                    if (result.DeletedFolders > 0)
                        outwriter.WriteLine("  Deleted folders:   {0}", result.DeletedFolders);
                    if (result.DeletedSymlinks > 0)
                        outwriter.WriteLine("  Deleted symlinks:  {0}", result.DeletedSymlinks);
                    if (result.DeletedFiles > 0)
                        outwriter.WriteLine("  Deleted files:     {0}", result.DeletedFiles);
                    if (result.ModifiedFolders > 0)
                        outwriter.WriteLine("  Modified folders:  {0}", result.ModifiedFolders);
                    if (result.ModifiedSymlinks > 0)
                        outwriter.WriteLine("  Modified symlinka: {0}", result.ModifiedSymlinks);
                    if (result.ModifiedFiles > 0)
                        outwriter.WriteLine("  Modified files:    {0}", result.ModifiedFiles);

                    if (result.AddedFolders + result.AddedSymlinks + result.AddedFolders +
                        result.ModifiedFolders + result.ModifiedSymlinks + result.ModifiedFiles +
                        result.DeletedFolders + result.DeletedSymlinks + result.DeletedFiles == 0)
                        outwriter.WriteLine("  No changes found");

                    outwriter.WriteLine("Size of backup {0}: {1}", result.CompareVersionIndex, Library.Utility.Utility.FormatSizeString(result.CurrentSize));
                };

            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller(args[0], options, console))
            {
                setup(i);
                if (args.Count == 2)
                    i.ListChanges(null, args[1], null, filter, handler);
                else
                    i.ListChanges(args.Count > 1 ? args[1] : null, args.Count > 2 ? args[2] : null, null, filter, handler);
            }

            return 0;
        }

        public static int TestFilters(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args == null || args.Count < 1)
            {
                outwriter.WriteLine("No source paths given");
                return 200;
            }

            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller("dummy://", options, console))
            {
                setup(i);
                var result = i.TestFilter(args.ToArray(), filter);

                outwriter.WriteLine("Matched {0} files ({1})", result.FileCount, Duplicati.Library.Utility.Utility.FormatSizeString(result.FileSize));
            }

            return 0;
        }

        public static int SystemInfo(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args != null && args.Count != 0)
            {
                outwriter.WriteLine("Command takes no arguments");
                return 200;
            }

            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller("dummy://", options, console))
            {
                setup(i);
                foreach (var line in i.SystemInfo().Lines)
                    outwriter.WriteLine(line);
            }

            outwriter.WriteLine("Know locales: {0}", string.Join(", ", Library.Localization.LocalizationService.AllLocales));
            outwriter.WriteLine("Translated locales: {0}", string.Join(", ", Library.Localization.LocalizationService.SupportedCultures));

            return 0;
        }

        public static int PurgeFiles(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count < 1)
                return PrintWrongNumberOfArguments(outwriter, args, 1);

            var backend = args[0];
            var paths = args.Skip(1).ToArray();

            if (paths.Length > 0)
            {
                if (filter == null || filter.Empty)
                    filter = new Library.Utility.FilterExpression(paths);
                else
                {
                    outwriter.WriteLine("You cannot combine filters and paths on the commandline");
                    return 200;
                }
            }
            else if (filter == null || filter.Empty)
            {
                outwriter.WriteLine("You must provide either filename filters, or a list of paths to remove");
                return 200;
            }


            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller(backend, options, console))
            {
                setup(i);
                i.PurgeFiles(filter);
            }

            return 0;
        }

        public static int ListBrokenFiles(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(outwriter, args, 1);

            var previd = -1L;
            var outputcount = 0L;
            var fullresult = Duplicati.Library.Utility.Utility.ParseBoolOption(options, "full-result");

            using (var con = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller(args[0], options, con))
            {
                setup(i);
                i.ListBrokenFiles(filter, (id, time, count, path, size) =>
                {
                    if (previd != id)
                    {
                        previd = id;
                        outputcount = 0;
                        con.MessageEvent(string.Format("Fileset {0}\t: {1}\t({2} match(es))", id, time.ToLocalTime(), count));
                    }

                    con.MessageEvent(string.Format("\t{0} ({1})", path, Library.Utility.Utility.FormatSizeString(size)));
                    outputcount++;
                    if (outputcount >= 5 && !fullresult && count != outputcount)
                    {
                        con.MessageEvent(string.Format("\t ... and {0} more, (use --{1} to list all)", count - outputcount, "full-result"));
                        return false;
                    }

                    return true;

                });
            }

            return 0;
        }

        public static int PurgeBrokenFiles(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(outwriter, args, 1);

            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller(args[0], options, console))
            {
                setup(i);
                i.PurgeBrokenFiles(filter);
            }

            return 0;
        }

        public static int SendMail(TextWriter outwriter, Action<Duplicati.Library.Main.Controller> setup, List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args != null && args.Count != 0)
            {
                outwriter.WriteLine("Command takes no arguments");
                return 200;
            }

            using (var console = new ConsoleOutput(outwriter, options))
            using (var i = new Library.Main.Controller("dummy://", options, console))
            {
                setup(i);
                foreach (var l in i.SendMail().Lines)
                    outwriter.WriteLine(l);
            }

            return 0;
        }

        public static int Vacuum(
            TextWriter outwriter,
            Action<Duplicati.Library.Main.Controller> setup,
            List<string> args, Dictionary<string, string> options,
            Library.Utility.IFilter filter)
        {
            if (args.Count != 1)
                return PrintWrongNumberOfArguments(outwriter, args, 1);

            using (var console = new ConsoleOutput(outwriter, options))
            using (var controller = new Library.Main.Controller(args[0], options, console))
            {
                setup(controller);
                controller.Vacuum();
            }
            return 0;
        }
    }
}

