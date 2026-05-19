// Copyright (C) 2026, The Duplicati Team
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

using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;

#nullable enable

namespace AutoTune;

// TODO backup command tuning
// TODO backup with several versions
// TODO block size
// TODO dblock size
// TODO non-folder source
// TODO non-folder restore target
// TODO current RunCore assumes restore tuning. Should be moved into several commands for additional tunings (e.g. backup).

/// <summary>
/// Configuration for a single restore tuning run, specifying the concurrency parameters.
/// </summary>
public record ConfigRestore
{
    /// <summary>
    /// The restore channel buffer depth.
    /// </summary>
    public required int ChannelDepth { get; init; }

    /// <summary>
    /// Number of parallel file processing workers.
    /// </summary>
    public required int FileProcessors { get; init; }

    /// <summary>
    /// Number of parallel volume download workers.
    /// </summary>
    public required int VolumeDownloaders { get; init; }

    /// <summary>
    /// Number of parallel volume decrypt workers.
    /// </summary>
    public required int VolumeDecryptors { get; init; }

    /// <summary>
    /// Number of parallel volume decompress workers.
    /// </summary>
    public required int VolumeDecompressors { get; init; }

    public override string ToString()
    {
        return $"FileProcessors: {FileProcessors}, VolumeDecompressors: {VolumeDecompressors}, VolumeDecryptors: {VolumeDecryptors}, VolumeDownloaders: {VolumeDownloaders}";
    }
}

/// <summary>
/// Holds the measured read, execute, and write timing samples (in milliseconds).
/// </summary>
public record ResultEntry
{
    /// <summary>
    /// Measured read-phase durations (ms).
    /// </summary>
    public List<int> Read { get; init; } = [];

    /// <summary>
    /// Measured execute-phase durations (ms).
    /// </summary>
    public List<int> Execute { get; init; } = [];

    /// <summary>
    /// Measured write-phase durations (ms).
    /// </summary>
    public List<int> Write { get; init; } = [];
}

/// <summary>
/// Aggregated restore timing results broken down by pipeline stage.
/// </summary>
public record ResultsRestore
{
    /// <summary>
    /// Average total wall-clock time for the restore operation(s) in milliseconds.
    /// </summary>
    public required int Total { get; init; }

    /// <summary>
    /// Execution-time samples from the FileProcessor.
    /// </summary>
    public required ResultEntry FileProcessor { get; init; }

    /// <summary>
    /// Execution-time samples from the VolumeDownloader.
    /// </summary>
    public required ResultEntry VolumeDownloader { get; init; }

    /// <summary>
    /// Execution-time samples from the VolumeDecryptor.
    /// </summary>
    public required ResultEntry VolumeDecryptor { get; init; }

    /// <summary>
    /// Execution-time samples from the VolumeDecompressor.
    /// </summary>
    public required ResultEntry VolumeDecompressor { get; init; }
}

/// <summary>
/// Captures Duplicati profiling log lines and timing messages during a restore operation
/// so they can be parsed into structured <see cref="ResultsRestore"/> data.
/// </summary>
public class ProfilingCaptureSink : IMessageSink, IDisposable
{
    private readonly List<LogEntry> _log_lines = [];
    private readonly List<LogEntry> _network_wait = [];

    public void Dispose()
    {
    }

    void IMessageSink.BackendEvent(BackendActionType action, BackendEventType type, string path, long size)
    {
        // Do nothing
    }

    void IMessageSink.SetBackendProgress(IBackendProgress progress)
    {
        // Do nothing
    }

    void IMessageSink.SetOperationProgress(IOperationProgress progress)
    {
        // Do nothing
    }

    /// <summary>
    /// Parses internal-timings dictionary from the FileProcessor and appends
    /// the read, execute, and write durations to <paramref name="results"/>.
    /// </summary>
    /// <param name="results">The aggregate results object to append to.</param>
    /// <param name="timings">Parsed key-value timing pairs from one log line.</param>
    /// <returns>Always 0.</returns>
    private static int ParseFileProcessor(ResultsRestore results, Dictionary<string, int> timings)
    {
        results.FileProcessor.Read.Add(
            timings["File"] + timings["Resp"]
        );
        results.FileProcessor.Execute.Add(
            timings["Block"] + timings["Meta"] + timings["Work"] + timings["VerifyTarget"] + timings["Retarget"] + timings["VerifyLocal"] + timings["EmptyFile"] + timings["Hash"] + timings["Read"] + timings["Write"] + timings["Results"] + timings["Meta Work"]
        );
        results.FileProcessor.Write.Add(
            timings["Req"] + timings["NotifyLocal"]
        );
        return 0;
    }

    /// <summary>
    /// Parses internal-timings dictionary from the VolumeDecompressor and
    /// appends the read, execute, and write durations to <paramref name="results"/>.
    /// </summary>
    /// <param name="results">The aggregate results object to append to.</param>
    /// <param name="timings">Parsed key-value timing pairs from one log line.</param>
    /// <returns>Always 0.</returns>
    private static int ParseVolumeDecompressor(ResultsRestore results, Dictionary<string, int> timings)
    {
        results.VolumeDecompressor.Read.Add(timings["Read"]);
        results.VolumeDecompressor.Execute.Add(
            timings["Decompress allocate"] + timings["Decompress instantiate"] +
            timings["Decompress lock"] + timings["Decompress read"] +
            timings["Verify"]
        );
        results.VolumeDecompressor.Write.Add(timings["Write"]);
        return 0;
    }

    /// <summary>
    /// Parses internal-timings dictionary from the VolumeDecryptor and appends
    /// the read, execute, and write durations to <paramref name="results"/>.
    /// </summary>
    /// <param name="results">The aggregate results object to append to.</param>
    /// <param name="timings">Parsed key-value timing pairs from one log line.</param>
    /// <returns>Always 0.</returns>
    private static int ParseVolumeDecryptor(ResultsRestore results, Dictionary<string, int> timings)
    {
        results.VolumeDecryptor.Read.Add(timings["Read"]);
        results.VolumeDecryptor.Execute.Add(
            timings["Decrypt"] + timings["BlockVolumeReader"] +
            timings["VolumeWrapper"]
        );
        results.VolumeDecryptor.Write.Add(timings["Write"]);
        return 0;
    }

    /// <summary>
    /// Parses internal-timings dictionary from the VolumeDownloader and appends
    /// the read, execute, and write durations to <paramref name="results"/>.
    /// </summary>
    /// <param name="results">The aggregate results object to append to.</param>
    /// <param name="timings">Parsed key-value timing pairs from one log line.</param>
    /// <returns>Always 0.</returns>
    private static int ParseVolumeDownloader(ResultsRestore results, Dictionary<string, int> timings)
    {
        results.VolumeDownloader.Read.Add(timings["Read"]);
        results.VolumeDownloader.Execute.Add(timings["Wait"]);
        results.VolumeDownloader.Write.Add(timings["Write"]);
        return 0;
    }

    /// <summary>
    /// Parses all captured log lines and network-wait entries into a <see cref="ResultsRestore"/> record.
    /// </summary>
    /// <returns>A fully populated restore timing result.</returns>
    /// <exception cref="InvalidDataException">Thrown when no log lines or no network-wait entries have been captured.</exception>
    public async Task<ResultsRestore> ParseLines()
    {
        if (_log_lines.Count <= 0)
            throw new InvalidDataException("No log lines has been captured.");
        if (_network_wait.Count <= 0)
            throw new InvalidDataException("Total network execution time hasn't been captured.");

        List<int> totals = [];
        foreach (var line in _network_wait)
            if (line.Message.StartsWith("{0} took"))
                totals.Add((int)TimeSpan.ParseExact(line.ToString().Split(' ')[^1], @"d\:hh\:mm\:ss\.fff", null).TotalMilliseconds);

        ResultsRestore parsed = Program.EmptyResultsRestore((int)totals.Average());

        foreach (var line in _log_lines)
        {
            var proc = line.Tag.Split(".")[^1];
            var timings = line.Message.Split(",")
                .Select(x => x.Split(":").Select(y => y.Trim()).ToArray())
                .ToDictionary(x => x[0], x => int.Parse(x[1].TrimEnd("ms")));
            _ = proc switch
            {
                "FileProcessor" => ParseFileProcessor(parsed, timings),
                "VolumeDecompressor" => ParseVolumeDecompressor(parsed, timings),
                "VolumeDecryptor" => ParseVolumeDecryptor(parsed, timings),
                "VolumeDownloader" => ParseVolumeDownloader(parsed, timings),
                _ => 0  // Ignore the rest
            };
        }

        return parsed;
    }

    /// <summary>
    /// Clears all captured log lines and network-wait entries, readying the sink for a new measurement round.
    /// </summary>
    public void Reset()
    {
        _log_lines.Clear();
        _network_wait.Clear();
    }

    /// <summary>
    /// Called by the Duplicati logging infrastructure for each log entry.
    /// InternalTimings lines are retained for profiling analysis; RestoreNetworkWait lines
    /// are retained to compute total wall-clock time.
    /// </summary>
    /// <param name="entry">The log entry written by Duplicati.</param>
    void ILogDestination.WriteMessage(LogEntry entry)
    {
        if (entry.Id == "InternalTimings")
            _log_lines.Add(entry);
        else if (entry.Id == "RestoreNetworkWait")
            _network_wait.Add(entry);
    }
}

public class Program
{
    /// <summary>
    /// The Duplicati library default restore configuration, used both as a baseline and as the
    /// starting point when --default-settings is specified.
    /// </summary>
    private static readonly ConfigRestore DefaultConfigRestore = new()
    {
        ChannelDepth = Duplicati.Library.Main.Options.DEFAULT_RESTORE_CHANNEL_BUFFER_SIZE,
        FileProcessors = Duplicati.Library.Main.Options.DEFAULT_RESTORE_FILE_PROCESSORS,
        VolumeDownloaders = Duplicati.Library.Main.Options.DEFAULT_RESTORE_VOLUME_DOWNLOADERS,
        VolumeDecryptors = Duplicati.Library.Main.Options.DEFAULT_RESTORE_VOLUME_DECRYPTORS,
        VolumeDecompressors = Duplicati.Library.Main.Options.DEFAULT_RESTORE_VOLUME_DECOMPRESSORS,
    };

    /// <summary>
    /// The log tag for this tool.
    /// </summary>
    private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType(typeof(Program));

    /// <summary>
    /// Entry point for the AutoTune tool. Parses command-line options and invokes <see cref="RunCore"/>.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>A process exit code: 0 on success, -1 on error.</returns>
    public static async Task<int> Main(string[] args)
    {
        var root_cmd = new RootCommand("Auto tuning of the Duplicati concurrency parameters. Warning, this program will be accessing the source, destination, and restoretarget a lot, potentially degrading them.")
        {
            new Option<List<string>>(aliases: ["--backend-options"], description: "Duplicati options to pass to the backend during backup. Each option is a key-value pair spearated by an equals sign, e.g. --backend-options key1=value1 key2=value2. Default is an empty list.", getDefaultValue: () => [])
            {
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true
            },
            new Option<string?>(aliases: ["--destination"], description: "Destination to store the test backup. The destination should be empty (as required by Duplicati). The data will be deleted again after the tuning process. If no argument is specified, then a temporary folder (as optionally specified with the temp_folder argument) will be used.", getDefaultValue: () => null),
            new Option<bool>(aliases: ["--dont-revisit-parameters"], description: "During tuning, once a new 'better' configuration has been found, all of the tunable parameters become candidates again. Setting this option will disable already visited candidate parameters. This will make the tuning converge faster, but may not find an optimal configuration.", getDefaultValue: () => false),
            new Option<bool>(aliases: ["--exponential-steps"], description: "If specified, the steps taken for next candidate run is to multiply by 2 instead of plus 1. This will make the tuning converge faster, but may not find an optimal configuration.", getDefaultValue: () => false),
            new Option<string?>(aliases: ["--restoretarget"], description: "Target folder to restore a backup to. The folder should be empty beforehand, as it needs to be emptied during measurements. If no argument is specified, then a temporary folder (as optionally specified with the temp_folder argument) will be used.", getDefaultValue: () => null),
            new Option<bool>(aliases: ["--default-settings"], description: "Start tuning from the Duplicati default settings instead of a starting step of 1. Ignored if --starting-steps is specified.", getDefaultValue: () => false),
            new Option<int>(aliases: ["--runs"], description: "Number of runs to measure. The mean is reported.", getDefaultValue: () => 3),
            new Option<int[]>(aliases: ["--starting-steps"], description: "The starting step value(s) for the tunable parameters, 1 or 4 integers. If one value is specified, the same value is used for all parameters. If four values are specified, they are applied individually for file-processors, volume-decompressors, volume-decryptors, and volume-downloaders (in that order). If not specified, the starting step is 1 for all parameters. Cannot be used together with --default-settings.", getDefaultValue: () => [])
            {
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true,
            },
            new Option<string?>(aliases: ["--source-folder"], description: "Source folder to make a backup of. If the folder is empty, then some test data will be generated. If no argument is specified, then a temporary folder (as optionally specified with the temp_folder argument) will be used.", getDefaultValue: () => null),
            new Option<string?>(aliases: ["--temp-folder"], description: "Path to where the temporary files should be created. If no argument is specified, then the system default (e.g. /tmp or %TEMP%) will be used.", getDefaultValue: () => null),
            new Option<long>(aliases: ["--testdata-max-file-size"], description: "If no source folder has been specified, this option tunes the maximum size (in bytes) a generated file may have. Default is 1 MB.", getDefaultValue: () => 1024 * 1024),
            new Option<long>(aliases: ["--testdata-max-total-size"], description: "If no source folder has been specified, this option tunes the maximum size (in bytes) the generated files collectively may take up. Default is 512 MB.", getDefaultValue: () => 512 * 1024 * 1024),
            new Option<long>(aliases: ["--testdata-num-files"], description: "If no source folder has been specified, this option tunes how many files are generated as test data.", getDefaultValue: () => 10000),
            new Option<int>(aliases: ["--verbose"], description: "Verbosity level: 0 disables output, 1 prints full progress information during tuning runs. Higher levels reserved for future debug printing.", getDefaultValue: () => 1),
            new Option<int>(aliases: ["--warmup"], description: "Amount of warmup runs to perform before measuring.", getDefaultValue: () => 1),
        };

        root_cmd.Handler = CommandHandler.Create(async (ConfigAutoTune cfg, CancellationToken token) =>
        {
            // Warn on conflicting options
            if (cfg.StartingSteps is { Length: > 0 } && cfg.UseDefaultSettings)
            {
                Console.WriteLine("Warning: --starting-steps and --default-settings are both set. Ignoring --starting-steps.");
            }

            // Check if a specific temp folder has been specified.
            bool tempfolder_created = cfg.TempFolder is null;
            var tempfolder = cfg.TempFolder ?? Path.Combine(Path.GetTempPath(), $"duplicati_autotune_{new Guid()}");

            // Check if any of the other paths are unspecified and should thus use the temp folder.
            bool any_in_tmp = cfg.SourceFolder is null || cfg.Destination is null || cfg.RestoreTarget is null;
            bool source_created = cfg.SourceFolder is null;
            bool destination_created = cfg.Destination is null;
            bool restoretarget_created = cfg.RestoreTarget is null;
            var source = cfg.SourceFolder ?? Path.Combine(tempfolder, "source");
            var destination = cfg.Destination ?? Path.Combine(tempfolder, "backup");
            var restoretarget = cfg.RestoreTarget ?? Path.Combine(tempfolder, "restored");
            if (any_in_tmp)
                tempfolder = Path.Combine(tempfolder, "temp");

            // Check whether the paths exist, and if so, whether they already contain data.
            static void ensure_dir(string label, string path, int verbose, bool may_contain_data)
            {
                if (verbose > 0)
                    Console.WriteLine($"  [init] {path}");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                else if (!may_contain_data && !Directory.EnumerateFileSystemEntries(path).Any())
                    throw new InvalidDataException($"{label} directory '{path}' may not contain any data.");

            }
            ensure_dir("Source", source, cfg.Verbose, true);
            ensure_dir("Destination", destination, cfg.Verbose, false);
            ensure_dir("Temporary folder", tempfolder, cfg.Verbose, true);
            ensure_dir("Restore target", restoretarget, cfg.Verbose, false);

            if (!Directory.EnumerateFiles(source).Any())
                await GenerateData(source, cfg.TestdataMaxFileSize, cfg.TestdataMaxTotalSize, cfg.TestdataNumFiles, cfg.Verbose);

            var opts = ParseOptions(cfg.BackendOptions);

            var rc = await RunCore(source, destination, restoretarget, tempfolder, cfg, opts);

            // Cleanup - only delete directories that were created by this tool
            if (source_created)
                Directory.Delete(source, true);
            if (destination_created)
                Directory.Delete(destination, true);
            if (restoretarget_created)
                Directory.Delete(restoretarget, true);
            if (tempfolder_created)
                Directory.Delete(tempfolder, true);

            return rc;
        });

        return await root_cmd.InvokeAsync(args).ConfigureAwait(false);
    }

    /// <summary>
    /// Core tuning loop for restore performance. Measures a baseline, then iteratively adjusts
    /// concurrency parameters to find the fastest configuration.
    /// </summary>
    /// <param name="source">Path to the source folder to back up.</param>
    /// <param name="destination">Backup destination URI (e.g. file:///tmp/backup).</param>
    /// <param name="restoretarget">Path where restores are performed during measurement.</param>
    /// <param name="tempfolder">Path for temporary files such as the database.</param>
    /// <param name="cfg">User-supplied tuning options.</param>
    /// <param name="options">Parsed Duplicati key-value options passed to the controller.</param>
    /// <returns>A process exit code: 0 on success, -1 on error.</returns>
    internal static async Task<int> RunCore(string source, string destination, string restoretarget, string tempfolder, ConfigAutoTune cfg, Dictionary<string, string?> options)
    {
        if (!destination.Contains("://"))
            destination = $"file://{destination}";
        var database = Path.Combine(tempfolder, "db.sqlite");
        options["passphrase"] = "1234";
        options["dbpath"] = database;
        options["restore-path"] = restoretarget;
        options["allow-full-removal"] = "true";
        options["tempdir"] = tempfolder;
        options["version"] = "0";
        options["internal-profiling"] = "true";
        options["console-log-level"] = "Profiling";

        var sink = new ProfilingCaptureSink();

        // Setup
        if (cfg.Verbose > 0)
            Console.WriteLine("Creating initial backup...");
        using (var c = new Controller(destination, options, sink))
            try
            {
                var res = c.Backup([source]);
                if (cfg.Verbose > 0)
                    Console.WriteLine($"Backup ran in {res.Duration}");
            }
            catch (Exception e)
            {
                await Console.Error.WriteLineAsync($"Error during backup: {e.Message}.");
                await Console.Error.WriteLineAsync($"  Stack trace: {e.StackTrace}");
                return -1;
            }

        if (cfg.Verbose > 0)
            Console.WriteLine("Making the baseline measurement based of the Duplicati default concurrency parameters...");
        using (var c = new Controller(destination, options, sink))
            try
            {
                c.Restore(["*"]);
            }
            catch (Exception e)
            {
                await Console.Error.WriteLineAsync($"Error during restore: {e.Message}.");
                await Console.Error.WriteLineAsync($"  Stack trace: {e.StackTrace}");
                return -1;
            }
        var profile_default_baseline = await sink.ParseLines();

        if (cfg.Verbose > 0)
        {
            Console.WriteLine("----==== Default configuration baseline ====----");
            Console.WriteLine($"  Default config: {DefaultConfigRestore}");
            Console.WriteLine($"  Baseline time: {profile_default_baseline.Total} ms");
        }

        var config_current = cfg.UseDefaultSettings
            ? DefaultConfigRestore
             : cfg.StartingSteps is { Length: >= 1 } steps
                 ? steps.Length switch
                 {
                     1 => DefaultConfigRestore with
                     {
                         FileProcessors = steps[0],
                         VolumeDecompressors = steps[0],
                         VolumeDecryptors = steps[0],
                         VolumeDownloaders = steps[0],
                     },
                     4 => DefaultConfigRestore with
                     {
                         FileProcessors = steps[0],
                         VolumeDecompressors = steps[1],
                         VolumeDecryptors = steps[2],
                         VolumeDownloaders = steps[3],
                     },
                     _ => throw new ArgumentException($"--starting-steps requires 1 or 4 values, got {steps.Length}."),
                 }
                : DefaultConfigRestore with
                {
                    FileProcessors = 1,
                    VolumeDecompressors = 1,
                    VolumeDecryptors = 1,
                    VolumeDownloaders = 1
                };

        var profile_best = EmptyResultsRestore(int.MaxValue);
        var config_best = config_current;
        List<int> excludes = [];
        int last_idx = -1;
        string last_label = "";
        int round = 0;

        Func<int, int> step_method = cfg.ExponentialSteps ? x => x * 2 : x => x + 1;

        while (true)
        {
            round++;
            SetOptions(options, config_current);
            sink.Reset();
            using var c = new Controller(destination, options, sink);

            for (int i = 0; i < cfg.Warmup; i++)
            {
                Directory.Delete(restoretarget, true);
                Directory.CreateDirectory(restoretarget);
                c.Restore(["*"]);
            }

            sink.Reset();

            for (int i = 0; i < cfg.Runs; i++)
            {
                Directory.Delete(restoretarget, true);
                Directory.CreateDirectory(restoretarget);
                c.Restore(["*"]);
            }

            var profile_current = await sink.ParseLines();

            if (cfg.Verbose > 0)
            {
                Console.WriteLine($"----- Round {round} -----");
                Console.WriteLine($"  Config: {config_current}");
                var best_str = round > 1 ? $" (best: {profile_best.Total,6} ms)" : "";
                Console.WriteLine($"  Time:   {profile_current.Total,6} ms{best_str}");
            }

            if (profile_current.Total < profile_best.Total)
            {
                if (cfg.Verbose > 0)
                    if (round > 1)
                        Console.WriteLine($"  -> New best! Improvement: {profile_best.Total - profile_current.Total:D} ms saved");
                profile_best = profile_current;
                config_best = config_current;
                if (!cfg.DontRevisitParameters)
                    excludes.Clear();
            }
            else
            {
                excludes.Add(last_idx);
                if (cfg.Verbose > 0)
                    Console.WriteLine($"  -> Excluded stage {last_label} ({excludes.Count}/4 parameters exhausted)");
                if (excludes.Count == 4)
                    break;
            }

            var candidates = new List<(int Stage, string Label, int Time)>
            {
                (0, "FProc", (int)profile_current.FileProcessor.Execute.Average()),
                (1, "VDeco", (int)profile_current.VolumeDecompressor.Execute.Average()),
                (2, "VDecr", (int)profile_current.VolumeDecryptor.Execute.Average()),
                (3, "VDown", (int)profile_current.VolumeDownloader.Execute.Average()),
            };

            if (cfg.Verbose > 0)
                Console.WriteLine($"  Timings (avg ms): {string.Join(" ", candidates.Select(x => $"[{x.Label}] {x.Time,6}"))}");

            var (idx, label, _) = candidates.Where(x => !excludes.Contains(x.Stage)).MaxBy(x => x.Time);
            if (cfg.Verbose > 0) Console.WriteLine($"  -> Increasing stage {label} (most saturated: {candidates[idx].Time} ms avg execute time)");
            config_current = idx switch
            {
                0 => config_best with { FileProcessors = step_method(config_best.FileProcessors) },
                1 => config_best with { VolumeDecompressors = step_method(config_best.VolumeDecompressors) },
                2 => config_best with { VolumeDecryptors = step_method(config_best.VolumeDecryptors) },
                3 => config_best with { VolumeDownloaders = step_method(config_best.VolumeDownloaders) },
                _ => throw new Exception($"Incorrect process idx specified: {idx}"),
            };
            last_idx = idx;
            last_label = label;
        }

        Console.WriteLine("----==== Tuning complete ====----");
        Console.WriteLine($" Best configuration: {config_best}");
        Console.WriteLine($" Best time:         {profile_best.Total} ms");
        Console.WriteLine($" Default time:      {profile_default_baseline.Total} ms");
        Console.WriteLine($" Speedup:           {(double)profile_default_baseline.Total / profile_best.Total:F2}x");

        // Cleanup
        using (var c = new Controller(destination, options, sink))
            try
            {
                c.Delete();
            }
            catch (Exception e)
            {
                await Console.Error.WriteLineAsync($"Error during cleanup: {e.Message}.");
                await Console.Error.WriteLineAsync($"  Stack trace: {e.StackTrace}");
                return -1;
            }

        try
        {
            File.Delete(database);
        }
        catch (FileNotFoundException) { } // Ignore

        return 0;
    }

    // Helper methods

    /// <summary>
    /// Creates an empty <see cref="ResultsRestore"/> record with the given total time and no per-stage samples.
    /// </summary>
    /// <param name="total">Initial total time value (e.g. <see cref="int.MaxValue"/> for an unmeasured baseline).</param>
    /// <returns>A new empty <see cref="ResultsRestore"/> instance.</returns>
    public static ResultsRestore EmptyResultsRestore(int total)
    {
        return new()
        {
            Total = total,
            FileProcessor = new(),
            VolumeDownloader = new(),
            VolumeDecryptor = new(),
            VolumeDecompressor = new(),
        };
    }

    /// <summary>
    /// Generates synthetic test data by invoking the Duplicati TestDataGenerator tool.
    /// </summary>
    /// <param name="path">Output directory for the generated files.</param>
    /// <param name="max_file_size">Maximum size of an individual generated file (bytes).</param>
    /// <param name="max_total_size">Maximum cumulative size of all generated files (bytes).</param>
    /// <param name="file_count">Number of files to generate.</param>
    /// <param name="verbose">When true, generator output is forwarded to the console.</param>
    /// <exception cref="Exception">Thrown when the generator returns a non-zero exit code.</exception>
    private static async Task GenerateData(string path, long max_file_size, long max_total_size, long file_count, int verbose)
    {
        var cmd = TestDataGenerator.Commands.Create.CreateCommand();
        long sparse_factor = 30;
        var args = $"\"{path}\" --max-file-size {max_file_size} --max-total-size {max_total_size} --file-count {file_count} --sparse-factor {sparse_factor}";

        int return_code;
        if (verbose > 0)
        {
            return_code = await cmd.InvokeAsync(args);
        }
        else
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);

            return_code = await cmd.InvokeAsync(args);

            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        if (return_code != 0)
            throw new Exception("Test data generation failed.");
    }

    /// <summary>
    /// Writes the restore concurrency settings from <paramref name="config"/> into the Duplicati options dictionary.
    /// </summary>
    /// <param name="options">The mutable options dictionary for the Duplicati controller.</param>
    /// <param name="config">The restored concurrency configuration to apply.</param>
    private static void SetOptions(Dictionary<string, string?> options, ConfigRestore config)
    {
        options["restore-file-processors"] = config.FileProcessors.ToString();
        options["restore-volume-decompressors"] = config.VolumeDecompressors.ToString();
        options["restore-volume-decryptors"] = config.VolumeDecryptors.ToString();
        options["restore-volume-downloaders"] = config.VolumeDownloaders.ToString();
    }

    // TODO taken from the remote synchronization tool. Consolidate into shared library.
    /// <summary>
    /// Parses the options from a list of strings.
    /// Each option should be in the format "key=value". If the value contains spaces,
    /// it should be enclosed in quotes, e.g. "key=\"value with spaces\"".
    /// </summary>
    /// <param name="options">The list of string options to parse</param>
    /// <returns>A dictionary with the parsed options, where the key is the option name and the value is the option value.</returns>
    /// <exception cref="ArgumentException">If an option was not parsed correctly.</exception>
    private static Dictionary<string, string?> ParseOptions(IEnumerable<string> options)
    {
        var result = options
            .Select(x => x.Split('='))
            .ToDictionary(x => x[0], x => (string?)string.Join("=", x.Skip(1)));

        // Double check that the options are valid by reconstructing them from the dictionary
        foreach (var opt in result.Select(x => $"{x.Key}={x.Value}"))
        {
            if (!options.Contains(opt))
            {
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", null,
                    "The source option '{0}' is not valid. Please check the syntax.", opt);
                throw new ArgumentException($"The source option '{opt}' has not been parsed correctly.");
            }
        }

        return result;
    }
}