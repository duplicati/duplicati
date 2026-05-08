using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using Duplicati.Library.Main;

#nullable enable

namespace AutoTune;

// TODO backup command tuning
// TODO dblock size
// TODO data sizes
public record ConfigRestore
{
    public int ChannelDepth { get; init; }
    public int FileProcessors { get; init; }
    public int VolumeDownloaders { get; init; }
    public int VolumeDecryptors { get; init; }
    public int VolumeDecompressors { get; init; }
}

public class Program
{
    private readonly ConfigRestore DefaultConfigRestore = new()
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

    internal static async Task<int> RunCore(string source, string destination, string restoretarget, string tempfolder, Dictionary<string, string?> options)
    {
        if (!source.Contains("://"))
            source = $"file://{source}";
        if (!destination.Contains("://"))
            destination = $"file://{destination}";
        var database = Path.Combine(tempfolder, "db.sqlite");
        options["passphrase"] = "1234";
        options["dbpath"] = database;
        options["restore-path"] = restoretarget;
        options["allow-full-removal"] = "true";
        options["tempdir"] = tempfolder;
        options["version"] = "0";

        using var c = new Controller(destination, options, null);
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