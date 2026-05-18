using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;

#nullable enable

namespace AutoTune;

// TODO backup command tuning
// TODO backup with several versions
// TODO block size
// TODO dblock size
// TODO data sizes
// TODO non-folder source
// TODO non-folder restore target

public record ConfigRestore
{
    public required int ChannelDepth { get; init; }
    public required int FileProcessors { get; init; }
    public required int VolumeDownloaders { get; init; }
    public required int VolumeDecryptors { get; init; }
    public required int VolumeDecompressors { get; init; }
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
        var arg_src = new Option<string?>(aliases: ["--source-folder"], description: "Source folder to make a backup of. If the folder is empty, then some test data will be generated. If no argument is specified, then a temporary folder (as optionally specified with the temp_folder argument) will be used.", getDefaultValue: () => null);
        var arg_dst = new Option<string?>(aliases: ["--destination"], description: "Destination to store the test backup. The destination should be empty (as required by Duplicati). The data will be deleted again after the tuning process. If no argument is specified, then a temporary folder (as optionally specified with the temp_folder argument) will be used.", getDefaultValue: () => null);
        var arg_restore = new Option<string?>(aliases: ["--restoretarget"], description: "Target folder to restore a backup to. The folder should be empty beforehand, as it needs to be emptied during measurements. If no argument is specified, then a temporary folder (as optionally specified with the temp_folder argument) will be used.", getDefaultValue: () => null);
        var arg_tmp = new Option<string?>(aliases: ["--temp-folder"], description: "Path to where the temporary files should be created. If no argument is specified, then the system default (e.g. /tmp or %TEMP%) will be used.", getDefaultValue: () => null);

        var opt_backend_options = new Option<List<string>>(aliases: ["--backend-options"], description: "Duplicati options to pass to the backend during backup. Each option is a key-value pair spearated by an equals sign, e.g. --backend-options key1=value1 key2=value2. Default is an empty list.", getDefaultValue: () => [])
        {
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true
        };

        var root_cmd = new RootCommand("Auto tuning of the Duplicati concurrency parameters.")
        {
            arg_src, arg_dst,
            arg_tmp,
            opt_backend_options
        };

        root_cmd.Handler = CommandHandler.Create(async (string? source, string? destination, string? restoretarget, string? tempfolder, List<string> options, CancellationToken token) =>
        {
            // Check if a specific temp folder has been specified.
            tempfolder ??= Path.Combine(Path.GetTempPath(), $"duplicati_autotune_{new Guid()}");

            // Check if any of the other paths are unspecified and should thus use the temp folder.
            bool any_in_tmp = (source is null) || (destination is null) || (restoretarget is null);
            source ??= Path.Combine(tempfolder, "source");
            destination ??= Path.Combine(tempfolder, "backup");
            restoretarget ??= Path.Combine(tempfolder, "restored");
            if (any_in_tmp)
                tempfolder = Path.Combine(tempfolder, "temp");

            // Check whether the paths exist, and if so, whether they already contain data.
            static void create_dir(string path)
            {
                Console.WriteLine($"Creating folder {path}");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
            create_dir(source);
            create_dir(destination);
            create_dir(tempfolder);
            create_dir(restoretarget);

            if (!Directory.EnumerateFiles(source).Any())
                await GenerateData(source, token);

            var opts = ParseOptions(options);

            var rc = await RunCore(source, destination, restoretarget, tempfolder, opts);

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
    internal static async Task<int> RunCore(string source, string destination, string restoretarget, string tempfolder, Dictionary<string, string?> options)
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
                await Console.Error.WriteLineAsync(e.StackTrace);
                return -1;
            }

        using (var c = new Controller(destination, options, sink))
            try
            {
                c.Restore(["*"]);
            }
            catch (Exception e)
            {
                await Console.Error.WriteLineAsync($"Error during backup: {e.Message}.");
                await Console.Error.WriteLineAsync(e.StackTrace);
                return -1;
            }
        var profile_default_baseline = await sink.ParseLines();
        Console.WriteLine($"Default configuration ran in {profile_default_baseline.Total} ms");
        var config_current = DefaultConfigRestore with
        {
            FileProcessors = 1,
            VolumeDecompressors = 1,
            VolumeDecryptors = 1,
            VolumeDownloaders = 1
        };

        var profile_best = EmptyResultsRestore(int.MaxValue);
        var config_best = config_current;

        while (true)
        {
            Directory.Delete(restoretarget, true);
            Directory.CreateDirectory(restoretarget);
            SetOptions(options, config_current);
            sink.Reset();
            using var c = new Controller(destination, options, sink);

            c.Restore(["*"]);

            var profile_current = await sink.ParseLines();
            Console.WriteLine($"New run {profile_current.Total} - {profile_best.Total}");

            if (profile_current.Total < profile_best.Total)
            {
                profile_best = profile_current;
                config_best = config_current;

                // TODO current strategy is to double the count of the process with the longest execution time.
                List<(int, int)> candidates = [
                    (0, (int) profile_current.FileProcessor.Execute.Average()),
                    (1, (int) profile_current.VolumeDecompressor.Execute.Average()),
                    (2, (int) profile_current.VolumeDecryptor.Execute.Average()),
                    (3, (int) profile_current.VolumeDownloader.Execute.Average())
                ];
                Console.WriteLine($"{candidates[0].Item2} {candidates[1].Item2} {candidates[2].Item2} {candidates[3].Item2}");
                var (idx, _) = candidates.MaxBy(x => x.Item2);
                Console.WriteLine($"Increasing {idx}");
                config_current = idx switch
                {
                    0 => config_best with { FileProcessors = config_best.FileProcessors * 2 },
                    1 => config_best with { VolumeDecompressors = config_best.VolumeDecompressors * 2 },
                    2 => config_best with { VolumeDecryptors = config_best.VolumeDecryptors * 2 },
                    3 => config_best with { VolumeDownloaders = config_best.VolumeDownloaders * 2 },
                    _ => throw new Exception($"Incorrect process idx specified: {idx}"),
                };
            }
            else
                break;
        }

        // Cleanup
        using (var c = new Controller(destination, options, sink))
            try
            {
                c.Delete();
            }
            catch (Exception e)
            {
                await Console.Error.WriteLineAsync($"Error during backup: {e.Message}.");
                await Console.Error.WriteLineAsync(e.StackTrace);
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

    private static async Task GenerateData(string path, CancellationToken token)
    {
        var cmd = TestDataGenerator.Commands.Create.CreateCommand();
        const long MB = 1024 * 1024;
        long max_file_size = 1 * MB;
        long max_total_size = 1024 * MB;
        long file_count = 10000;
        long sparse_factor = 30;
        var args = $"\"{path}\" --max-file-size {max_file_size} --max-total-size {max_total_size} --file-count {file_count} --sparse-factor {sparse_factor}";
        await cmd.InvokeAsync(args);
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