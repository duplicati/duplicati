using System.Collections.Concurrent;
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using static TestDataGenerator.Commands.Shared;

namespace TestDataGenerator.Commands;

/// <summary>
/// Create test data in a folder
/// </summary>
public static class Create
{
    /// <summary>
    /// The input parameters for the command
    /// </summary>
    /// <param name="TargetFolder">The folder to create files in</param>
    /// <param name="FileCount">The number of files to create in the target folder</param>
    /// <param name="MaxTotalSize">The maximum total size of the files to create in the target folder</param>
    /// <param name="MaxFileSize">The maximum size of each file to create in the target folder</param>
    /// <param name="MinFileSize">The minimum size of each file to create in the target folder</param>
    /// <param name="SparseFactor">The percentage of data that should be zeroed out in each file</param>
    /// <param name="MaxFanOut">The maximum number of subfolders to create in each folder</param>
    /// <param name="MaxDepth">The maximum depth of subfolders to create in the target folder</param>
    /// <param name="MaxFolderCount">The maximum number of folders to create in the target folder</param>
    /// <param name="MaxPathSegmentLength">The maximum length of each path segment in the target folder</param>
    /// <param name="RandomSeed">The random seed to use for generating the test data</param>
    /// <param name="Parallel">The number of parallel tasks to use for creating the test data</param>
    record CommandInput(
        DirectoryInfo TargetFolder,
        int FileCount,
        long MaxTotalSize,
        long MaxFileSize,
        long MinFileSize,
        int SparseFactor,
        int MaxFanOut,
        int MaxDepth,
        int MaxFolderCount,
        int MaxPathSegmentLength,
        string RandomSeed,
        int Parallel);

    /// <summary>
    /// Creates the command
    /// </summary>
    /// <returns>The command</returns>
    public static Command CreateCommand()
    {
        var command = new Command("create", "Create test data in a folder");

        var targetFolderOption = new Argument<DirectoryInfo>("target-folder", "The folder to create files in");
        targetFolderOption.SetDefaultValue(new DirectoryInfo(Directory.GetCurrentDirectory()));
        command.AddArgument(targetFolderOption);

        var fileCountOption = new Option<int>("--file-count", "The number of files to create in the target folder");
        fileCountOption.SetDefaultValue(200000);
        command.AddOption(fileCountOption);

        var maxTotalSizeOption = new Option<long>("--max-total-size", "The maximum total size of the files to create in the target folder");
        maxTotalSizeOption.SetDefaultValue(1024 * 1024 * 1024L);
        command.AddOption(maxTotalSizeOption);

        var maxFileSizeOption = new Option<long>("--max-file-size", "The maximum size of each file to create in the target folder");
        maxFileSizeOption.SetDefaultValue(1024 * 1024L);
        command.AddOption(maxFileSizeOption);

        var minFileSizeOption = new Option<long>("--min-file-size", "The minimum size of each file to create in the target folder");
        minFileSizeOption.SetDefaultValue(0L);
        command.AddOption(minFileSizeOption);

        var sparseFactorOption = new Option<int>("--sparse-factor", "The percentage of data that should be zeroed out in each file");
        sparseFactorOption.SetDefaultValue(10);
        command.AddOption(sparseFactorOption);

        var maxFanOutOption = new Option<int>("--max-fan-out", "The maximum number of subfolders to create in each folder");
        maxFanOutOption.SetDefaultValue(10);
        command.AddOption(maxFanOutOption);

        var maxDepthOption = new Option<int>("--max-depth", "The maximum depth of subfolders to create in the target folder");
        maxDepthOption.SetDefaultValue(5);
        command.AddOption(maxDepthOption);

        var maxFolderCountOption = new Option<int>("--max-folder-count", "The maximum number of folders to create in the target folder");
        maxFolderCountOption.SetDefaultValue(10000);
        command.AddOption(maxFolderCountOption);

        var maxPathSegmentLengthOption = new Option<int>("--max-path-segment-length", "The maximum length of each path segment in the target folder");
        maxPathSegmentLengthOption.SetDefaultValue(15);
        command.AddOption(maxPathSegmentLengthOption);

        var randomSeedOption = new Option<string>("--random-seed", "The random seed to use for generating the test data");
        randomSeedOption.SetDefaultValue("Duplicati");
        command.AddOption(randomSeedOption);

        var parallelOption = new Option<int>("--parallel", "The number of parallel tasks to use for creating the test data");
        parallelOption.SetDefaultValue(Environment.ProcessorCount);
        command.AddOption(parallelOption);

        command.Handler = CommandHandler.Create<CommandInput>(Execute);

        return command;
    }

    /// <summary>
    /// Executes the command
    /// </summary>
    /// <param name="input">The input parameters</param>
    private static void Execute(CommandInput input)
    {
        if (input.TargetFolder.Exists && input.TargetFolder.GetFileSystemInfos().Length > 0)
            throw new Exception($"The target folder {input.TargetFolder.FullName} already exists");

        if (input.MaxFileSize == 0)
            throw new Exception("The maximum file size must be greater than zero");

        if (input.MaxTotalSize == 0)
            throw new Exception("The maximum total size must be greater than zero");

        if (input.MinFileSize > input.MaxFileSize)
            throw new Exception("The minimum file size must be less than or equal to the maximum file size");

        if (input.FileCount == 0)
            throw new Exception("The file count must be greater than zero");

        if (input.MaxFolderCount == 0)
            throw new Exception("The maximum folder count must be greater than zero");

        if (input.MaxFanOut == 0)
            throw new Exception("The maximum fan-out must be greater than zero");

        if (input.MaxDepth == 0)
            throw new Exception("The maximum depth must be greater than zero");

        if (input.MaxPathSegmentLength == 0)
            throw new Exception("The maximum path segment length must be greater than zero");

        Console.WriteLine($"Creating test data in {input.TargetFolder.FullName}");
        var rnd = new Random(input.RandomSeed.GetHashCode());
        var folders = GeneratePathStructure(rnd, input.TargetFolder.FullName, input.MaxDepth, input.MaxFanOut, input.MaxPathSegmentLength);
        while (folders.Count < input.MaxFolderCount)
        {
            folders.AddRange(GeneratePathStructure(rnd, input.TargetFolder.FullName, input.MaxDepth, input.MaxFanOut, input.MaxPathSegmentLength));
            folders = folders.Distinct().ToList();
        }

        while (folders.Count > input.MaxFolderCount)
            folders.RemoveAt(folders.Count - 1);

        folders.Add(input.TargetFolder.FullName);

        var files = DistributeFiles(rnd, folders, GenerateFileNames(rnd, input.FileCount, input.MaxPathSegmentLength));

        var filesWithSizes = files.Select(x => (x, rnd.NextInt64(input.MinFileSize, input.MaxFileSize))).ToList();
        var totalSize = filesWithSizes.Sum(x => x.Item2);

        while (totalSize > input.MaxTotalSize)
        {
            var index = rnd.Next(filesWithSizes.Count);
            var newSize = rnd.NextInt64(input.MinFileSize, filesWithSizes[index].Item2);
            totalSize -= filesWithSizes[index].Item2 - newSize;
            filesWithSizes[index] = (filesWithSizes[index].Item1, newSize);
        }

        Console.WriteLine($"Creating {folders.Count - 1} folders and {files.Count} files ({SizeToHumanReadable(totalSize)})");
        if (!input.TargetFolder.Exists)
            input.TargetFolder.Create();

        var lastUpdate = DateTime.UtcNow;
        var foldersCreated = 0L;

        var updateInterval = TimeSpan.FromSeconds(10);

        foreach (var folder in folders)
        {
            Directory.CreateDirectory(folder);
            foldersCreated++;
            if ((DateTime.UtcNow - lastUpdate) > updateInterval)
            {
                Console.WriteLine($"Created {foldersCreated} of {folders.Count} folders");
                lastUpdate = DateTime.UtcNow;
            }
        }

        object stats_lock = new();
        var filesCreated = 0L;
        var fileSizeCreated = 0L;
        var generateStart = DateTime.UtcNow;

        var cancelToken = new CancellationTokenSource();
        var status_updater = Task.Run(async () =>
        {
            while (!cancelToken.Token.IsCancellationRequested)
            {
                await Task.Delay((int)updateInterval.TotalMilliseconds, cancelToken.Token);
                long local_filesCreated, local_fileSizeCreated;
                lock (stats_lock)
                {
                    local_filesCreated = filesCreated;
                    local_fileSizeCreated = fileSizeCreated;
                }

                var generateNow = DateTime.UtcNow;
                long throughput = (long)Math.Max(Math.Floor(local_fileSizeCreated / (generateNow - generateStart).TotalSeconds), 1);
                var timeLeft = TimeSpan.FromSeconds((input.MaxTotalSize - local_fileSizeCreated) / throughput);
                string timeLeftString = timeLeft.ToString(@"hh\:mm\:ss");
                Console.WriteLine($"Created {local_filesCreated} of {files.Count} files ({SizeToHumanReadable(local_fileSizeCreated)} of {SizeToHumanReadable(totalSize)}) ({SizeToHumanReadable(throughput)}/s) - ETA: {timeLeftString}");
            }
        }, cancelToken.Token);

        Parallel.ForEach(Partitioner.Create(0, filesWithSizes.Count), new ParallelOptions { MaxDegreeOfParallelism = input.Parallel }, range =>
        {
            var local_rnd = new Random(input.RandomSeed.GetHashCode() + range.Item1);
            for (int i = range.Item1; i < range.Item2; i++)
            {
                var (file, size) = filesWithSizes[i];
                var filename = file;
                try
                {
                    // Sometimes with a large number of files, the file name clashes with a folder name. In that case, generate a new file name
                    for (int j = 0; j < 10 && Path.Exists(filename); j++)
                        filename = Path.Combine(Path.GetDirectoryName(filename) ?? "", "_" + Path.GetFileName(filename));

                    using var fs = new FileStream(filename, FileMode.Create, FileAccess.Write);
                    if (size > 0)
                    {
                        fs.SetLength(size);
                        fs.Position = 0;

                        WriteRandomData(local_rnd, fs, size, input.SparseFactor);
                    }

                    lock (stats_lock)
                    {
                        filesCreated++;
                        fileSizeCreated += size;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating file {filename}: {ex.Message}");
                }
            }
        });

        cancelToken.Cancel();
        var time_string = (DateTime.UtcNow - generateStart).ToString(@"hh\:mm\:ss");
        Console.WriteLine($"Created {filesCreated} of {files.Count} files ({SizeToHumanReadable(fileSizeCreated)} of {SizeToHumanReadable(totalSize)} at {SizeToHumanReadable((long)(fileSizeCreated / (DateTime.UtcNow - generateStart).TotalSeconds))}/s) in {time_string}");
    }

    /// <summary>
    /// Generates a list of random folder paths
    /// </summary>
    /// <param name="rnd">The random number generator to use</param>
    /// <param name="prefix">The prefix to use for the folder paths</param>
    /// <param name="maxDepth">The maximum depth of the folder structure</param>
    /// <param name="maxFanOut">The maximum number of subfolders to create in each folder</param>
    /// <param name="maxSegmentLength">The maximum length of each path segment</param>
    private static List<string> GeneratePathStructure(Random rnd, string prefix, int maxDepth, int maxFanOut, int maxSegmentLength)
    {
        var folders = new List<string>();
        var folderCount = rnd.Next(1, maxFanOut + 1);

        if (maxDepth == 0)
            return GeneratePathSegments(rnd, folderCount, maxSegmentLength).Select(x => Path.Combine(prefix, x)).ToList();

        for (var i = 0; i < folderCount; i++)
        {
            var folderName = GetPathSegment(rnd, maxSegmentLength);
            folders.Add(Path.Combine(prefix, folderName));
            folders.AddRange(GeneratePathStructure(rnd, Path.Combine(prefix, folderName), maxDepth - 1, maxFanOut, maxSegmentLength));
        }

        return folders;
    }
}
