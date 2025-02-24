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
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;

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
    public static Command Create() =>
        new Command("download", "Downloads all files from the remote")
        {
            new Argument<string>("url", "The source URL") {
                Arity = ArgumentArity.ExactlyOne
            },
            new Option<DirectoryInfo>("--destination", description: "The destination folder", getDefaultValue: () => new DirectoryInfo(Directory.GetCurrentDirectory())),
            new Option<int>("--max-depth", description: "The maximum depth to visit", getDefaultValue: () => 0),
            new Option<long>("--max-size", description: "The maximum filesize to download", getDefaultValue: () => 0),
            new Option<bool>("--overwrite", description: "Overwrite existing files", getDefaultValue: () => false),
        }
        .WithHandler(CommandHandler.Create<string, DirectoryInfo, int, long, bool>(async (url, destination, maxdepth, maxsize, overwrite) =>
            {
                var token = new CancellationTokenSource().Token;

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
                                await using var stream = await entry.OpenRead(token);
                                await using var dest = File.OpenWrite(path);
                                await stream.CopyToAsync(dest, token);
                            }
                        }
                    }

                    return true;
                }, token);

                Console.WriteLine($"Found {fileCount} files and {folderCount} folders with a total size of {Library.Utility.Utility.FormatSizeString(totalSize)}");
                Console.WriteLine($"Downloaded {downloadCount} files with a total size of {Library.Utility.Utility.FormatSizeString(downloadSize)} in {DateTime.UtcNow - started}");
            }));
}
