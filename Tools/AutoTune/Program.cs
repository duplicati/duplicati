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

public record ConfigRestore
{
    public required int ChannelDepth { get; init; }
    public required int FileProcessors { get; init; }
    public required int VolumeDownloaders { get; init; }
    public required int VolumeDecryptors { get; init; }
    public required int VolumeDecompressors { get; init; }

    public override string ToString()
    {
        return $"FileProcessors: {FileProcessors}, VolumeDecompressors: {VolumeDecompressors}, VolumeDecryptors: {VolumeDecryptors}, VolumeDownloaders: {VolumeDownloaders}";
    }
}

public record ResultEntry
{
    public List<int> Read { get; init; } = [];
    public List<int> Execute { get; init; } = [];
    public List<int> Write { get; init; } = [];
}

public record ResultsRestore
{
    /// <summary>
    /// Total time for the restore operation in milliseconds.
    /// </summary>
    public required int Total { get; init; }
    public required ResultEntry FileProcessor { get; init; }
    //public required ResultEntry BlockHandler { get; init; }
    //public required ResultEntry VolumeManager { get; init; }
    public required ResultEntry VolumeDownloader { get; init; }
    public required ResultEntry VolumeDecryptor { get; init; }
    public required ResultEntry VolumeDecompressor { get; init; }
    //public required ResultEntry VolumeConsumer { get; init; }
}

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

    private static int ParseVolumeDownloader(ResultsRestore results, Dictionary<string, int> timings)
    {
        results.VolumeDownloader.Read.Add(timings["Read"]);
        results.VolumeDownloader.Execute.Add(timings["Wait"]);
        results.VolumeDownloader.Write.Add(timings["Write"]);
        return 0;
    }

    public async Task<ResultsRestore> ParseLines()
    {
        if (_log_lines.Count <= 0)
            throw new InvalidDataException("No log lines has been captured.");
        if (_network_wait.Count <= 0)
            throw new InvalidDataException("Total network execution time hasn't been captured.");

        int total = -1;
        foreach (var line in _network_wait)
            if (line.Message.StartsWith("{0} took"))
                total = (int)TimeSpan.ParseExact(line.ToString().Split(' ')[^1], @"d\:hh\:mm\:ss\.fff", null).TotalMilliseconds;

        ResultsRestore parsed = Program.EmptyResultsRestore(total);

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

    public void Reset()
    {
        _log_lines.Clear();
        _network_wait.Clear();
    }

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

    public static async Task<int> Main(string[] args)
    {
        var root_cmd = new RootCommand("Auto tuning of the Duplicati concurrency parameters.")
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
            new Option<int>(aliases: ["--runs"], description: "Number of runs to measure. The mean is reported.", getDefaultValue: () => 3),
            new Option<string?>(aliases: ["--source-folder"], description: "Source folder to make a backup of. If the folder is empty, then some test data will be generated. If no argument is specified, then a temporary folder (as optionally specified with the temp_folder argument) will be used.", getDefaultValue: () => null),
            new Option<string?>(aliases: ["--temp-folder"], description: "Path to where the temporary files should be created. If no argument is specified, then the system default (e.g. /tmp or %TEMP%) will be used.", getDefaultValue: () => null),
            new Option<long>(aliases: ["--testdata-max-file-size"], description: "If no source folder has been specified, this option tunes the maximum size (in bytes) a generated file may have.", getDefaultValue: () => 1024 * 1024),
            new Option<long>(aliases: ["--testdata-max-total-size"], description: "If no source folder has been specified, this option tunes the maximum size (in bytes) the generated files collectively may take up.", getDefaultValue: () => 512 * 1024 * 1024),
            new Option<long>(aliases: ["--testdata-num-files"], description: "If no source folder has been specified, this option tunes how many files are generated as test data.", getDefaultValue: () => 10000),
            new Option<bool>(aliases: ["--verbose"], description: "Toggles whether to print progress information during tuning runs. By default, only the final result is printed.", getDefaultValue: () => false),
            new Option<int>(aliases: ["--warmup"], description: "Amount of warmup runs to perform before measuring.", getDefaultValue: () => 1),
        };

        root_cmd.Handler = CommandHandler.Create(async (ConfigAutoTune cfg, CancellationToken token) =>
        {
            // Check if a specific temp folder has been specified.
            var tempfolder = cfg.TempFolder ?? Path.Combine(Path.GetTempPath(), $"duplicati_autotune_{new Guid()}");

            // Check if any of the other paths are unspecified and should thus use the temp folder.
            bool any_in_tmp = cfg.SourceFolder is null || cfg.Destination is null || cfg.RestoreTarget is null;
            var source = cfg.SourceFolder ?? Path.Combine(tempfolder, "source");
            var destination = cfg.Destination ?? Path.Combine(tempfolder, "backup");
            var restoretarget = cfg.RestoreTarget ?? Path.Combine(tempfolder, "restored");
            if (any_in_tmp)
                tempfolder = Path.Combine(tempfolder, "temp");

            // Check whether the paths exist, and if so, whether they already contain data.
            static void ensure_dir(string path, bool verbose)
            {
                if (verbose)
                    Console.WriteLine($"  [init] {path}");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
            ensure_dir(source, cfg.Verbose);
            ensure_dir(destination, cfg.Verbose);
            ensure_dir(tempfolder, cfg.Verbose);
            ensure_dir(restoretarget, cfg.Verbose);

            if (!Directory.EnumerateFiles(source).Any())
                await GenerateData(source, cfg.TestdataMaxFileSize, cfg.TestdataMaxTotalSize, cfg.TestdataNumFiles, cfg.Verbose);

            var opts = ParseOptions(cfg.BackendOptions);

            var rc = await RunCore(source, destination, restoretarget, tempfolder, cfg, opts);

            // Cleanup
            // TODO only if the directories were created
            Directory.Delete(source, true);
            Directory.Delete(destination, true);
            Directory.Delete(tempfolder, true);
            Directory.Delete(restoretarget, true);

            return rc;
        });

        return await root_cmd.InvokeAsync(args).ConfigureAwait(false);
    }

    // TODO assumes restore tuning. Should be moved into several commands for additional tunings (e.g. backup).
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
        using (var c = new Controller(destination, options, sink))
            try
            {
                c.Backup([source]);
            }
            catch (Exception e)
            {
                await Console.Error.WriteLineAsync($"Error during backup: {e.Message}.");
                await Console.Error.WriteLineAsync($"  Stack trace: {e.StackTrace}");
                return -1;
            }

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

        if (cfg.Verbose)
        {
            Console.WriteLine("----==== Default configuration baseline ====----");
            Console.WriteLine($"  Default config: {DefaultConfigRestore}");
            Console.WriteLine($"  Baseline time: {profile_default_baseline.Total} ms");
        }

        var config_current = DefaultConfigRestore with
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

        Func<int, int> step_method = cfg.ExponentialSteps ? x => x * 2 : x => x + 1;

        while (true)
        {
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

            if (cfg.Verbose)
            {
                Console.WriteLine($"----- Round {(last_idx >= 0 ? '→' : '1')} -----");
                Console.WriteLine($"  Config: {config_current}");
                Console.WriteLine($"  Time:   {profile_current.Total,6} ms  (best: {profile_best.Total,6} ms)");
            }

            if (profile_current.Total < profile_best.Total)
            {
                if (cfg.Verbose)
                    Console.WriteLine($"  -> New best! Improvement: {profile_best.Total - profile_current.Total:D} ms saved");
                profile_best = profile_current;
                config_best = config_current;
                if (!cfg.DontRevisitParameters)
                    excludes.Clear();
            }
            else
            {
                excludes.Add(last_idx);
                if (cfg.Verbose)
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

            if (cfg.Verbose)
                Console.WriteLine($"  Timings (avg ms): {string.Join(" ", candidates.Select(x => $"[{x.Label}] {x.Time,6}"))}");

            var (idx, label, _) = candidates.Where(x => !excludes.Contains(x.Stage)).MaxBy(x => x.Time);
            if (cfg.Verbose) Console.WriteLine($"  -> Increasing stage {label} (most saturated: {candidates[idx].Time} ms avg execute time)");
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

    public static ResultsRestore EmptyResultsRestore(int total)
    {
        return new()
        {
            Total = total,
            FileProcessor = new(),
            //BlockHandler = new(),
            //VolumeManager = new(),
            VolumeDownloader = new(),
            VolumeDecryptor = new(),
            VolumeDecompressor = new(),
            //VolumeConsumer = new(),
        };
    }

    private static async Task GenerateData(string path, long max_file_size, long max_total_size, long file_count, bool verbose)
    {
        var cmd = TestDataGenerator.Commands.Create.CreateCommand();
        long sparse_factor = 30;
        var args = $"\"{path}\" --max-file-size {max_file_size} --max-total-size {max_total_size} --file-count {file_count} --sparse-factor {sparse_factor}";

        if (verbose)
        {
            await cmd.InvokeAsync(args);
        }
        else
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);

            await cmd.InvokeAsync(args);

            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

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