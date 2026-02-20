using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Proprietary.DiskImage;

public class WebModule : IWebModule
{
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
        options.TryGetValue(KEY_URL, out var url);
        options.TryGetValue(KEY_PATH, out var path);

        if (op != Operation.ListDestination)
            throw new UserInformationException($"Unsupported operation: {op}", "UnsupportedOperation");

        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return await ListPhysicalDrives(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(url))
            throw new UserInformationException($"Missing URL", "MissingURL");

        using var client = new SourceProvider(url, "", new Dictionary<string, string?>(options));
        await client.Initialize(cancellationToken);

        var targetEntry = await client.GetEntry(path.TrimStart('/'), isFolder: true, cancellationToken).ConfigureAwait(false);
        if (targetEntry == null)
            throw new DirectoryNotFoundException($"Path not found: {path}");

        var result = new Dictionary<string, string>();
        await foreach (var entry in targetEntry.Enumerate(cancellationToken))
        {
            var metadata = await entry.GetMinorMetadata(cancellationToken);
            result[entry.Path] = JsonSerializer.Serialize(metadata);
        }

        return result;
    }

    private async Task<IDictionary<string, string>> ListPhysicalDrives(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(Strings.RestorePlatformNotSupported);

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
            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var disk in doc.RootElement.EnumerateArray())
                {
                    AddDiskToResult(disk, result);
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                AddDiskToResult(doc.RootElement, result);
            }
        }
        catch (JsonException)
        {
            // Ignore parse errors
        }

        return result;
    }

    private void AddDiskToResult(JsonElement disk, Dictionary<string, string> result)
    {
        var number = disk.GetProperty("Number").GetInt32();
        var friendlyName = disk.GetProperty("FriendlyName").GetString();
        var size = disk.GetProperty("Size").GetInt64();
        var path = disk.GetProperty("Path").GetString();

        var displayPath = $"diskimage://\\\\.\\PhysicalDrive{number}/";
        var metadata = new Dictionary<string, string?>
        {
            { "diskimage:Number", number.ToString() },
            { "diskimage:FriendlyName", friendlyName },
            { "diskimage:Size", size.ToString() },
            { "diskimage:DevicePath", path }
        };

        result[displayPath] = JsonSerializer.Serialize(metadata);
    }

    private async Task<string> RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
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

    public IDictionary<string, IDictionary<string, string>> GetLookups()
        => new Dictionary<string, IDictionary<string, string>>();
}