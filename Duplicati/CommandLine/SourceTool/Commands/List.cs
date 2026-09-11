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
using System.Text.Json;
using System.Text.Json.Serialization;
using Duplicati.Library.Interface;

namespace Duplicati.CommandLine.SourceTool.Commands;

/// <summary>
/// The list command
/// </summary>
public static class List
{
    /// <summary>
    /// The JSON serializer options used for JSON output
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// DTO for a single entry in JSON output
    /// </summary>
    /// <param name="Path">The path of the entry</param>
    /// <param name="IsFolder">True if the entry is a folder</param>
    /// <param name="IsMetaEntry">True if the entry is a meta entry</param>
    /// <param name="IsRootEntry">True if the entry is the root entry</param>
    /// <param name="CreatedUtc">The creation time of the entry</param>
    /// <param name="LastModificationUtc">The last modification time of the entry</param>
    /// <param name="Size">The size of the entry</param>
    /// <param name="IsSymlink">True if the entry is a symlink</param>
    /// <param name="SymlinkTarget">The target of the symlink, if the entry is a symlink</param>
    /// <param name="Attributes">The entry attributes</param>
    /// <param name="IsBlockDevice">True if the entry is a block device</param>
    /// <param name="IsCharacterDevice">True if the entry is a character device</param>
    /// <param name="IsAlternateStream">True if the entry is an alternate stream</param>
    /// <param name="HardlinkTargetId">The hardlink target id, if the entry is a hardlink</param>
    /// <param name="Metadata">The minor metadata of the entry</param>
    private sealed record EntryDto(
        string Path,
        bool IsFolder,
        bool IsMetaEntry,
        bool IsRootEntry,
        DateTime CreatedUtc,
        DateTime LastModificationUtc,
        long Size,
        bool IsSymlink,
        string? SymlinkTarget,
        FileAttributes Attributes,
        bool IsBlockDevice,
        bool IsCharacterDevice,
        bool IsAlternateStream,
        string? HardlinkTargetId,
        Dictionary<string, string?> Metadata
    );

    /// <summary>
    /// Creates the list command
    /// </summary>
    /// <returns>The command</returns>
    public static Command Create()
    {
        var urlArgument = new Argument<string>("url") { Description = "The source URL", Arity = ArgumentArity.ExactlyOne };
        var pathArgument = new Argument<string?>("path") { Description = "The path to start listing from", Arity = ArgumentArity.ZeroOrOne };
        var maxDepthOption = new Option<int>("--max-depth") { Description = "The maximum depth to list", DefaultValueFactory = _ => 0 };
        var outputJsonOption = new Option<bool>("--output-json") { Description = "Output as JSON", DefaultValueFactory = _ => false };

        var cmd = new Command("list", "Lists all paths on the remote")
        {
            urlArgument,
            pathArgument,
            maxDepthOption,
            outputJsonOption
        };

        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var url = parseResult.GetValue(urlArgument)!;
            var path = parseResult.GetValue(pathArgument);
            var maxdepth = parseResult.GetValue(maxDepthOption);
            var outputjson = parseResult.GetValue(outputJsonOption);

            var folders = 0L;
            var files = 0L;
            var entries = outputjson ? new List<EntryDto>() : null;

            using var source = await Common.GetProvider(url);

            var visitor = async (ISourceProviderEntry entry, int level) =>
            {
                if (entry.IsFolder)
                    folders++;
                else
                    files++;

                if (entries != null)
                {
                    if (!entry.IsRootEntry)
                        entries.Add(new EntryDto(
                            entry.Path,
                            entry.IsFolder,
                            entry.IsMetaEntry,
                            entry.IsRootEntry,
                            entry.CreatedUtc,
                            entry.LastModificationUtc,
                            entry.Size,
                            entry.IsSymlink,
                            entry.SymlinkTarget,
                            entry.Attributes,
                            entry.IsBlockDevice,
                            entry.IsCharacterDevice,
                            entry.IsAlternateStream,
                            entry.HardlinkTargetId,
                            await entry.GetMinorMetadata(cancellationToken)
                        ));
                }
                else
                {
                    if (entry.IsFolder && level > 0)
                        level--;
                    if (!entry.IsRootEntry)
                        Console.WriteLine($"{new string(' ', (level + 1) * 2)}{entry.Path}");
                }

                return true;
            };

            if (string.IsNullOrWhiteSpace(path))
            {
                await Common.Visit(source, maxdepth, visitor, cancellationToken);
            }
            else
            {
                var entry = await source.GetEntryAsync(path, true, cancellationToken);
                if (entry == null || !entry.IsFolder)
                    throw new UserInformationException("Folder does not exist", "FolderDoesNotExist");

                await Common.Visit(entry, maxdepth, visitor, cancellationToken);
            }

            if (entries != null)
                Console.WriteLine(JsonSerializer.Serialize(entries, JsonOptions));
            else
                Console.WriteLine($"Found {folders} folders and {files} files");
        });

        return cmd;
    }

}
