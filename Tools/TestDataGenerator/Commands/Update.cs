using System.CommandLine;
using static TestDataGenerator.Commands.Shared;

namespace TestDataGenerator.Commands;

/// <summary>
/// Update test data in a folder
/// </summary>
public static class Update
{
    /// <summary>
    /// The input parameters for the command
    /// </summary>
    /// <param name="TargetFolder">The folder to update files in</param>
    /// <param name="NewFiles">The number of new files to create in the target folder</param>
    /// <param name="UpdatedFiles">The number of files to update in the target folder</param>
    /// <param name="DeletedFiles">The number of files to delete in the target folder</param>
    /// <param name="RenameFiles">The number of files to rename in the target folder</param>
    /// <param name="MaxFileSize">The maximum size of each file to create in the target folder</param>
    /// <param name="MinFileSize">The minimum size of each file to create in the target folder</param>
    /// <param name="SparseFactor">The percentage of data that should be zeroed out in each file</param>
    /// <param name="UpdateFactor">The percentage of files to update in the target folder</param>
    /// <param name="MaxPathSegmentLength">The maximum length of each path segment in the target folder</param>
    record CommandInput(
        DirectoryInfo TargetFolder,
        int NewFiles,
        int UpdatedFiles,
        int DeletedFiles,
        int RenameFiles,
        long MaxFileSize,
        long MinFileSize,
        int SparseFactor,
        int UpdateFactor,
        int MaxPathSegmentLength);

    /// <summary>
    /// Creates the command
    /// </summary>
    /// <returns>The command</returns>
    public static Command CreateCommand()
    {
        var command = new Command("update", "Update test data in a folder");

        var targetFolderArgument = new Argument<DirectoryInfo>("target-folder")
        {
            Description = "The folder to update files in",
            DefaultValueFactory = _ => new DirectoryInfo(Directory.GetCurrentDirectory())
        };
        command.Arguments.Add(targetFolderArgument);

        var newFilesOption = new Option<int>("--new-files")
        {
            Description = "The number of new files to create in the target folder",
            DefaultValueFactory = _ => 1000
        };
        command.Options.Add(newFilesOption);

        var updatedFilesOption = new Option<int>("--updated-files")
        {
            Description = "The number of files to update in the target folder",
            DefaultValueFactory = _ => 1000
        };
        command.Options.Add(updatedFilesOption);

        var deletedFilesOption = new Option<int>("--deleted-files")
        {
            Description = "The number of files to delete in the target folder",
            DefaultValueFactory = _ => 1000
        };
        command.Options.Add(deletedFilesOption);

        var renameFilesOption = new Option<int>("--rename-files")
        {
            Description = "The number of files to rename in the target folder",
            DefaultValueFactory = _ => 1000
        };
        command.Options.Add(renameFilesOption);

        var maxFileSizeOption = new Option<long>("--max-file-size")
        {
            Description = "The maximum size of each file to create in the target folder",
            DefaultValueFactory = _ => 1024 * 1024L
        };
        command.Options.Add(maxFileSizeOption);

        var minFileSizeOption = new Option<long>("--min-file-size")
        {
            Description = "The minimum size of each file to create in the target folder",
            DefaultValueFactory = _ => 1024L
        };
        command.Options.Add(minFileSizeOption);

        var sparseFactorOption = new Option<int>("--sparse-factor")
        {
            Description = "The percentage of data that should be zeroed out in each file",
            DefaultValueFactory = _ => 10
        };
        command.Options.Add(sparseFactorOption);

        var updateFactorOption = new Option<int>("--update-factor")
        {
            Description = "The percentage of files to update in the target folder",
            DefaultValueFactory = _ => 10
        };
        command.Options.Add(updateFactorOption);

        var maxPathSegmentLengthOption = new Option<int>("--max-path-segment-length")
        {
            Description = "The maximum length of each path segment in the target folder",
            DefaultValueFactory = _ => 15
        };
        command.Options.Add(maxPathSegmentLengthOption);

        command.SetAction(parseResult =>
        {
            var input = new CommandInput(
                TargetFolder: parseResult.GetValue(targetFolderArgument)!,
                NewFiles: parseResult.GetValue(newFilesOption),
                UpdatedFiles: parseResult.GetValue(updatedFilesOption),
                DeletedFiles: parseResult.GetValue(deletedFilesOption),
                RenameFiles: parseResult.GetValue(renameFilesOption),
                MaxFileSize: parseResult.GetValue(maxFileSizeOption),
                MinFileSize: parseResult.GetValue(minFileSizeOption),
                SparseFactor: parseResult.GetValue(sparseFactorOption),
                UpdateFactor: parseResult.GetValue(updateFactorOption),
                MaxPathSegmentLength: parseResult.GetValue(maxPathSegmentLengthOption));
            Execute(input);
        });

        return command;
    }

    /// <summary>
    /// Executes the command
    /// </summary>
    /// <param name="input">The input parameters</param>
    private static void Execute(CommandInput input)
    {
        if (!input.TargetFolder.Exists)
            throw new DirectoryNotFoundException($"The target folder {input.TargetFolder.FullName} does not exist");

        if (input.MaxFileSize == 0)
            throw new Exception("The maximum file size must be greater than zero");

        Console.WriteLine($"Updating test data in {input.TargetFolder.FullName}");

        var folders = new List<string>();
        var files = new List<string>();

        foreach (var folder in input.TargetFolder.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            folders.Add(folder.FullName);
            foreach (var file in folder.EnumerateFiles())
                files.Add(file.FullName);
        }

        Console.WriteLine($"Found {folders.Count} folders and {files.Count} files");

        folders.Add(input.TargetFolder.FullName);

        var rnd = new Random();
        var newFileNames = DistributeFiles(rnd, folders, GenerateFileNames(rnd, input.NewFiles, input.MaxPathSegmentLength));

        foreach (var file in newFileNames)
        {
            Console.WriteLine($"Creating new file {file}");

            var size = rnd.NextInt64((int)input.MinFileSize, (int)input.MaxFileSize);
            using var fs = new FileStream(file, FileMode.Create, FileAccess.Write);
            fs.SetLength(size);
            if (size > 0)
            {
                fs.Position = 0;
                WriteRandomData(rnd, fs, size, input.SparseFactor);
            }
        }

        var updatedFileNames = new HashSet<string>();
        while (updatedFileNames.Count < input.UpdatedFiles)
        {
            var file = files[rnd.Next(files.Count)];
            if (!updatedFileNames.Contains(file))
                updatedFileNames.Add(file);

            if (updatedFileNames.Count == files.Count)
                break;
        }

        foreach (var file in updatedFileNames)
        {
            Console.WriteLine($"Updating file {file}");

            var size = rnd.NextInt64(input.MinFileSize, input.MaxFileSize);
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Write);
            fs.SetLength(size);
            if (size > 0)
            {
                fs.Position = 0;
                WriteRandomData(rnd, fs, size, 100 - input.UpdateFactor);
            }
        }

        var deletedFileNames = new HashSet<string>();
        while (deletedFileNames.Count < input.DeletedFiles)
        {
            var file = files[rnd.Next(files.Count)];
            if (!deletedFileNames.Contains(file))
                deletedFileNames.Add(file);

            if (deletedFileNames.Count == files.Count)
                break;
        }

        foreach (var file in deletedFileNames)
        {
            Console.WriteLine($"Deleting file {file}");
            File.Delete(file);
        }

        var renamedFileNames = new HashSet<string>();
        while (renamedFileNames.Count < input.RenameFiles)
        {
            var file = files[rnd.Next(files.Count)];
            if (!renamedFileNames.Contains(file))
                renamedFileNames.Add(file);

            if (renamedFileNames.Count == files.Count)
                break;
        }

        foreach (var file in renamedFileNames)
        {
            var newName = Path.Combine(folders[rnd.Next(folders.Count)], GenerateFileNames(rnd, 1, input.MaxPathSegmentLength).First());
            Console.WriteLine($"Renaming file {file} to {newName}");
            File.Move(file, newName);
        }
    }
}
