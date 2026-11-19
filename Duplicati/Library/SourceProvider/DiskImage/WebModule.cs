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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;
using Duplicati.Library.SourceProvider.DiskImage.General;

namespace Duplicati.Library.SourceProvider.DiskImage;

public class WebModule : IWebModule
{
    private static readonly string LOGTAG = Log.LogTagFromType<WebModule>();

    public string Key => OptionsHelper.ModuleKey;

    public string DisplayName => Strings.WebModuleDisplayName;

    public string Description => Strings.WebModuleDescription;

    public enum Operation
    {
        ListDestination
    }

    private static readonly Operation DEFAULT_OPERATION = Operation.ListDestination;
    private const string KEY_OPERATION = "operation";
    private const string KEY_URL = "url";
    private const string KEY_PATH = "path";

    public IList<ICommandLineArgument> SupportedCommands => [
        new CommandLineArgument(KEY_OPERATION, CommandLineArgument.ArgumentType.Enumeration, Strings.WebModuleOperationShort, Strings.WebModuleOperationLong, DEFAULT_OPERATION.ToString(), null, Enum.GetNames<Operation>()),
        new CommandLineArgument(KEY_URL, CommandLineArgument.ArgumentType.String, Strings.WebModuleURLShort, Strings.WebModuleURLLong),
        new CommandLineArgument(KEY_PATH, CommandLineArgument.ArgumentType.String, Strings.WebModulePathShort, Strings.WebModulePathLong)
    ];

    public async Task<IDictionary<string, string>> Execute(IDictionary<string, string?> options, CancellationToken cancellationToken)
    {
        var op = Utility.Utility.ParseEnumOption(options.AsReadOnly(), KEY_OPERATION, DEFAULT_OPERATION);
        options.TryGetValue(KEY_PATH, out var path);

        if (op != Operation.ListDestination)
            throw new UserInformationException($"Unsupported operation: {op}", "UnsupportedOperation");

        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            var res = new Dictionary<string, string>();
            await foreach (var drive in SourceProvider.ListPhysicalDrives(cancellationToken))
                res[drive.Path] = JsonSerializer.Serialize(await drive.GetMinorMetadata(cancellationToken));

            return res;
        }

        var (physicalDrivePath, subpath) = SourceProvider.SplitDeviceAndSubpath(path);

        using var client = new SourceProvider("diskimage://" + physicalDrivePath, "", new Dictionary<string, string?>(options));
        await client.InitializeAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(subpath))
            return new Dictionary<string, string>()
            {
                {$"{physicalDrivePath}{Path.DirectorySeparatorChar}root{Path.DirectorySeparatorChar}", "{}"}
            };

        var targetEntry = await client.GetEntryAsync(subpath, isFolder: true, cancellationToken).ConfigureAwait(false)
            ?? throw new DirectoryNotFoundException($"Path not found: {path}");

        var result = new Dictionary<string, string>();
        await foreach (var entry in targetEntry.Enumerate(cancellationToken))
        {
            if (entry.Path.EndsWith(GeometryMetadata.FileName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (entry.Path.EndsWith(PartitionInfoMetadata.FileName, StringComparison.OrdinalIgnoreCase))
                continue;
            var metadata = await entry.GetMinorMetadata(cancellationToken);
            if (metadata.ContainsKey("partition:Number"))
                result[$"{physicalDrivePath}{Path.DirectorySeparatorChar}{entry.Path.TrimEnd(Path.DirectorySeparatorChar)}"] = JsonSerializer.Serialize(metadata);
            else
                result[$"{physicalDrivePath}{Path.DirectorySeparatorChar}{entry.Path}"] = JsonSerializer.Serialize(metadata);
        }

        return result;
    }

    public IDictionary<string, IDictionary<string, string>> GetLookups()
        => new Dictionary<string, IDictionary<string, string>>();
}