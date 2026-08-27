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

namespace Duplicati.CommandLine.SourceTool.Commands;

/// <summary>
/// The download command
/// </summary>
public static class Download
{
    /// <summary>
    /// Creates the download command
    /// </summary>
    /// <returns>The download command</returns>
    public static Command Create()
    {
        var urlArgument = new Argument<string>("url") { Description = "The source URL", Arity = ArgumentArity.ExactlyOne };
        var destinationOption = new Option<DirectoryInfo>("--destination") { Description = "The destination folder", DefaultValueFactory = _ => new DirectoryInfo(Directory.GetCurrentDirectory()) };
        var maxDepthOption = new Option<int>("--max-depth") { Description = "The maximum depth to visit", DefaultValueFactory = _ => 0 };
        var maxSizeOption = new Option<long>("--max-size") { Description = "The maximum filesize to download", DefaultValueFactory = _ => 0 };
        var overwriteOption = new Option<bool>("--overwrite") { Description = "Overwrite existing files", DefaultValueFactory = _ => false };

        var cmd = new Command("download", "Downloads all files from the remote")
        {
            urlArgument,
            destinationOption,
            maxDepthOption,
            maxSizeOption,
            overwriteOption
        };

        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var url = parseResult.GetValue(urlArgument)!;
            var destination = parseResult.GetValue(destinationOption)!;
            var maxdepth = parseResult.GetValue(maxDepthOption);
            var maxsize = parseResult.GetValue(maxSizeOption);
            var overwrite = parseResult.GetValue(overwriteOption);

            using var source = await Common.GetProvider(url);

            string localPath(string path)
            {
                var relpath = path.Substring(source.MountedPath.Length).TrimStart(Path.DirectorySeparatorChar);
                return Path.Combine(destination.FullName, relpath);
            }

            var started = DateTime.UtcNow;
            var fileCount = 0L;
            var folderCount = 0L;
            var totalSize = 0L;
            var downloadCount = 0L;
            var downloadSize = 0L;

            await Common.Visit(source, maxdepth, async (entry, level) =>
            {
                if (entry.IsMetaEntry)
                    return true;

                var path = localPath(entry.Path);
                if (entry.IsFolder)
                {
                    folderCount++;
                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);
                }
                else
                {
                    fileCount++;
                    totalSize += entry.Size;

                    if (entry.Size <= maxsize || maxsize <= 0)
                    {
                        var folder = Path.GetDirectoryName(path);
                        if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
                            Directory.CreateDirectory(folder);

                        if (!overwrite && File.Exists(path))
                        {
                            Console.WriteLine($"Skipping {entry.Path} as it already exists");
                        }
                        else
                        {
                            downloadCount++;
                            downloadSize += entry.Size;
                            Console.WriteLine($"Downloading {entry.Path} to {path} ({Library.Utility.Utility.FormatSizeString(entry.Size)})");
                            await using var stream = await entry.OpenRead(cancellationToken);
                            await using var dest = File.OpenWrite(path);
                            await stream.CopyToAsync(dest, cancellationToken);
                        }
                    }
                }

                return true;
            }, cancellationToken);

            Console.WriteLine($"Found {fileCount} files and {folderCount} folders with a total size of {Library.Utility.Utility.FormatSizeString(totalSize)}");
            Console.WriteLine($"Downloaded {downloadCount} files with a total size of {Library.Utility.Utility.FormatSizeString(downloadSize)} in {DateTime.UtcNow - started}");
        });

        return cmd;
    }
}
