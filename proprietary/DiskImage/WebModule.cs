using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;
using Duplicati.Proprietary.DiskImage.General;

namespace Duplicati.Proprietary.DiskImage;

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
        var op = Utility.ParseEnumOption(options.AsReadOnly(), KEY_OPERATION, DEFAULT_OPERATION);
        options.TryGetValue(KEY_PATH, out var path);

        if (op != Operation.ListDestination)
            throw new UserInformationException($"Unsupported operation: {op}", "UnsupportedOperation");

        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            var res = new Dictionary<string, string>();
            await foreach (var drive in SourceProvider.ListPhysicalDrives(cancellationToken))
                AddDiskToResult(drive, res);
            return res;
        }

        var prefix = SourceProvider.GetDevicePrefix();
        List<string> parts = [.. path[prefix.Length..].Split(Path.DirectorySeparatorChar, 2)];
        var physicalDrivePath = prefix + parts.First();
        var subpath = parts.Last();

        using var client = new SourceProvider("diskimage://" + physicalDrivePath, "", new Dictionary<string, string?>(options));
        await client.Initialize(cancellationToken);

        if (string.IsNullOrWhiteSpace(subpath))
            return new Dictionary<string, string>()
            {
                {$"{physicalDrivePath}{Path.DirectorySeparatorChar}root{Path.DirectorySeparatorChar}", "{}"}
            };

        var targetEntry = await client.GetEntry(subpath, isFolder: true, cancellationToken).ConfigureAwait(false)
            ?? throw new DirectoryNotFoundException($"Path not found: {path}");

        var result = new Dictionary<string, string>();
        await foreach (var entry in targetEntry.Enumerate(cancellationToken))
        {
            if (entry.Path.EndsWith("geometry.json", StringComparison.OrdinalIgnoreCase))
                continue;
            var metadata = await entry.GetMinorMetadata(cancellationToken);
            if (metadata.ContainsKey("partition:Number"))
                result[$"{physicalDrivePath}{Path.DirectorySeparatorChar}{entry.Path.TrimEnd(Path.DirectorySeparatorChar)}"] = JsonSerializer.Serialize(metadata);
            else
                result[$"{physicalDrivePath}{Path.DirectorySeparatorChar}{entry.Path}"] = JsonSerializer.Serialize(metadata);
        }

        return result;
    }

    private void AddDiskToResult(PhysicalDriveInfo drive, Dictionary<string, string> result)
    {
        // For now, we only allow picking full disks, not individual partitions
        // var resultpath = Util.AppendDirSeparator(drive.Path);
        var resultpath = drive.Path.TrimEnd(Path.DirectorySeparatorChar);
        var metadata = new Dictionary<string, string?>
        {
            { "diskimage:Number", drive.Number },
            { "diskimage:FriendlyName", drive.DisplayName },
            { "diskimage:Size", drive.Size.ToString() },
            { "diskimage:DevicePath", drive.Path },
            { "diskimage:Name", $"{drive.DisplayName} ({Library.Utility.Utility.FormatSizeString(drive.Size)})" },
        };

        result[resultpath] = JsonSerializer.Serialize(metadata);
    }

    public IDictionary<string, IDictionary<string, string>> GetLookups()
        => new Dictionary<string, IDictionary<string, string>>();
}