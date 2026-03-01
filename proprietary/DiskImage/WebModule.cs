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

namespace Duplicati.Proprietary.DiskImage;

/// <summary>
/// Represents a physical drive returned by PowerShell Get-Disk.
/// </summary>
public class PhysicalDriveInfo
{
    /// <summary>
    /// The drive number (e.g., 0 for \\.\PHYSICALDRIVE0), 3 for /dev/disk3, a for /dev/sda, etc.
    /// </summary>
    public string Number { get; set; } = "";

    /// <summary>
    /// The device path (e.g., \\.\PHYSICALDRIVE0, /dev/disk3, /dev/sda, etc.).
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// The size of the drive in bytes.
    /// </summary>
    public ulong Size { get; set; }

    /// <summary>
    /// The display name of the drive.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// The GUID of the drive.
    /// </summary>
    public string? Guid { get; set; }

    /// <summary>
    /// The mount points associated with this drive.
    /// </summary>
    public string[] MountPoints { get; set; } = [];

    /// <summary>
    /// Whether the drive is online.
    /// </summary>
    public bool? Online { get; set; }
}

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
            if (OperatingSystem.IsWindows())
                return await WindowsListPhysicalDrives(cancellationToken);
            else if (OperatingSystem.IsMacOS())
                return await MacListPhysicalDrives(cancellationToken);
            else
                throw new PlatformNotSupportedException(Strings.PlatformNotSupported);

        string prefix = "";
        if (OperatingSystem.IsWindows())
            prefix = "\\\\.\\";
        else if (OperatingSystem.IsMacOS())
            prefix = "/dev/";
        else
            throw new PlatformNotSupportedException(Strings.PlatformNotSupported);

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

    private async Task<IDictionary<string, string>> WindowsListPhysicalDrives(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(Strings.PlatformNotSupported);

        var script = @"
$diskInfo =
    Get-CimInstance Win32_DiskDrive |
    ForEach-Object {
        $wmi = $_
        $diskNumber = [int]$wmi.Index
        $disk = Get-Disk -Number $diskNumber -ErrorAction SilentlyContinue

        $driveLetters = @(
            Get-Partition -DiskNumber $diskNumber -ErrorAction SilentlyContinue |
            Where-Object { $_.DriveLetter } |
            ForEach-Object { $_.DriveLetter.ToString() } |
            Sort-Object -Unique
        )

        [pscustomobject]@{
            Path         = $wmi.DeviceID
            Size         = [uint64]$wmi.Size
            DisplayName  = if ($disk) { $disk.FriendlyName } else { $wmi.Model }
            Guid         = if ($disk) { $disk.Guid } else { $null }
            DriveLetters = $driveLetters   # Always an array
            Online       = if ($disk) { -not $disk.IsOffline } else { $null }
        }
    }

$diskInfo | ConvertTo-Json -Depth 4
";
        var output = await RunPowerShellAsync(script, cancellationToken);

        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(output))
            return result;

        try
        {
            // PowerShell ConvertTo-Json returns a single object for 1 item, array for multiple
            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var drives = JsonSerializer.Deserialize<PhysicalDriveInfo[]>(output);
                if (drives != null)
                {
                    foreach (var drive in drives)
                    {
                        var number = -1;
                        if (drive.Path.StartsWith("\\\\.\\PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase))
                        {
                            int.TryParse(drive.Path.AsSpan("\\\\.\\PHYSICALDRIVE".Length), out number);
                        }
                        drive.Number = number.ToString();
                        AddDiskToResult(drive, result);
                    }
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var drive = JsonSerializer.Deserialize<PhysicalDriveInfo>(output);
                if (drive != null)
                {
                    // Extract drive number from path (e.g., \\.\PHYSICALDRIVE0 -> 0)
                    var number = -1;
                    if (drive.Path.StartsWith("\\\\.\\PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(drive.Path.AsSpan("\\\\.\\PHYSICALDRIVE".Length), out number);
                    }
                    drive.Number = number.ToString();
                    AddDiskToResult(drive, result);
                }
            }
        }
        catch (JsonException ex)
        {
            Log.WriteWarningMessage(LOGTAG, "FailedDriveOutputParsing", ex, "Failed to parse output");
        }

        return result;
    }

    private async Task<IDictionary<string, string>> MacListPhysicalDrives(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException(Strings.PlatformNotSupported);

        var output = await RunShellAsync("diskutil list", cancellationToken);

        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(output))
            return result;

        var devices = output.Split("/dev/disk", StringSplitOptions.RemoveEmptyEntries);

        foreach (var device in devices)
        {
            var lines = device.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
                continue;
            if (lines[0].Contains("synthesized", StringComparison.OrdinalIgnoreCase))
                continue; // Skip synthesized disks (e.g., APFS containers)
            var mainLine = lines[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var identifier = mainLine.Last();
            if (string.IsNullOrWhiteSpace(identifier))
                continue; // Skip if we can't determine an identifier
            var path = $"/dev/{identifier}";
            var size = mainLine[^3..^1]; // Size is typically the third-to-last token alongside the unit (e.g., "500 GB")
            size[0] = size[0].Trim()[1..]; // Remove any leading/trailing whitespace, and the leading + or *
            var sizeinBytes = Sizeparser.ParseSize(string.Join(' ', size));

            var info = await RunShellAsync($"diskutil info {path}", cancellationToken);
            var infoLines = info.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var displayName = infoLines.FirstOrDefault(l => l.TrimStart().StartsWith("Device / Media Name:", StringComparison.OrdinalIgnoreCase))?.Split(':', 2)[1].Trim() ?? identifier;
            var guid = infoLines.FirstOrDefault(l => l.TrimStart().StartsWith("Disk / Partition UUID:", StringComparison.OrdinalIgnoreCase))?.Split(':', 2)[1].Trim() ?? null;

            var mountpoints = lines[3..].Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()!).Where(m => !string.IsNullOrWhiteSpace(m)).ToArray();

            var driveInfo = new PhysicalDriveInfo
            {
                Number = identifier["/dev/disk".Length..],
                Path = path,
                Size = (ulong)sizeinBytes,
                DisplayName = displayName,
                Guid = guid,
                MountPoints = mountpoints
            };

            AddDiskToResult(driveInfo, result);
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

    private async Task<string> RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(Strings.PlatformNotSupported);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        return output.ToString();
    }

    private async Task<string> RunShellAsync(string script, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(Strings.PlatformNotSupported);

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments = $"-c \"{script.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        return output.ToString();
    }

    public IDictionary<string, IDictionary<string, string>> GetLookups()
        => new Dictionary<string, IDictionary<string, string>>();
}